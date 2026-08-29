using TraceDeckFE.Localization;
using Microsoft.Win32;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TraceDeckFE.Models;
using TraceDeckFE.Services;
using TraceDeckFE.ViewModels;
using TraceDeckFE.Views;

namespace TraceDeckFE;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _positionedForFirstTarget;
    private bool _closeApproved;
    private bool _closePromptActive;
    private System.Windows.Point _paletteDragStart;
    private PaletteItem? _draggedPaletteItem;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        _viewModel.FirstTargetConnected += OnFirstTargetConnected;
        Closing += OnClosing;
        Closed += OnClosed;
        InitializeReliabilityUi();
    }

    private async void OnOpenImageClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = L.Get("Dialog.OpenImage"),
            Filter = ReferenceImageService.OpenFileDialogFilter,
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true) await OpenReferenceWithConfirmationAsync(dialog.FileName);
    }

    private async void OnNewProjectClick(object sender, RoutedEventArgs e)
    {
        if (await ConfirmUnsavedChangesAsync()) { _viewModel.NewProject(); await CommitDiscardAsync(); }
    }

    private async void OnOpenProjectClick(object sender, RoutedEventArgs e) => await OpenProjectFromDialogAsync();
    private async void OnSaveProjectClick(object sender, RoutedEventArgs e) => await SaveProjectWithDialogAsync(forceSaveAs: false);
    private async void OnSaveAsProjectClick(object sender, RoutedEventArgs e) => await SaveProjectWithDialogAsync(forceSaveAs: true);

    private void OnSelectWindowClick(object sender, RoutedEventArgs e)
    {
        var dialog = new WindowPickerDialog(_viewModel.GetWindowChoices()) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedWindow is not null) _viewModel.ConnectToWindow(dialog.SelectedWindow);
    }

    private void OnReconnectClick(object sender, RoutedEventArgs e) => _viewModel.Reconnect();
    private void OnScaleDownClick(object sender, RoutedEventArgs e) => _viewModel.AdjustScale(1.0 / (1 + _viewModel.Settings.ZoomStepPercent / 100));
    private void OnScaleUpClick(object sender, RoutedEventArgs e) => _viewModel.AdjustScale(1 + _viewModel.Settings.ZoomStepPercent / 100);
    private void OnRotateLeftClick(object sender, RoutedEventArgs e) => _viewModel.AdjustRotation(-5);
    private void OnRotateRightClick(object sender, RoutedEventArgs e) => _viewModel.AdjustRotation(5);
    private void OnResetTransformClick(object sender, RoutedEventArgs e) => _viewModel.ResetTransform();
    private void OnResetEffectsClick(object sender, RoutedEventArgs e) => _viewModel.ResetEffects();
    private void OnNudgeLeftClick(object sender, RoutedEventArgs e) => _viewModel.Nudge(-1, 0);
    private void OnNudgeRightClick(object sender, RoutedEventArgs e) => _viewModel.Nudge(1, 0);
    private void OnNudgeUpClick(object sender, RoutedEventArgs e) => _viewModel.Nudge(0, -1);
    private void OnNudgeDownClick(object sender, RoutedEventArgs e) => _viewModel.Nudge(0, 1);
    private void OnCenterClick(object sender, RoutedEventArgs e) => _viewModel.CenterReference();
    private void OnFitClick(object sender, RoutedEventArgs e) => _viewModel.FitReference();
    private void OnPickColorClick(object sender, RoutedEventArgs e) => _viewModel.ToggleColorPicker();
    private void OnAddPaletteClick(object sender, RoutedEventArgs e) => _viewModel.AddCurrentColor();
    private async void OnGeneratePaletteClick(object sender, RoutedEventArgs e) => await _viewModel.GeneratePaletteAsync();
    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnCopyColorComponentClick(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.Colors.HasColor || sender is not Button { Tag: string component }) return;
        var value = component switch
        {
            "H" => _viewModel.Colors.HueText,
            "S" => _viewModel.Colors.SaturationText,
            "B" => _viewModel.Colors.BrightnessText,
            _ => string.Empty
        };
        CopyToClipboard(value);
    }

    private void OnCopyHsbClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Colors.HasColor) CopyToClipboard(_viewModel.Colors.Hsb.ToDisplayString(_viewModel.Colors.Precision));
    }

    private void CopyToClipboard(string value)
    {
        try
        {
            Clipboard.SetText(value);
            _viewModel.NotifyClipboardCopied(value);
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            _viewModel.NotifyClipboardUnavailable();
        }
    }

    private void OnPaletteSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PaletteList.SelectedItem is PaletteItem item) _viewModel.SelectPaletteItem(item);
        PaletteList.SelectedItem = null;
    }

    private void OnDeletePaletteClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PaletteItem item }) _viewModel.DeletePaletteItem(item);
        e.Handled = true;
    }

    private void OnPaletteMouseDown(object sender, MouseButtonEventArgs e)
    {
        _paletteDragStart = e.GetPosition(PaletteList);
        var source = e.OriginalSource as DependencyObject;
        _draggedPaletteItem = FindAncestor<TextBox>(source) is null && FindAncestor<Button>(source) is null
            ? GetPaletteItem(source)
            : null;
    }

    private void OnPaletteMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedPaletteItem is null || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(PaletteList);
        if (Math.Abs(point.X - _paletteDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _paletteDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _ = DragDrop.DoDragDrop(PaletteList, _draggedPaletteItem, DragDropEffects.Move);
        _draggedPaletteItem = null;
    }

    private void OnPaletteDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(PaletteItem)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnPaletteDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(PaletteItem)) is not PaletteItem dragged) return;
        var target = GetPaletteItem(e.OriginalSource as DependencyObject);
        var index = target is null ? _viewModel.Palette.Items.Count - 1 : _viewModel.Palette.Items.IndexOf(target);
        _viewModel.ReorderPaletteItem(dragged, Math.Max(0, index));
        e.Handled = true;
    }

    private PaletteItem? GetPaletteItem(DependencyObject? source) =>
        ItemsControl.ContainerFromElement(PaletteList, source) is ListBoxItem item ? item.DataContext as PaletteItem : null;

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDroppedReference(e.Data, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (TryGetDroppedReference(e.Data, out var path) && path is not null) await OpenReferenceWithConfirmationAsync(path);
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (e.Key == Key.Escape && _viewModel.Colors.IsPicking)
        {
            e.Handled = true;
            _viewModel.CancelColorPick();
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (e.OriginalSource is DependencyObject sliderSource && FindAncestor<Slider>(sliderSource) is { } slider &&
            modifiers is ModifierKeys.None or ModifierKeys.Shift && key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            _viewModel.BeginBurst("slider-" + slider.GetHashCode());
            return;
        }
        var typing = e.OriginalSource is DependencyObject source && FindAncestor<TextBox>(source) is not null;
        if (typing && (modifiers is ModifierKeys.None or ModifierKeys.Shift || modifiers == ModifierKeys.Control && key is Key.V or Key.C or Key.X or Key.A or Key.Z or Key.Y)) return;
        var shortcut = _viewModel.Settings.Shortcuts.FirstOrDefault(b => b.Key == key && b.Modifiers == modifiers);
        if (shortcut is not null)
        {
            e.Handled = true;
            await ExecuteShortcutAsync(shortcut.Action);
            return;
        }
        if (e.Key != Key.V || modifiers != ModifierKeys.Control) return;

        e.Handled = true;
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var path = Clipboard.GetFileDropList().Cast<string>().FirstOrDefault(ReferenceImageService.IsSupportedPath);
                if (path is not null)
                {
                    await OpenReferenceWithConfirmationAsync(path);
                    return;
                }
            }
            if (Clipboard.ContainsImage())
            {
                var image = Clipboard.GetImage();
                if (image is not null && ConfirmReferenceReplacement(L.Get("Ui.ClipboardImage"))) await _viewModel.OpenClipboardBitmapAsync(image);
                return;
            }
            _viewModel.NotifyUnsupportedPaste();
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            _viewModel.NotifyUnsupportedPaste();
        }
    }

    private async Task OpenProjectFromDialogAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = L.Get("Dialog.OpenProject"),
            Filter = ProjectArchiveService.FileDialogFilter,
            DefaultExt = ".TDFE",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true && await ConfirmUnsavedChangesAsync())
        {
            if (await _viewModel.OpenProjectAsync(dialog.FileName)) await CommitDiscardAsync();
            else _discardPendingId = null;
        }
    }

    private async Task<bool> SaveProjectWithDialogAsync(bool forceSaveAs)
    {
        CommitFocusedText();
        _viewModel.EndGesture();
        var path = _viewModel.Project.Path;
        if (forceSaveAs || string.IsNullOrWhiteSpace(path))
        {
            var dialog = new SaveFileDialog
            {
                Title = L.Get("Dialog.SaveProject"),
                Filter = ProjectArchiveService.FileDialogFilter,
                DefaultExt = ".TDFE",
                AddExtension = true,
                FileName = string.IsNullOrWhiteSpace(path) ? "Untitled.TDFE" : System.IO.Path.GetFileName(path)
            };
            if (dialog.ShowDialog(this) != true) return false;
            path = dialog.FileName;
        }
        return await _viewModel.SaveProjectAsync(path);
    }

    private async Task<bool> ConfirmUnsavedChangesAsync()
    {
        CommitFocusedText();
        _viewModel.EndGesture();
        if (_viewModel.IsBusy) { _viewModel.Notify(L.Get("Notice.WaitOperation")); return false; }
        _discardPendingId = null;
        if (!_viewModel.Project.IsDirty) return true;
        var dialog = new UnsavedChangesDialog { Owner = this };
        _ = dialog.ShowDialog();
        if (dialog.Choice == UnsavedChangesChoice.Save) return await SaveProjectWithDialogAsync(forceSaveAs: false);
        if (dialog.Choice != UnsavedChangesChoice.DontSave) return false;
        _discardPendingId = _viewModel.Project.ProjectId;
        return true;
    }

    private async Task OpenReferenceWithConfirmationAsync(string path)
    {
        if (ConfirmReferenceReplacement(System.IO.Path.GetFileName(path))) await _viewModel.OpenReferenceAsync(path);
    }

    private bool ConfirmReferenceReplacement(string incomingName)
    {
        if (!_viewModel.Reference.HasImage || !_viewModel.InputSettings.ConfirmReplacement) return true;
        var dialog = new ReferenceReplacementDialog(_viewModel.Reference.Source?.Name ?? L.Get("Ui.CurrentReference"), incomingName) { Owner = this };
        return dialog.ShowDialog() == true;
    }

    private static bool TryGetDroppedReference(IDataObject data, out string? path)
    {
        path = null;
        if (!data.GetDataPresent(DataFormats.FileDrop) || data.GetData(DataFormats.FileDrop) is not string[] files) return false;
        path = files.FirstOrDefault(ReferenceImageService.IsSupportedPath);
        return path is not null;
    }

    private void OnFirstTargetConnected(object? sender, IntRect bounds)
    {
        if (_positionedForFirstTarget) return;
        _positionedForFirstTarget = true;
        var desiredLeft = bounds.Left - ActualWidth - 12;
        if (desiredLeft >= SystemParameters.VirtualScreenLeft)
        {
            Left = desiredLeft;
            Top = Math.Max(SystemParameters.VirtualScreenTop, bounds.Top);
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeApproved) return;
        e.Cancel = true;
        if (_closePromptActive) return;
        _closePromptActive = true;
        try
        {
            if (await ConfirmUnsavedChangesAsync())
            {
                await CommitDiscardAsync();
                await _viewModel.FlushSettingsAsync();
                _closeApproved = true;
                Close();
            }
        }
        finally { _closePromptActive = false; }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        DisposeReliabilityUi();
        _viewModel.FirstTargetConnected -= OnFirstTargetConnected;
        Closing -= OnClosing;
        Closed -= OnClosed;
        _viewModel.Dispose();
    }
}
