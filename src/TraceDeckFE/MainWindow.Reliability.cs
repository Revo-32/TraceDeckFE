using TraceDeckFE.Localization;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;
using TraceDeckFE.Models;
using TraceDeckFE.Services;
using TraceDeckFE.Views;

namespace TraceDeckFE;

public partial class MainWindow
{
    private NativeHotkeyHost? _hotkeys;
    private Guid? _discardPendingId;
    private bool _sliderGesture;
    private bool _starting = true;
    private void InitializeReliabilityUi()
    {
        Loaded += OnReliabilityLoaded;
        _viewModel.SettingsUpdated += OnSettingsUpdated;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        AddHandler(PreviewMouseLeftButtonDownEvent,new MouseButtonEventHandler(OnGestureMouseDown),true);
        AddHandler(PreviewMouseLeftButtonUpEvent,new MouseButtonEventHandler(OnGestureMouseUp),true);
        AddHandler(LostMouseCaptureEvent,new MouseEventHandler(OnGestureCaptureLost),true);
    }
    private async void OnReliabilityLoaded(object sender, RoutedEventArgs e)
    {
        IsEnabled = false;
        _hotkeys = new NativeHotkeyHost(new WindowInteropHelper(this).Handle, () => _viewModel.TargetHandle, () => IsEnabled && !_starting, Dispatcher);
        _hotkeys.Invoked += OnGlobalShortcut;
        _hotkeys.Service.StatusChanged += OnHotkeyStatusChanged;
        _hotkeys.Service.Configure(_viewModel.Settings.Shortcuts);
        if (!_hotkeys.HasForegroundHook) _viewModel.Notify(L.Get("Notice.HotkeyTrackingUnavailable"));
        try
        {
            var candidates = await _viewModel.FindRecoveryAsync();
            bool decisionMade = false;
            foreach (var candidate in candidates)
            {
                var path = candidate.Snapshot.ManualPath;
                var saved = !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path);
                var name = saved ? System.IO.Path.GetFileNameWithoutExtension(path) : L.Get("Dialog.UnsavedTitle");
                var message = L.Format("Dialog.RecoveryMessage", name, candidate.Snapshot.CapturedUtc.LocalDateTime);
                if (saved) message += L.Format("Dialog.LastSaveTime", System.IO.File.GetLastWriteTime(path!));
                var dialog = new ChoiceDialog(L.Get("Dialog.RecoveryTitle"),message,L.Get("Ui.RestoreRecovery"),saved ? L.Get("Ui.OpenLastSave") : L.Get("Ui.Discard")) { Owner = this };
                dialog.SetChoiceHelp(L.Get("Tip.RestoreRecovery"), L.Get(saved ? "Tip.OpenLastSave" : "Tip.DiscardRecovery"));
                dialog.ShowDialog(); decisionMade = true;
                if (dialog.Choice == 0)
                {
                    if (await _viewModel.RestoreRecoveryAsync(candidate)) break;
                    continue;
                }
                if (dialog.Choice == 1)
                {
                    if (saved)
                    {
                        if (await _viewModel.OpenProjectAsync(path!))
                            await _viewModel.DismissRecoveryAsync(candidate.Snapshot.Manifest.ProjectId,candidate.Snapshot.CapturedUtc);
                        break;
                    }
                    await _viewModel.DismissRecoveryAsync(candidate.Snapshot.Manifest.ProjectId,candidate.Snapshot.CapturedUtc);
                    continue;
                }
                break; // Closing the decision leaves snapshots intact and does not hide them with last-save restore.
            }
            if (!decisionMade && _viewModel.Settings.RememberLastProject && _viewModel.Settings.RestorePreviousSession &&
                _viewModel.Settings.LastProjectPath is { Length: > 0 } lastPath)
                await _viewModel.OpenProjectAsync(lastPath);
        }
        finally
        {
            _starting = false; IsEnabled = true; _hotkeys.RefreshContext();
            // After an asynchronous startup/recovery dialog there may be no focused child.
            // Keep local shortcuts available before the user's first pointer click.
            Focusable = true;
            if (IsActive) Keyboard.Focus(this);
        }
    }
    private void OnSettingsUpdated(object? sender, EventArgs e) => _hotkeys?.Service.Configure(_viewModel.Settings.Shortcuts);
    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(SystemParameters.WorkArea)) _viewModel.RefreshWorkspace(); }
    private void OnHotkeyStatusChanged(object? sender, EventArgs e)
    { if (_hotkeys?.Service.Conflicts.Count > 0) _viewModel.Notify(string.Join(" ",_hotkeys.Service.Conflicts)); }
    private async void OnGlobalShortcut(object? sender, ShortcutAction action) => await ExecuteShortcutAsync(action);
    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        _viewModel.EndGesture();
        var dialog = new SettingsWindow(_viewModel.Settings, ProbeShortcut) { Owner = this };
        _hotkeys?.Service.SetContext(false);
        dialog.ShowDialog(); _hotkeys?.RefreshContext();
    }
    private string? ProbeShortcut(ShortcutBinding binding)
    {
        if (!binding.IsGlobal || _hotkeys is null) return null;
        const int probeId = 0x6FFF;
        if (!_hotkeys.Register(probeId,binding)) return L.Format("Shortcut.InUse", binding.Gesture);
        _hotkeys.Unregister(probeId); return null;
    }
    private void OnUndoClick(object sender,RoutedEventArgs e) { CommitFocusedText(); _viewModel.Undo(); }
    private void OnRedoClick(object sender,RoutedEventArgs e) { CommitFocusedText(); _viewModel.Redo(); }
    private void OnResetGuidesClick(object sender,RoutedEventArgs e) => _viewModel.ResetGuides();
    private void OnResetAllClick(object sender,RoutedEventArgs e) => _viewModel.ResetAll();
    private void OnWidthDrag(object sender,DragDeltaEventArgs e) => _viewModel.UiState.ControllerWidth += e.HorizontalChange;
    private void OnGestureMouseDown(object sender,MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject obj && FindAncestor<Slider>(obj) is not null)
        { _sliderGesture = true; _viewModel.BeginGesture(); }
    }
    private void OnGestureMouseUp(object sender,MouseButtonEventArgs e) { if (_sliderGesture) { _sliderGesture = false; _viewModel.EndGesture(); } }
    private void OnGestureCaptureLost(object sender,MouseEventArgs e) { if (_sliderGesture && Mouse.LeftButton != MouseButtonState.Pressed) { _sliderGesture = false; _viewModel.EndGesture(); } }
    private void CommitFocusedText()
    {
        if (Keyboard.FocusedElement is TextBox text) text.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }
    private async Task CommitDiscardAsync()
    {
        if (_discardPendingId is { } id) { await _viewModel.DismissRecoveryAsync(id); _discardPendingId = null; }
    }
    private async Task ExecuteShortcutAsync(ShortcutAction action)
    {
        if (_starting || !IsEnabled) return;
        switch (action)
        {
            case ShortcutAction.NewProject: if (await ConfirmUnsavedChangesAsync()) { _viewModel.NewProject(); await CommitDiscardAsync(); } break;
            case ShortcutAction.OpenImage: OnOpenImageClick(this,new RoutedEventArgs()); break;
            case ShortcutAction.OpenProject: await OpenProjectFromDialogAsync(); break;
            case ShortcutAction.Save: await SaveProjectWithDialogAsync(false); break;
            case ShortcutAction.SaveAs: await SaveProjectWithDialogAsync(true); break;
            case ShortcutAction.Undo: CommitFocusedText(); _viewModel.Undo(); break;
            case ShortcutAction.Redo: case ShortcutAction.RedoAlternate: CommitFocusedText(); _viewModel.Redo(); break;
            case ShortcutAction.Fit: _viewModel.Edit(_viewModel.FitReference); break;
            case ShortcutAction.ActualSize: _viewModel.ActualSize(); break;
            case ShortcutAction.Cancel: _viewModel.CancelColorPick(); _viewModel.EndGesture(); Mouse.Capture(null); break;
            case ShortcutAction.ToggleVisible: _viewModel.Edit(() => _viewModel.Reference.IsVisible = !_viewModel.Reference.IsVisible); break;
            case ShortcutAction.ToggleLock: _viewModel.Edit(() => _viewModel.Reference.IsLocked = !_viewModel.Reference.IsLocked); break;
            case ShortcutAction.PickColor: _viewModel.ToggleColorPicker(); break;
            case ShortcutAction.OpacityDown: _viewModel.Edit(() => _viewModel.Reference.Opacity -= .05); break;
            case ShortcutAction.OpacityUp: _viewModel.Edit(() => _viewModel.Reference.Opacity += .05); break;
            case ShortcutAction.ToggleGrid: _viewModel.Edit(() => _viewModel.Guides.IsGridVisible = !_viewModel.Guides.IsGridVisible); break;
            case ShortcutAction.ToggleCenters:
                _viewModel.Edit(() => { var enabled = !(_viewModel.Guides.IsHorizontalCenterVisible && _viewModel.Guides.IsVerticalCenterVisible);
                    _viewModel.Guides.IsHorizontalCenterVisible = enabled; _viewModel.Guides.IsVerticalCenterVisible = enabled; }); break;
            default:
                if (action is >= ShortcutAction.MoveLeft and <= ShortcutAction.MoveDownFast && _viewModel.Reference.HasImage)
                {
                    _viewModel.BeginBurst("arrows");
                    var fast = action >= ShortcutAction.MoveLeftFast;
                    var direction = ((int)action - (int)ShortcutAction.MoveLeft) % 4;
                    var step = fast ? _viewModel.Settings.ShiftArrowMovement : _viewModel.Settings.ArrowMovement;
                    _viewModel.Nudge(direction == 0 ? -step : direction == 1 ? step : 0,direction == 2 ? -step : direction == 3 ? step : 0);
                }
                break;
        }
    }
    private void DisposeReliabilityUi()
    {
        _hotkeys?.Dispose(); Loaded -= OnReliabilityLoaded;
        _viewModel.SettingsUpdated -= OnSettingsUpdated;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
    }
}
