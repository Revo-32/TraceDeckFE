using TraceDeckFE.Localization;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using TraceDeckFE.Interop;
using TraceDeckFE.Models;
using TraceDeckFE.Services;

namespace TraceDeckFE.Overlay;

public partial class OverlayWindow : Window, IDisposable
{
    public double ZoomFactor { get; set; } = 1.10;
    public bool ZoomTowardCursor { get; set; } = true;
    private const int PickerEscapeHotKeyId = 0x5444;
    private readonly ReferenceState _reference;
    private readonly GuideState _guides;
    private readonly ColorState _colors;
    private readonly ReferenceColorService _colorService;
    private readonly ITraceLogger _logger;
    private nint _windowHandle;
    private nint _targetHandle;
    private TargetWindowSnapshot _targetState = TargetWindowSnapshot.Disconnected;
    private System.Windows.Point _previousPointer;
    private bool _isDragging;
    private bool _isInitialized;
    private bool _isDisposed;
    private HwndSource? _windowSource;
    private CancellationTokenSource? _pickerCancellation;
    private CancellationTokenSource? _magnifierCancellation;
    private long _pickerOperation;
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardHookCallback;
    private nint _keyboardHook;

    public OverlayWindow(
        ReferenceState reference,
        GuideState guides,
        ColorState colors,
        ReferenceColorService colorService,
        ITraceLogger logger)
    {
        _reference = reference;
        _guides = guides;
        _colors = colors;
        _colorService = colorService;
        _logger = logger;
        _keyboardHookCallback = KeyboardHookProcedure;
        InitializeComponent();

        SourceInitialized += OnSourceInitialized;
        PreviewMouseLeftButtonDown += OnMouseLeftButtonDown;
        PreviewMouseMove += OnMouseMove;
        PreviewMouseLeftButtonUp += OnMouseLeftButtonUp;
        PreviewMouseWheel += OnMouseWheel;
        LostMouseCapture += (_, _) => EndDrag();
        _reference.PropertyChanged += OnReferencePropertyChanged;
        _guides.PropertyChanged += OnGuidesPropertyChanged;
    }

    public event EventHandler<ColorPickedEventArgs>? ColorPicked;
    public event EventHandler? ColorPickCanceled;
    public event EventHandler<string>? ColorPickFailed;
    public event EventHandler? UserTransformChanged;
    public event EventHandler? GestureStarted;
    public event EventHandler? GestureCompleted;
    public event EventHandler? ZoomGesture;

    public double ViewportWidth => _targetState.ClientBounds.Width;
    public double ViewportHeight => _targetState.ClientBounds.Height;

    public bool BeginColorPick()
    {
        if (_isDisposed || !_reference.HasImage || !_reference.IsVisible || !_targetState.Exists || _targetState.IsMinimized)
        {
            return false;
        }

        if (_colors.IsPicking)
        {
            return true;
        }

        _pickerCancellation?.Cancel();
        _pickerCancellation?.Dispose();
        _pickerCancellation = new CancellationTokenSource();
        _pickerOperation++;
        _colors.IsPicking = true;
        OverlayCanvas.Cursor = Cursors.Cross;
        _keyboardHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLl,
            _keyboardHookCallback,
            NativeMethods.GetModuleHandle(null),
            threadId: 0);
        var hotKeyRegistered = _windowHandle != 0 && NativeMethods.RegisterHotKey(
            _windowHandle,
            PickerEscapeHotKeyId,
            modifiers: 0,
            NativeMethods.VkEscape);
        if (_keyboardHook == 0 && !hotKeyRegistered)
        {
            _logger.Warning("Picker Esc registration failed; the controller Cancel button remains available.");
        }
        UpdateExtendedStyles();
        UpdateVisibility();
        return true;
    }

    public void CancelColorPick()
    {
        if (!_colors.IsPicking)
        {
            return;
        }

        EndColorPick();
        ColorPickCanceled?.Invoke(this, EventArgs.Empty);
    }

    public void InitializeHidden()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        Show();
        HideNative();
    }

    public void AttachToTarget(nint targetHandle)
    {
        _targetHandle = targetHandle;
        if (_windowHandle != 0)
        {
            _ = NativeMethods.SetWindowLongPtr(
                _windowHandle,
                NativeMethods.GwlHwndParent,
                targetHandle);
        }
    }

    public void DetachTarget()
    {
        _targetHandle = 0;
        _targetState = TargetWindowSnapshot.Disconnected;
        HideNative();
        if (_windowHandle != 0)
        {
            _ = NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GwlHwndParent, 0);
        }
    }

    public void ApplyTargetState(TargetWindowSnapshot state)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => ApplyTargetState(state));
            return;
        }

        _targetState = state;
        if (!state.Exists)
        {
            DetachTarget();
            return;
        }

        if (_targetHandle != state.Handle)
        {
            AttachToTarget(state.Handle);
        }

        if (_windowHandle == 0)
        {
            return;
        }

        var bounds = state.ClientBounds;
        if (!bounds.IsEmpty)
        {
            _ = NativeMethods.SetWindowPos(
                _windowHandle,
                0,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                NativeMethods.SwpNoActivate |
                NativeMethods.SwpNoZOrder |
                NativeMethods.SwpNoOwnerZOrder);
        }

        UpdateGuides();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(_windowHandle);
        _windowSource?.AddHook(WindowProcedure);
        UpdateExtendedStyles();
        if (_targetHandle != 0)
        {
            _ = NativeMethods.SetWindowLongPtr(
                _windowHandle,
                NativeMethods.GwlHwndParent,
                _targetHandle);
        }
        UpdateVisual();
        UpdateGuides();
    }

    private nint WindowProcedure(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == NativeMethods.WmHotkey && wParam.ToInt32() == PickerEscapeHotKeyId)
        {
            handled = true;
            CancelColorPick();
        }

        return 0;
    }

    private nint KeyboardHookProcedure(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && _colors.IsPicking &&
            wParam.ToInt32() is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown)
        {
            var data = Marshal.PtrToStructure<NativeMethods.KeyboardHookData>(lParam);
            if (data.VirtualKey == NativeMethods.VkEscape)
            {
                _ = Dispatcher.BeginInvoke(CancelColorPick);
                return 1;
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private void OnReferencePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(UpdateVisual);
            return;
        }

        if (e.PropertyName == nameof(ReferenceState.IsLocked))
        {
            UpdateExtendedStyles();
        }

        UpdateVisual();
    }

    private void OnGuidesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(UpdateGuides);
            return;
        }

        UpdateGuides();
    }

    private void UpdateVisual()
    {
        if (_isDisposed)
        {
            return;
        }

        ReferenceImage.Source = _reference.Image;
        if (_reference.HasImage)
        {
            ReferenceImage.Width = _reference.ImageWidth;
            ReferenceImage.Height = _reference.ImageHeight;
        }

        ReferenceImage.Opacity = _reference.Opacity;
        // WPF applies a center-origin RenderTransform around the unscaled element.
        // Offset layout so that this origin matches the model's scaled visual center.
        Canvas.SetLeft(
            ReferenceImage,
            _reference.X + _reference.ImageWidth * _reference.Scale / 2.0 - _reference.ImageWidth / 2.0);
        Canvas.SetTop(
            ReferenceImage,
            _reference.Y + _reference.ImageHeight * _reference.Scale / 2.0 - _reference.ImageHeight / 2.0);
        var transforms = new TransformGroup();
        transforms.Children.Add(new ScaleTransform(
            _reference.Scale * (_reference.FlipHorizontal ? -1 : 1),
            _reference.Scale * (_reference.FlipVertical ? -1 : 1)));
        transforms.Children.Add(new RotateTransform(_reference.Rotation));
        ReferenceImage.RenderTransform = transforms;
        ReferenceImage.Visibility = _reference.HasImage && _reference.IsVisible
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!_reference.HasImage || !_reference.IsVisible)
        {
            CancelColorPick();
        }

        UpdateVisibility();
    }

    private void UpdateGuides()
    {
        if (_isDisposed)
        {
            return;
        }

        GuideCanvas.Children.Clear();
        var width = ViewportWidth;
        var height = ViewportHeight;
        if (width <= 0 || height <= 0)
        {
            UpdateVisibility();
            return;
        }

        var gridBrush = new SolidColorBrush(Color.FromArgb(255, 205, 205, 205));
        gridBrush.Freeze();
        if (_guides.IsGridVisible)
        {
            for (var x = _guides.GridSpacing; x < width; x += _guides.GridSpacing)
            {
                GuideCanvas.Children.Add(CreateGuideLine(x, 0, x, height, gridBrush, 0.75));
            }

            for (var y = _guides.GridSpacing; y < height; y += _guides.GridSpacing)
            {
                GuideCanvas.Children.Add(CreateGuideLine(0, y, width, y, gridBrush, 0.75));
            }
        }

        if (_guides.IsHorizontalCenterVisible)
        {
            GuideCanvas.Children.Add(CreateGuideLine(0, height / 2.0, width, height / 2.0, gridBrush, 1.25));
        }

        if (_guides.IsVerticalCenterVisible)
        {
            GuideCanvas.Children.Add(CreateGuideLine(width / 2.0, 0, width / 2.0, height, gridBrush, 1.25));
        }

        GuideCanvas.Opacity = _guides.Opacity;
        UpdateVisibility();
    }

    private static Line CreateGuideLine(
        double x1,
        double y1,
        double x2,
        double y2,
        Brush brush,
        double thickness) => new()
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = brush,
            StrokeThickness = thickness,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false
        };

    private void UpdateVisibility()
    {
        if (_windowHandle == 0)
        {
            return;
        }

        var hasVisibleContent = (_reference.IsVisible && _reference.HasImage) || _guides.HasVisibleGuide;
        if (OverlayVisibilityPolicy.ShouldShow(_targetState, referenceVisible: true, hasImage: hasVisibleContent))
        {
            _ = NativeMethods.ShowWindow(_windowHandle, NativeMethods.SwShowNoActivate);
            UpdateZOrder();
        }
        else
        {
            HideNative();
        }
    }

    private void UpdateZOrder()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        var stackingMode = OverlayStackingPolicy.Decide(_targetHandle, foreground);
        var insertAfter = stackingMode == OverlayStackingMode.AboveTarget
            ? nint.Zero
            : foreground;

        // HWND_TOP is non-topmost. With another app active, placing the owned
        // overlay immediately behind that foreground window keeps it above its
        // target without covering the active application.
        _ = NativeMethods.SetWindowPos(
            _windowHandle,
            insertAfter,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoActivate |
            NativeMethods.SwpNoMove |
            NativeMethods.SwpNoSize |
            NativeMethods.SwpNoOwnerZOrder);
    }

    private void UpdateExtendedStyles()
    {
        if (_windowHandle == 0)
        {
            return;
        }

        var style = NativeMethods.GetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle).ToInt64();
        style |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        if (_reference.IsLocked && !_colors.IsPicking)
        {
            style |= NativeMethods.WsExTransparent;
            EndDrag();
        }
        else
        {
            style &= ~NativeMethods.WsExTransparent;
        }

        _ = NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle, (nint)style);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_colors.IsPicking)
        {
            e.Handled = true;
            _ = PickColorAsync(e.GetPosition(OverlayCanvas));
            return;
        }

        if (_reference.IsLocked || !_reference.HasImage)
        {
            return;
        }

        var point = e.GetPosition(OverlayCanvas);
        if (!IsReferencePoint(point))
        {
            return;
        }

        _previousPointer = point;
        _isDragging = Mouse.Capture(OverlayCanvas);
        if (_isDragging) GestureStarted?.Invoke(this, EventArgs.Empty);
        e.Handled = _isDragging;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_colors.IsPicking)
        {
            _ = UpdateMagnifierAsync(e.GetPosition(OverlayCanvas));
            e.Handled = true;
            return;
        }

        if (!_isDragging || _reference.IsLocked || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var point = e.GetPosition(OverlayCanvas);
        _reference.MoveBy(point.X - _previousPointer.X, point.Y - _previousPointer.Y);
        UserTransformChanged?.Invoke(this, EventArgs.Empty);
        _previousPointer = point;
        e.Handled = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            EndDrag();
            e.Handled = true;
        }
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_colors.IsPicking)
        {
            e.Handled = true;
            return;
        }

        if (_reference.IsLocked || !_reference.HasImage)
        {
            return;
        }

        var point = e.GetPosition(OverlayCanvas);
        if (!IsReferencePoint(point))
        {
            return;
        }

        ZoomGesture?.Invoke(this, EventArgs.Empty);
        var factor = e.Delta > 0 ? ZoomFactor : 1.0 / ZoomFactor;
        if (!ZoomTowardCursor) point = new System.Windows.Point(_reference.X + _reference.ImageWidth * _reference.Scale / 2,
            _reference.Y + _reference.ImageHeight * _reference.Scale / 2);
        _reference.ZoomAt(new PointD(point.X, point.Y), factor);
        UserTransformChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private async Task PickColorAsync(System.Windows.Point point)
    {
        var operation = _pickerOperation;
        var source = _reference.Source;
        var cancellationToken = _pickerCancellation?.Token ?? CancellationToken.None;
        if (source is null || !IsReferencePoint(point))
        {
            return;
        }

        try
        {
            var color = await _colorService.SampleDisplayAsync(
                source,
                _reference.VisualTransform,
                new PointD(point.X, point.Y),
                cancellationToken);
            if (color is null || operation != _pickerOperation || !_colors.IsPicking)
            {
                return;
            }

            var imagePoint = ReferenceTransformMath.DisplayToImage(
                _reference.VisualTransform,
                new PointD(point.X, point.Y),
                source.PixelWidth,
                source.PixelHeight);
            EndColorPick();
            ColorPicked?.Invoke(this, new ColorPickedEventArgs(color.Value, imagePoint));
        }
        catch (OperationCanceledException)
        {
            // Picker cancellation or a newer operation superseded this sample.
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or ImageMagick.MagickException)
        {
            _logger.Error("Original Reference color sampling failed.", exception);
            ColorPickFailed?.Invoke(this, L.Get("Notice.PickFailed"));
        }
    }

    private async Task UpdateMagnifierAsync(System.Windows.Point point)
    {
        if (!_colors.IsPicking || !_colors.MagnifierEnabled || _reference.Source is not { } source ||
            !IsReferencePoint(point))
        {
            MagnifierBorder.Visibility = Visibility.Collapsed;
            return;
        }

        _magnifierCancellation?.Cancel();
        _magnifierCancellation?.Dispose();
        _magnifierCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _pickerCancellation?.Token ?? CancellationToken.None);
        var token = _magnifierCancellation.Token;
        var operation = _pickerOperation;
        try
        {
            var bitmap = await _colorService.CreateMagnifierAsync(
                source,
                _reference.VisualTransform,
                new PointD(point.X, point.Y),
                cancellationToken: token);
            if (bitmap is null || token.IsCancellationRequested || operation != _pickerOperation || !_colors.IsPicking)
            {
                return;
            }

            MagnifierImage.Source = bitmap;
            PositionMagnifier(point);
            MagnifierBorder.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            // Pointer moved; only the latest event-driven magnifier request is shown.
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or ImageMagick.MagickException)
        {
            MagnifierBorder.Visibility = Visibility.Collapsed;
            _logger.Error("Picker magnifier update failed.", exception);
        }
    }

    private void PositionMagnifier(System.Windows.Point point)
    {
        const double gap = 18;
        var left = point.X + gap;
        var top = point.Y + gap;
        if (left + MagnifierBorder.Width > ViewportWidth)
        {
            left = point.X - MagnifierBorder.Width - gap;
        }
        if (top + MagnifierBorder.Height > ViewportHeight)
        {
            top = point.Y - MagnifierBorder.Height - gap;
        }

        Canvas.SetLeft(MagnifierBorder, Math.Clamp(left, 0, Math.Max(0, ViewportWidth - MagnifierBorder.Width)));
        Canvas.SetTop(MagnifierBorder, Math.Clamp(top, 0, Math.Max(0, ViewportHeight - MagnifierBorder.Height)));
    }

    private void EndColorPick()
    {
        _pickerOperation++;
        _pickerCancellation?.Cancel();
        _magnifierCancellation?.Cancel();
        _colors.IsPicking = false;
        OverlayCanvas.Cursor = Cursors.Arrow;
        MagnifierBorder.Visibility = Visibility.Collapsed;
        MagnifierImage.Source = null;
        if (_windowHandle != 0)
        {
            _ = NativeMethods.UnregisterHotKey(_windowHandle, PickerEscapeHotKeyId);
        }
        if (_keyboardHook != 0)
        {
            _ = NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = 0;
        }
        UpdateExtendedStyles();
    }

    private bool IsReferencePoint(System.Windows.Point point)
    {
        if (!_reference.HasImage)
        {
            return false;
        }

        return ReferenceTransformMath.ContainsDisplayPoint(
            _reference.VisualTransform,
            new PointD(point.X, point.Y),
            _reference.ImageWidth,
            _reference.ImageHeight);
    }

    private void EndDrag()
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        Mouse.Capture(null);
        GestureCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void HideNative()
    {
        if (_windowHandle != 0)
        {
            _ = NativeMethods.ShowWindow(_windowHandle, NativeMethods.SwHide);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        EndColorPick();
        _reference.PropertyChanged -= OnReferencePropertyChanged;
        _guides.PropertyChanged -= OnGuidesPropertyChanged;
        SourceInitialized -= OnSourceInitialized;
        PreviewMouseLeftButtonDown -= OnMouseLeftButtonDown;
        PreviewMouseMove -= OnMouseMove;
        PreviewMouseLeftButtonUp -= OnMouseLeftButtonUp;
        PreviewMouseWheel -= OnMouseWheel;
        _windowSource?.RemoveHook(WindowProcedure);
        _pickerCancellation?.Dispose();
        _magnifierCancellation?.Dispose();

        if (Dispatcher.CheckAccess())
        {
            Close();
        }
        else
        {
            _ = Dispatcher.BeginInvoke(Close);
        }
    }
}

public sealed class ColorPickedEventArgs : EventArgs
{
    public ColorPickedEventArgs(RgbaColor color, PointD imagePoint)
    {
        Color = color;
        ImagePoint = imagePoint;
    }

    public RgbaColor Color { get; }
    public PointD ImagePoint { get; }
}
