using TraceDeckFE.Localization;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using TraceDeckFE.Models;
using TraceDeckFE.Services;

namespace TraceDeckFE.ViewModels;

public sealed partial class MainViewModel
{
    public ApplicationSettings Settings { get; private set; } = new();
    public SessionHistory<ProjectEditSnapshot> History { get; private set; } = null!;
    private SettingsService? _settingsService;
    private RecoveryService? _recovery;
    private readonly DispatcherTimer _gestureTimer = new() { Interval = SessionHistory<ProjectEditSnapshot>.BurstDelay };
    private readonly DispatcherTimer _settingsTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _autosaveTimer = new(DispatcherPriority.Background);
    private bool _editQueued, _applyingSettings, _autosaveRunning;
    private LayoutMode _effectiveLayout = LayoutMode.Compact;
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ReferenceImageSource, SourceDigest> _sourceDigests = new();
    private sealed class SourceDigest { public string Hash { get; } public SourceDigest(ReferenceImageSource source) => Hash = ProjectArchiveService.ComputeSha256(source.OriginalBytes); }
    public bool CanUndo => History.CanUndo;
    public bool CanRedo => History.CanRedo;
    public nint TargetHandle => _windowTracker.TargetHandle;
    public LayoutMode EffectiveLayout { get => _effectiveLayout; private set => SetProperty(ref _effectiveLayout, value); }
    public bool IsCompact => EffectiveLayout == LayoutMode.Compact;
    public int HsbColumns => IsCompact || UiState.ControllerWidth < 400 ? 1 : 3;
    public bool CompactDensity => Settings.Density == UiDensity.Compact || Settings.Density == UiDensity.Automatic && IsCompact;
    public double CardPadding => CompactDensity ? 10 : 14;
    public Thickness CardContentMargin => new(CardPadding,0,CardPadding,CardPadding);
    public Thickness CardHeaderPadding => new(CardPadding,CompactDensity ? 10 : 12,CardPadding,CompactDensity ? 10 : 12);
    public event EventHandler? SettingsUpdated;

    private void InitializeEditing()
    {
        History = new(CaptureEdit(), ProjectEditSnapshot.Equivalent);
        History.Changed += OnHistoryChanged;
        _gestureTimer.Tick += (_, _) => { _gestureTimer.Stop(); EndGesture(); };
        _settingsTimer.Tick += async (_, _) => { _settingsTimer.Stop(); await FlushSettingsAsync(); };
        _autosaveTimer.Tick += async (_, _) => await AutosaveAsync();
        _overlay.GestureStarted += OnOverlayGestureStarted;
        _overlay.GestureCompleted += OnOverlayGestureCompleted;
        _overlay.ZoomGesture += OnOverlayZoomGesture;
    }
    public void ConfigureReliability(ApplicationSettings settings, SettingsService storage, RecoveryService recovery)
    {
        Settings = settings; _settingsService = storage; _recovery = recovery;
        Settings.PropertyChanged += OnSettingsChanged;
        UiState.ContentChanged += OnUiPreferenceChanged;
        Colors.PropertyChanged += OnColorPreferenceChanged;
        _applyingSettings = true;
        if (Settings.RememberFoldedCards) UiState.Restore(Settings.FoldedCards);
        _applyingSettings = false;
        ApplySettings(forceWidth: true);
        OnPropertyChanged(nameof(Settings));
    }
    public void Notify(string message) => Notification = message;
    private ProjectEditSnapshot CaptureEdit() => ProjectEditSnapshot.Capture(Reference, Guides, Colors, Palette);
    private void QueueEditObservation()
    {
        if (_editQueued || _disposed) return;
        _editQueued = true;
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
        {
            // Continuous pointer input is captured at gesture end, not deep-snapshotted on every move.
            if (!_disposed && !History.IsGrouping) FlushEdits();
        }));
    }
    public void FlushEdits()
    {
        if (!_editQueued) return;
        _editQueued = false;
        if (!_suppressDirtyTracking) History.Observe(CaptureEdit());
    }
    public void BeginGesture() { FlushEdits(); _gestureTimer.Stop(); History.BeginGesture(); }
    public void EndGesture() { _gestureTimer.Stop(); FlushEdits(); History.EndGesture(); }
    public void BeginBurst(string key)
    {
        FlushEdits(); History.TouchBurst(key, DateTimeOffset.UtcNow);
        _gestureTimer.Stop(); _gestureTimer.Start();
    }
    public void Edit(Action action)
    {
        BeginGesture();
        try { action(); }
        finally { QueueEditObservation(); EndGesture(); }
    }
    public void Undo()
    {
        EndGesture(); CancelColorPick();
        ApplyHistory(History.Undo());
    }
    public void Redo()
    {
        EndGesture(); CancelColorPick();
        ApplyHistory(History.Redo());
    }
    private void ApplyHistory(ProjectEditSnapshot snapshot)
    {
        CancelPendingOperations();
        _suppressDirtyTracking = true;
        try { snapshot.Apply(Reference, Guides, Colors, Palette, GetViewportWidth(), GetViewportHeight()); }
        finally { _suppressDirtyTracking = false; }
        ScheduleDisplayRefresh(); RaiseProjectSummaries();
    }
    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        Project.SetEditDirty(History.IsDirty);
        OnPropertyChanged(nameof(CanUndo)); OnPropertyChanged(nameof(CanRedo));
    }
    private void OnOverlayGestureStarted(object? sender, EventArgs e) => BeginGesture();
    private void OnOverlayGestureCompleted(object? sender, EventArgs e) => EndGesture();
    private void OnOverlayZoomGesture(object? sender, EventArgs e) => BeginBurst("wheel");
    public void ResetGuides() => Edit(Guides.Reset);
    public void ResetAll() => Edit(() =>
    {
        Reference.ResetTransform(); Reference.Center(GetViewportWidth(), GetViewportHeight());
        Reference.ResetEffects(); Guides.Reset(); Reference.Opacity = .62; Reference.IsVisible = true;
    });
    public void ActualSize() => Edit(() => { if (Reference.HasImage) Reference.ZoomAt(
        new PointD(Reference.X + Reference.ImageWidth * Reference.Scale / 2, Reference.Y + Reference.ImageHeight * Reference.Scale / 2), 1 / Reference.Scale); });

    private void CancelPendingOperations(bool cancelProjectLoad = true)
    {
        _imageLoadCancellation?.Cancel(); _paletteCancellation?.Cancel(); _displayRenderCancellation?.Cancel();
        if (cancelProjectLoad) _projectLoadCancellation?.Cancel();
        IsGeneratingPalette = false; IsBusy = false;
    }
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        ApplySettings(e.PropertyName is nameof(ApplicationSettings.Layout) or nameof(ApplicationSettings.RememberWidthPerLayout));
        if (!Settings.RememberLastProject && Settings.LastProjectPath is not null) Settings.LastProjectPath = null;
        if (e.PropertyName == nameof(ApplicationSettings.ControllerWidth) && !_applyingSettings) UiState.ControllerWidth = Settings.ControllerWidth;
        ScheduleSettingsSave();
        SettingsUpdated?.Invoke(this, EventArgs.Empty);
    }
    public void RefreshWorkspace() => ApplySettings();
    private void ApplySettings(bool forceWidth = false)
    {
        if (_applyingSettings) return;
        _applyingSettings = true;
        try
        {
            var work = SystemParameters.WorkArea;
            var layout = LayoutPolicy.Resolve(Settings.Layout, work.Width, work.Height);
            if (layout != EffectiveLayout || forceWidth)
            {
                EffectiveLayout = layout;
                UiState.ControllerWidth = Settings.WidthFor(layout);
                Settings.ControllerWidth = UiState.ControllerWidth;
            }
            InputSettings.ConfirmReplacement = Settings.ConfirmReferenceReplacement;
            Colors.MagnifierEnabled = Settings.Magnifier;
            Colors.Precision = Settings.HsbDecimalPlaces;
            Palette.Precision = Settings.HsbDecimalPlaces;
            _overlay.ZoomFactor = 1 + Settings.ZoomStepPercent / 100;
            _overlay.ZoomTowardCursor = Settings.ZoomTowardCursor;
            _autosaveTimer.Interval = TimeSpan.FromSeconds(Settings.AutosaveIntervalSeconds);
            if (Settings.AutosaveEnabled) _autosaveTimer.Start(); else _autosaveTimer.Stop();
            OnPropertyChanged(nameof(IsCompact)); OnPropertyChanged(nameof(HsbColumns)); OnPropertyChanged(nameof(CompactDensity)); OnPropertyChanged(nameof(CardPadding));
            OnPropertyChanged(nameof(CardContentMargin)); OnPropertyChanged(nameof(CardHeaderPadding));
        }
        finally { _applyingSettings = false; }
    }
    private void OnUiPreferenceChanged(object? sender, EventArgs e)
    {
        if (_applyingSettings) return;
        _applyingSettings = true;
        try
        {
            Settings.RememberWidth(EffectiveLayout, UiState.ControllerWidth);
            if (Settings.RememberFoldedCards) Settings.FoldedCards = UiState.Capture();
        }
        finally { _applyingSettings = false; }
        OnPropertyChanged(nameof(HsbColumns));
        ScheduleSettingsSave();
    }
    private void OnColorPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_applyingSettings && e.PropertyName == nameof(ColorState.MagnifierEnabled)) Settings.Magnifier = Colors.MagnifierEnabled;
    }
    private void ScheduleSettingsSave() { _settingsTimer.Stop(); _settingsTimer.Start(); }
    public async Task FlushSettingsAsync()
    {
        _settingsTimer.Stop();
        if (_settingsService is null) return;
        var snapshot = JsonSerializer.SerializeToUtf8Bytes(Settings, SettingsService.JsonOptions);
        try { await _settingsService.SaveAsync(snapshot); }
        catch (Exception e) when (RecoveryService.IsRecoverable(e)) { Notification = L.Get("Notice.SettingsSaveFailed"); _logger.Error("Settings save failed",e); }
    }
    private void RememberProject(string path) { if (Settings.RememberLastProject) Settings.LastProjectPath = Path.GetFullPath(path); }

    private TdfProjectPackage PackageFrom(ProjectEditSnapshot edit, ProjectManifest manifest, ProjectUiStateData ui)
    {
        var source = edit.Source;
        var hash = source is null ? null : _sourceDigests.GetValue(source, s => new SourceDigest(s)).Hash;
        return new(manifest with { ReferenceEntry = source is null ? null : ProjectArchiveService.CreateReferenceEntry(source.Name,source.Format), ReferenceSha256 = hash },
            edit.State with { Ui = ui }, source?.OriginalBytes);
    }
    public async Task<bool> AutosaveAsync()
    {
        FlushEdits();
        if (_autosaveRunning || _recovery is null || !Settings.AutosaveEnabled || !Project.IsDirty || IsBusy || History.IsGrouping) return false;
        var edit = CaptureEdit(); var ui = UiState.Capture(); var path = Project.Path;
        var manifest = new ProjectManifest { ProjectId = Project.ProjectId, CreatedUtc = Project.CreatedUtc, ModifiedUtc = DateTimeOffset.UtcNow };
        var fingerprint = (edit.Source?.Id.ToString() ?? "none") + edit.Fingerprint;
        _autosaveRunning = true;
        try { return await Task.Run(() => _recovery.WriteSnapshotAsync(PackageFrom(edit,manifest,ui),path,true,fingerprint,_lifetimeCancellation.Token)); }
        catch (OperationCanceledException) { return false; }
        catch (Exception e) when (RecoveryService.IsRecoverable(e)) { Notification = L.Get("Notice.AutosaveFailed"); _logger.Error("Autosave failed",e); return false; }
        finally { _autosaveRunning = false; }
    }
    public Task<IReadOnlyList<RecoveryCandidate>> FindRecoveryAsync() => _recovery is null ? Task.FromResult<IReadOnlyList<RecoveryCandidate>>([]) : Task.Run(() => _recovery.FindCandidatesAsync(_lifetimeCancellation.Token));
    public async Task<bool> RestoreRecoveryAsync(RecoveryCandidate candidate)
    {
        try
        {
            var package = candidate.Package;
            ReferenceImageSource? source = null;
            if (package.State.Reference is { } state && package.ReferenceBytes is { } bytes)
            {
                source = await _imageService.LoadEmbeddedAsync(bytes,state.OriginalFilename,state.SourceFormat,state.SourceKind,GetViewportWidth(),GetViewportHeight(),_lifetimeCancellation.Token);
                if (source.PixelWidth != state.PixelWidth || source.PixelHeight != state.PixelHeight) throw new ProjectArchiveException("Recovery image dimensions do not match.");
            }
            ApplyLoadedProject(package,source,candidate.Snapshot.ManualPath,recovered:true);
            Notification = L.Get("Notice.Recovered");
            return true;
        }
        catch (Exception e) when (RecoveryService.IsRecoverable(e) || e is InvalidDataException)
        { Notification = L.Get("Notice.RecoveryFailed"); _logger.Error("Recovery restore failed",e); return false; }
    }
    public async Task DismissRecoveryAsync(Guid? id = null, DateTimeOffset? through = null)
    {
        if (_recovery is null) return;
        try { await _recovery.DismissAsync(id ?? Project.ProjectId,through ?? DateTimeOffset.UtcNow); }
        catch (Exception e) when (RecoveryService.IsRecoverable(e)) { Notification = L.Get("Notice.DismissFailed"); _logger.Error("Recovery dismissal failed",e); }
    }
    private async Task CleanupRecoveryAfterSaveAsync(Guid id, DateTimeOffset saved)
    {
        if (_recovery is null) return;
        try { await _recovery.ManualSaveSucceededAsync(id,saved); }
        catch (Exception e) when (RecoveryService.IsRecoverable(e)) { _logger.Warning("Recovery cleanup deferred: " + e.Message); }
    }
    private void DisposeReliability()
    {
        _gestureTimer.Stop(); _settingsTimer.Stop(); _autosaveTimer.Stop();
        Settings.PropertyChanged -= OnSettingsChanged; UiState.ContentChanged -= OnUiPreferenceChanged;
        Colors.PropertyChanged -= OnColorPreferenceChanged; History.Changed -= OnHistoryChanged;
        _overlay.GestureStarted -= OnOverlayGestureStarted; _overlay.GestureCompleted -= OnOverlayGestureCompleted;
        _overlay.ZoomGesture -= OnOverlayZoomGesture;
    }
}
