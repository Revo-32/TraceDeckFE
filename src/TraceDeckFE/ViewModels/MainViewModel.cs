using TraceDeckFE.Localization;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TraceDeckFE.Models;
using TraceDeckFE.Overlay;
using TraceDeckFE.Services;

namespace TraceDeckFE.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan VerificationInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RenderDebounce = TimeSpan.FromMilliseconds(120);
    private readonly WindowCatalog _windowCatalog;
    private readonly ForzaWindowTracker _windowTracker;
    private readonly ReferenceImageService _imageService;
    private readonly AutoPaletteService _autoPaletteService;
    private readonly ProjectArchiveService _projectService;
    private readonly OverlayWindow _overlay;
    private readonly ITraceLogger _logger;
    private readonly DispatcherTimer _verificationTimer;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _imageLoadCancellation;
    private CancellationTokenSource? _displayRenderCancellation;
    private CancellationTokenSource? _paletteCancellation;
    private CancellationTokenSource? _projectLoadCancellation;
    private TargetWindowSnapshot _targetState = TargetWindowSnapshot.Disconnected;
    private string _connectionStateText = L.Get("Status.Disconnected");
    private string _connectionDetail = L.Get("Status.NotFound");
    private string _notification = string.Empty;
    private bool _isConnected;
    private bool _isBusy;
    private bool _isGeneratingPalette;
    private bool _suppressDirtyTracking;
    private bool _disposed;

    public MainViewModel(
        ReferenceState reference,
        GuideState guides,
        ReferenceInputSettings inputSettings,
        ColorState colors,
        PaletteState palette,
        ProjectSession project,
        ProjectUiState uiState,
        WindowCatalog windowCatalog,
        ForzaWindowTracker windowTracker,
        ReferenceImageService imageService,
        AutoPaletteService autoPaletteService,
        ProjectArchiveService projectService,
        OverlayWindow overlay,
        ITraceLogger logger)
    {
        Reference = reference;
        Guides = guides;
        InputSettings = inputSettings;
        Colors = colors;
        Palette = palette;
        Project = project;
        UiState = uiState;
        _windowCatalog = windowCatalog;
        _windowTracker = windowTracker;
        _imageService = imageService;
        _autoPaletteService = autoPaletteService;
        _projectService = projectService;
        _overlay = overlay;
        _logger = logger;
        _verificationTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = VerificationInterval };
        _verificationTimer.Tick += OnVerificationTick;
        _windowTracker.StateChanged += OnTargetStateChanged;
        _windowTracker.ConnectionLost += OnConnectionLost;
        Reference.PropertyChanged += OnReferencePropertyChanged;
        Guides.PropertyChanged += OnGuidesPropertyChanged;
        Colors.ContentChanged += OnProjectContentChanged;
        Palette.ContentChanged += OnProjectContentChanged;
        Project.PropertyChanged += OnProjectPropertyChanged;
        _overlay.ColorPicked += OnColorPicked;
        _overlay.ColorPickCanceled += OnColorPickCanceled;
        _overlay.ColorPickFailed += OnColorPickFailed;
        _overlay.UserTransformChanged += OnUserTransformChanged;
        InitializeEditing();
    }

    public event EventHandler<IntRect>? FirstTargetConnected;

    public ReferenceState Reference { get; }
    public GuideState Guides { get; }
    public ReferenceInputSettings InputSettings { get; }
    public ColorState Colors { get; }
    public PaletteState Palette { get; }
    public ProjectSession Project { get; }
    public ProjectUiState UiState { get; }

    public bool IsConnected { get => _isConnected; private set => SetProperty(ref _isConnected, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool IsGeneratingPalette { get => _isGeneratingPalette; private set => SetProperty(ref _isGeneratingPalette, value); }
    public string ConnectionStateText { get => _connectionStateText; private set => SetProperty(ref _connectionStateText, value); }
    public string ConnectionDetail { get => _connectionDetail; private set => SetProperty(ref _connectionDetail, value); }
    public string Notification { get => _notification; private set => SetProperty(ref _notification, value); }
    public string ProjectDisplayName => Project.DisplayName;
    public string OverlaySummary => Reference.HasImage
        ? $"{Reference.Opacity:P0} · {(Reference.IsLocked ? L.Get("Status.Locked") : L.Get("Status.Unlocked"))}" : L.Get("Status.NoReference");

    public string TransformSummary
    {
        get
        {
            if (!Reference.HasImage) return "—";
            var flips = (Reference.FlipHorizontal, Reference.FlipVertical) switch
            {
                (true, true) => " · H/V",
                (true, false) => " · H",
                (false, true) => " · V",
                _ => string.Empty
            };
            return $"{Reference.Scale:P0} · {Reference.Rotation:0}°{flips}";
        }
    }

    public string PositionSummary => Reference.HasImage ? $"{Reference.X:+0;-0;0} / {Reference.Y:+0;-0;0}" : "—";
    public string ImageAssistSummary => !Reference.HasImage ? "—" : $"{(Reference.IsGrayscale ? L.Get("Status.Gray") : L.Get("Status.Color"))} · {Reference.Contrast:+0;-0;0}";
    public string GuideSummary => Guides.HasVisibleGuide
        ? $"{(Guides.IsGridVisible ? L.Format("Status.GridSpacing", Guides.GridSpacing) : L.Get("Status.NoGrid"))} · {Guides.Opacity:P0}" : L.Get("Option.Off");
    public string PaletteSummary => Palette.Items.Count == 1 ? L.Get("Status.OneColor") : L.Format("Status.Colors", Palette.Items.Count);

    public void Initialize()
    {
        ThrowIfDisposed();
        if (Settings.AutomaticallyDetectForza) TryAutoConnect(showFailureMessage: false);
        _verificationTimer.Start();
    }

    public IReadOnlyList<WindowInfo> GetWindowChoices() => _windowCatalog.EnumerateCandidateWindows();

    public bool ConnectToWindow(WindowInfo window)
    {
        ThrowIfDisposed();
        Notification = string.Empty;
        if (!_windowTracker.Attach(window.Handle))
        {
            Notification = L.Get("Notice.WindowUnavailable");
            return false;
        }
        return true;
    }

    public void Reconnect()
    {
        ThrowIfDisposed();
        CancelColorPick();
        _windowTracker.Disconnect();
        _overlay.DetachTarget();
        TryAutoConnect(showFailureMessage: true);
    }

    public Task OpenReferenceAsync(string path) => ReplaceReferenceAsync(
        token => _imageService.LoadAsync(path, GetViewportWidth(), GetViewportHeight(), token),
        System.IO.Path.GetFileName(path));

    public Task OpenClipboardBitmapAsync(BitmapSource bitmap) => ReplaceReferenceAsync(
        token => _imageService.LoadClipboardBitmapAsync(bitmap, GetViewportWidth(), GetViewportHeight(), token),
        L.Get("Ui.ClipboardImage"));

    public void NotifyUnsupportedPaste() => Notification = L.Get("Notice.UnsupportedPaste");
    public void NotifyClipboardUnavailable() => Notification = L.Get("Notice.ClipboardUnavailable");
    public void NotifyClipboardCopied(string value) => Notification = L.Format("Notice.Copied", value);

    public void AdjustScale(double factor)
    {
        if (!Reference.HasImage) return;
        Reference.ZoomAt(new PointD(GetViewportWidth() / 2.0, GetViewportHeight() / 2.0), factor);
        MarkDirty();
    }

    public void AdjustRotation(double degrees) => Reference.RotateBy(degrees);
    public void ResetTransform() { Reference.ResetTransform(); MarkDirty(); }
    public void ResetEffects() => Reference.ResetEffects();
    public void Nudge(double x, double y) { Reference.MoveBy(x, y); MarkDirty(); }
    public void CenterReference() { Reference.Center(GetViewportWidth(), GetViewportHeight()); MarkDirty(); }
    public void FitReference() { Reference.Fit(GetViewportWidth(), GetViewportHeight()); MarkDirty(); }

    public void ToggleColorPicker()
    {
        if (Colors.IsPicking)
        {
            CancelColorPick();
            return;
        }
        if (!Reference.HasImage || !Reference.IsVisible || !_overlay.BeginColorPick())
        {
            Notification = L.Get("Notice.ReferenceUnavailable");
            return;
        }
        Notification = L.Get("Notice.PickInstruction");
    }

    public void CancelColorPick() => _overlay.CancelColorPick();

    public void AddCurrentColor()
    {
        if (Colors.Current is not { } color)
        {
            Notification = L.Get("Notice.SelectColorFirst");
            return;
        }
        Palette.Add(color);
        Notification = L.Get("Notice.ColorAdded");
        OnPropertyChanged(nameof(PaletteSummary));
    }

    public void SelectPaletteItem(PaletteItem? item)
    {
        if (item is null) return;
        Colors.SetColor(item.Color);
        Notification = L.Format("Notice.Loaded", item.Name);
    }

    public void DeletePaletteItem(PaletteItem? item)
    {
        if (item is not null && Palette.Delete(item))
        {
            Notification = L.Get("Notice.ColorDeleted");
            OnPropertyChanged(nameof(PaletteSummary));
        }
    }

    public void ReorderPaletteItem(PaletteItem item, int newIndex)
    {
        if (Palette.Move(item, newIndex)) Notification = L.Get("Notice.PaletteReordered");
    }

    public async Task GeneratePaletteAsync()
    {
        if (Reference.Source is not { } source)
        {
            Notification = L.Get("Notice.ReferenceUnavailable");
            return;
        }
        _paletteCancellation?.Cancel();
        _paletteCancellation?.Dispose();
        _paletteCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        var cancellationToken = _paletteCancellation.Token;
        IsGeneratingPalette = true;
        Notification = L.Get("Notice.Generating");
        try
        {
            var colors = await _autoPaletteService.GenerateAsync(source, Palette.AutoColorCount, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (Reference.Source?.Id != source.Id) return;
            Edit(() => { foreach (var color in colors) Palette.Add(color, isGenerated: true); });
            Notification = colors.Count == 0 ? L.Get("Notice.NoColors") : L.Format("Notice.Generated", colors.Count);
            OnPropertyChanged(nameof(PaletteSummary));
        }
        catch (OperationCanceledException)
        {
            Notification = L.Get("Notice.GenerationCanceled");
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or ImageMagick.MagickException)
        {
            Notification = L.Get("Notice.GenerationFailed");
            _logger.Error("Auto Palette generation failed.", exception);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested) IsGeneratingPalette = false;
        }
    }

    public void NewProject()
    {
        CancelPendingOperations();
        FlushEdits();
        CancelColorPick();
        _suppressDirtyTracking = true;
        try
        {
            Reference.Clear();
            Guides.Reset();
            Colors.Restore(null, Settings.Magnifier);
            Palette.ReplaceAll(Array.Empty<PaletteItem>());
            Project.ResetNew();
        }
        finally { _suppressDirtyTracking = false; }
        History.Reset(CaptureEdit());
        Notification = L.Get("Notice.NewProject");
        RaiseProjectSummaries();
    }

    public async Task<bool> SaveProjectAsync(string? requestedPath = null)
    {
        ThrowIfDisposed();
        var path = requestedPath ?? Project.Path;
        if (string.IsNullOrWhiteSpace(path)) return false;
        path = ProjectArchiveService.EnsureExtension(path);
        if (!await _saveGate.WaitAsync(0, _lifetimeCancellation.Token))
        {
            Notification = L.Get("Notice.SaveBusy");
            return false;
        }

        EndGesture();
        var savedEdit = CaptureEdit();
        var projectId = Project.ProjectId;
        var modifiedUtc = DateTimeOffset.UtcNow;
        var revision = Project.Revision;
        var metadata = new ProjectManifest { ProjectId = projectId, CreatedUtc = Project.CreatedUtc, ModifiedUtc = modifiedUtc };
        var ui = UiState.Capture();
        IsBusy = true;
        Notification = L.Get("Notice.Saving");
        try
        {
            await Task.Run(
                () => _projectService.SaveAsync(path, PackageFrom(savedEdit, metadata, ui), _lifetimeCancellation.Token),
                _lifetimeCancellation.Token);
            if (Project.ProjectId != projectId) return false;
            FlushEdits();
            Project.MarkSaved(path, modifiedUtc, revision);
            History.MarkSaved(savedEdit);
            RememberProject(path);
            await CleanupRecoveryAfterSaveAsync(projectId, modifiedUtc);
            Notification = Project.IsDirty
                ? L.Format("Notice.SavedWithNewEdits", System.IO.Path.GetFileName(path))
                : L.Format("Notice.Saved", System.IO.Path.GetFileName(path));
            _logger.Info($"Saved TDFE project '{path}'.");
            return !Project.IsDirty;
        }
        catch (OperationCanceledException)
        {
            Notification = L.Get("Notice.SaveCanceled");
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ProjectArchiveException or ArgumentException)
        {
            Notification = L.Get("Notice.SaveFailed");
            _logger.Error($"Project save failed for '{path}'.", exception);
            return false;
        }
        finally
        {
            IsBusy = false;
            _saveGate.Release();
        }
    }

    public async Task<bool> OpenProjectAsync(string path)
    {
        ThrowIfDisposed();
        _projectLoadCancellation?.Cancel();
        _projectLoadCancellation?.Dispose();
        _projectLoadCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        var cancellationToken = _projectLoadCancellation.Token;
        IsBusy = true;
        Notification = L.Get("Notice.Opening");
        try
        {
            var package = await _projectService.LoadAsync(path, cancellationToken);
            ReferenceImageSource? decodedSource = null;
            if (package.State.Reference is { } referenceState && package.ReferenceBytes is { } bytes)
            {
                decodedSource = await _imageService.LoadEmbeddedAsync(
                    bytes, referenceState.OriginalFilename, referenceState.SourceFormat,
                    referenceState.SourceKind, GetViewportWidth(), GetViewportHeight(), cancellationToken);
                if (decodedSource.PixelWidth != referenceState.PixelWidth || decodedSource.PixelHeight != referenceState.PixelHeight)
                    throw new ProjectArchiveException(L.Get("Error.ReferenceDimensions"));
            }

            cancellationToken.ThrowIfCancellationRequested();
            ApplyLoadedProject(package, decodedSource, path);
            RememberProject(path);
            Notification = L.Format("Notice.Opened", System.IO.Path.GetFileName(path));
            _logger.Info($"Opened TDFE project '{path}'.");
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ProjectArchiveException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            Notification = exception is ProjectArchiveException archiveException
                ? archiveException.Message : L.Get("Notice.OpenFailed");
            _logger.Error($"Project open failed for '{path}'. Current state was kept.", exception);
            return false;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested) IsBusy = false;
        }
    }

    private async Task ReplaceReferenceAsync(Func<CancellationToken, Task<ReferenceImageSource>> load, string displayName)
    {
        ThrowIfDisposed();
        CancelColorPick();
        _paletteCancellation?.Cancel();
        IsGeneratingPalette = false;
        _imageLoadCancellation?.Cancel();
        _imageLoadCancellation?.Dispose();
        _imageLoadCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        var cancellationToken = _imageLoadCancellation.Token;
        IsBusy = true;
        Notification = string.Empty;
        try
        {
            var source = await load(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Edit(() => Reference.SetImage(source, GetViewportWidth(), GetViewportHeight()));
            Notification = L.Format("Notice.Loaded", displayName);
            _logger.Info($"Loaded {source.Format} reference '{displayName}'.");
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or ArgumentException)
        {
            Notification = exception.Message;
            _logger.Error($"Reference load failed for '{displayName}'.", exception);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested) IsBusy = false;
        }
    }

    private void ApplyLoadedProject(TdfProjectPackage package, ReferenceImageSource? decodedSource, string? path, bool recovered = false)
    {
        CancelPendingOperations(cancelProjectLoad: false);
        FlushEdits();
        CancelColorPick();
        _suppressDirtyTracking = true;
        try
        {
            if (decodedSource is not null && package.State.Reference is { } referenceState)
                Reference.RestoreProject(decodedSource, referenceState, package.State.Overlay, GetViewportWidth(), GetViewportHeight());
            else
            {
                Reference.Clear();
                Reference.IsVisible = package.State.Overlay.Visible;
                Reference.IsLocked = package.State.Overlay.Locked;
                Reference.Opacity = package.State.Overlay.Opacity;
            }
            Guides.Restore(package.State.Guides);
            Colors.Restore(package.State.Color.Current, Settings.Magnifier);
            Palette.ReplaceAll(package.State.Palette.Select(item => new PaletteItem(item.Id, item.Name, item.Color, item.IsGenerated)), package.State.AutoPaletteColorCount);
            Project.AdoptLoaded(package.Manifest, path);
        }
        finally { _suppressDirtyTracking = false; }
        History.Reset(CaptureEdit(), recovered);
        ScheduleDisplayRefresh();
        RaiseProjectSummaries();
    }

    private void ScheduleDisplayRefresh()
    {
        if (_disposed || Reference.Source is null) return;
        _displayRenderCancellation?.Cancel();
        _displayRenderCancellation?.Dispose();
        _displayRenderCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _ = RefreshDisplayAsync(_displayRenderCancellation.Token);
    }

    private async Task RefreshDisplayAsync(CancellationToken cancellationToken)
    {
        var source = Reference.Source;
        if (source is null) return;
        try
        {
            await Task.Delay(RenderDebounce, cancellationToken);
            var targetWidth = Math.Max(1, (int)Math.Ceiling(source.PixelWidth * Reference.Scale));
            var targetHeight = Math.Max(1, (int)Math.Ceiling(source.PixelHeight * Reference.Scale));
            var image = await _imageService.RenderDisplayAsync(source, Reference.IsGrayscale, Reference.Contrast, targetWidth, targetHeight, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Reference.SetDisplayImage(source.Id, image);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is InvalidDataException or ImageMagick.MagickException or ArgumentException)
        {
            Notification = L.Get("Notice.DisplayFailed");
            _logger.Error("Reference display update failed.", exception);
        }
    }

    private double GetViewportWidth() => _targetState.ClientBounds.Width > 0 ? _targetState.ClientBounds.Width : 1280;
    private double GetViewportHeight() => _targetState.ClientBounds.Height > 0 ? _targetState.ClientBounds.Height : 720;

    private void TryAutoConnect(bool showFailureMessage)
    {
        if (_windowTracker.IsConnected) return;
        var forza = _windowCatalog.FindForzaHorizon6();
        if (forza is not null && _windowTracker.Attach(forza.Handle))
        {
            Notification = L.Get("Notice.AutoConnected");
            return;
        }
        SetDisconnectedState();
        if (showFailureMessage) Notification = L.Get("Notice.ManualConnect");
    }

    private void OnVerificationTick(object? sender, EventArgs e)
    {
        if (_windowTracker.IsConnected) _windowTracker.Verify();
        else if (Settings.AutomaticallyDetectForza) TryAutoConnect(showFailureMessage: false);
    }

    private void OnTargetStateChanged(object? sender, TargetWindowSnapshot state)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (!dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(() => ApplyTargetState(state));
            return;
        }
        ApplyTargetState(state);
    }

    private void ApplyTargetState(TargetWindowSnapshot state)
    {
        var wasConnected = IsConnected;
        _targetState = state;
        IsConnected = state.Exists;
        if (ReferenceViewportPolicy.ShouldUpdate(state))
        {
            FlushEdits();
            _suppressDirtyTracking = true;
            try { Reference.UpdateViewport(state.ClientBounds.Width, state.ClientBounds.Height); }
            finally { _suppressDirtyTracking = false; }
        }
        _overlay.ApplyTargetState(state);
        if (!state.Exists)
        {
            SetDisconnectedState();
            return;
        }
        ConnectionStateText = L.Get("Status.Connected");
        ConnectionDetail = $"{state.Title} · {state.ClientBounds.Width} × {state.ClientBounds.Height}";
        if (!wasConnected) FirstTargetConnected?.Invoke(this, state.ClientBounds);
    }

    private void OnConnectionLost(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current.Dispatcher;
        _ = dispatcher.BeginInvoke(() =>
        {
            CancelColorPick();
            SetDisconnectedState();
            _overlay.DetachTarget();
            Notification = L.Get("Notice.TargetClosed");
        });
    }

    private void SetDisconnectedState()
    {
        _targetState = TargetWindowSnapshot.Disconnected;
        IsConnected = false;
        ConnectionStateText = L.Get("Status.Disconnected");
        ConnectionDetail = L.Get("Status.Waiting");
    }

    private void OnReferencePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(OverlaySummary));
        OnPropertyChanged(nameof(TransformSummary));
        OnPropertyChanged(nameof(PositionSummary));
        OnPropertyChanged(nameof(ImageAssistSummary));
        if (e.PropertyName is nameof(ReferenceState.IsGrayscale) or nameof(ReferenceState.Contrast) or nameof(ReferenceState.Scale) or nameof(ReferenceState.Source))
            ScheduleDisplayRefresh();
        if (e.PropertyName is nameof(ReferenceState.X) or nameof(ReferenceState.Y) or nameof(ReferenceState.Scale) or nameof(ReferenceState.Source) or nameof(ReferenceState.Rotation) or nameof(ReferenceState.FlipHorizontal) or
            nameof(ReferenceState.FlipVertical) or nameof(ReferenceState.IsGrayscale) or nameof(ReferenceState.Contrast) or
            nameof(ReferenceState.Opacity) or nameof(ReferenceState.IsVisible) or nameof(ReferenceState.IsLocked))
            MarkDirty();
    }

    private void OnGuidesPropertyChanged(object? sender, PropertyChangedEventArgs e) { OnPropertyChanged(nameof(GuideSummary)); MarkDirty(); }
    private void OnProjectContentChanged(object? sender, EventArgs e) { MarkDirty(); OnPropertyChanged(nameof(PaletteSummary)); }
    private void OnProjectPropertyChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(ProjectDisplayName));

    private void OnColorPicked(object? sender, ColorPickedEventArgs e)
    {
        Colors.SetColor(e.Color);
        Notification = e.Color.Alpha == 0 ? L.Format("Notice.Transparent", e.Color.HexRgb)
            : e.Color.Alpha < 255 ? L.Format("Notice.Alpha", e.Color.HexRgb, e.Color.Alpha) : L.Format("Notice.Picked", e.Color.HexRgb);
    }

    private void OnColorPickCanceled(object? sender, EventArgs e) => Notification = L.Get("Notice.PickCanceled");
    private void OnColorPickFailed(object? sender, string message) => Notification = message;
    private void OnUserTransformChanged(object? sender, EventArgs e) => MarkDirty();
    private void MarkDirty() { if (!_suppressDirtyTracking) QueueEditObservation(); }

    private void RaiseProjectSummaries()
    {
        OnPropertyChanged(nameof(ProjectDisplayName));
        OnPropertyChanged(nameof(OverlaySummary));
        OnPropertyChanged(nameof(TransformSummary));
        OnPropertyChanged(nameof(PositionSummary));
        OnPropertyChanged(nameof(ImageAssistSummary));
        OnPropertyChanged(nameof(GuideSummary));
        OnPropertyChanged(nameof(PaletteSummary));
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeReliability();
        _lifetimeCancellation.Cancel();
        _verificationTimer.Stop();
        _verificationTimer.Tick -= OnVerificationTick;
        _windowTracker.StateChanged -= OnTargetStateChanged;
        _windowTracker.ConnectionLost -= OnConnectionLost;
        Reference.PropertyChanged -= OnReferencePropertyChanged;
        Guides.PropertyChanged -= OnGuidesPropertyChanged;
        Colors.ContentChanged -= OnProjectContentChanged;
        Palette.ContentChanged -= OnProjectContentChanged;
        Project.PropertyChanged -= OnProjectPropertyChanged;
        _overlay.ColorPicked -= OnColorPicked;
        _overlay.ColorPickCanceled -= OnColorPickCanceled;
        _overlay.ColorPickFailed -= OnColorPickFailed;
        _overlay.UserTransformChanged -= OnUserTransformChanged;
        _imageLoadCancellation?.Cancel(); _imageLoadCancellation?.Dispose();
        _displayRenderCancellation?.Cancel(); _displayRenderCancellation?.Dispose();
        _paletteCancellation?.Cancel(); _paletteCancellation?.Dispose();
        _projectLoadCancellation?.Cancel(); _projectLoadCancellation?.Dispose();
        _lifetimeCancellation.Dispose();
        _saveGate.Dispose();
        _windowTracker.Dispose();
        _overlay.Dispose();
    }
}
