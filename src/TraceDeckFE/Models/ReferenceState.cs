using TraceDeckFE.Localization;
using System.Windows.Media.Imaging;

namespace TraceDeckFE.Models;

public sealed class ReferenceState : ObservableObject
{
    private ReferenceImageSource? _source;
    private BitmapSource? _image;
    private double _x;
    private double _y;
    private double _scale = 1.0;
    private double _rotation;
    private bool _flipHorizontal;
    private bool _flipVertical;
    private bool _isGrayscale;
    private double _contrast;
    private double _opacity = 0.62;
    private bool _isVisible = true;
    private bool _isLocked = true;
    private double _viewportWidth;
    private double _viewportHeight;
    private NormalizedReferenceTransform? _normalizedTransform;

    public ReferenceImageSource? Source
    {
        get => _source;
        private set
        {
            if (SetProperty(ref _source, value))
            {
                OnPropertyChanged(nameof(SourcePath));
                OnPropertyChanged(nameof(HasImage));
                OnPropertyChanged(nameof(ImageDescription));
                OnPropertyChanged(nameof(ImageWidth));
                OnPropertyChanged(nameof(ImageHeight));
            }
        }
    }

    public BitmapSource? Image
    {
        get => _image;
        private set
        {
            if (SetProperty(ref _image, value))
            {
                OnPropertyChanged(nameof(HasImage));
            }
        }
    }

    public string? SourcePath => Source?.SourcePath;
    public double ImageWidth => Source?.PixelWidth ?? 0;
    public double ImageHeight => Source?.PixelHeight ?? 0;
    public bool HasImage => Source is not null && Image is not null;
    public string ImageDescription => Source is null
        ? L.Get("Status.NoImage")
        : $"{Source.Name} · {Source.PixelWidth} × {Source.PixelHeight}";

    public double X
    {
        get => _x;
        set
        {
            if (double.IsFinite(value) && SetProperty(ref _x, value))
            {
                SynchronizeNormalizedTransform();
            }
        }
    }

    public double Y
    {
        get => _y;
        set
        {
            if (double.IsFinite(value) && SetProperty(ref _y, value))
            {
                SynchronizeNormalizedTransform();
            }
        }
    }

    public double Scale
    {
        get => _scale;
        set
        {
            if (SetProperty(ref _scale, ReferenceTransformMath.ClampScale(value)))
            {
                SynchronizeNormalizedTransform();
            }
        }
    }

    public double Rotation
    {
        get => _rotation;
        set => SetProperty(ref _rotation, ReferenceTransformMath.NormalizeRotation(value));
    }

    public bool FlipHorizontal
    {
        get => _flipHorizontal;
        set => SetProperty(ref _flipHorizontal, value);
    }

    public bool FlipVertical
    {
        get => _flipVertical;
        set => SetProperty(ref _flipVertical, value);
    }

    public bool IsGrayscale
    {
        get => _isGrayscale;
        set => SetProperty(ref _isGrayscale, value);
    }

    public double Contrast
    {
        get => _contrast;
        set => SetProperty(ref _contrast, double.IsFinite(value) ? Math.Clamp(Math.Round(value), -100, 100) : 0);
    }

    public double ViewportWidth => _viewportWidth;
    public double ViewportHeight => _viewportHeight;
    public NormalizedReferenceTransform? NormalizedTransform => _normalizedTransform;
    public ReferenceVisualTransform VisualTransform => new(X, Y, Scale, Rotation, FlipHorizontal, FlipVertical);

    public double Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public bool IsLocked
    {
        get => _isLocked;
        set => SetProperty(ref _isLocked, value);
    }

    public void SetImage(ReferenceImageSource source, double viewportWidth, double viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!HasValidViewport(viewportWidth, viewportHeight))
        {
            throw new ArgumentOutOfRangeException(nameof(viewportWidth), "Viewport dimensions must be finite and positive.");
        }

        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;
        Source = source;
        Image = source.OriginalBitmap;
        Rotation = 0;
        FlipHorizontal = false;
        FlipVertical = false;
        IsGrayscale = false;
        Contrast = 0;
        var fitScale = ReferenceTransformMath.FitScale(viewportWidth, viewportHeight, ImageWidth, ImageHeight);
        ApplyTransform(
            ReferenceTransformMath.Center(viewportWidth, viewportHeight, ImageWidth, ImageHeight, fitScale),
            updateNormalized: true);
        IsVisible = true;
    }

    public void RestoreProject(
        ReferenceImageSource source,
        ReferenceProjectState state,
        OverlayProjectState overlay,
        double viewportWidth,
        double viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(overlay);
        if (!HasValidViewport(viewportWidth, viewportHeight))
        {
            viewportWidth = state.SavedViewportWidth > 0 ? state.SavedViewportWidth : 1280;
            viewportHeight = state.SavedViewportHeight > 0 ? state.SavedViewportHeight : 720;
        }

        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;
        Source = source;
        Image = source.OriginalBitmap;
        _normalizedTransform = new NormalizedReferenceTransform(
            state.NormalizedCenterX,
            state.NormalizedCenterY,
            state.NormalizedVisualWidth);
        ApplyTransform(
            ReferenceTransformMath.Denormalize(
                _normalizedTransform.Value,
                viewportWidth,
                viewportHeight,
                source.PixelWidth,
                source.PixelHeight),
            updateNormalized: false);
        Rotation = state.Rotation;
        FlipHorizontal = state.FlipHorizontal;
        FlipVertical = state.FlipVertical;
        IsGrayscale = state.Grayscale;
        Contrast = state.Contrast;
        Opacity = overlay.Opacity;
        IsVisible = overlay.Visible;
        IsLocked = overlay.Locked;
        OnPropertyChanged(nameof(NormalizedTransform));
    }

    public void Clear()
    {
        Source = null;
        Image = null;
        _x = 0;
        _y = 0;
        _scale = 1;
        _rotation = 0;
        _flipHorizontal = false;
        _flipVertical = false;
        _isGrayscale = false;
        _contrast = 0;
        _opacity = 0.62;
        _isVisible = true;
        _isLocked = true;
        _normalizedTransform = null;
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(Scale));
        OnPropertyChanged(nameof(Rotation));
        OnPropertyChanged(nameof(FlipHorizontal));
        OnPropertyChanged(nameof(FlipVertical));
        OnPropertyChanged(nameof(IsGrayscale));
        OnPropertyChanged(nameof(Contrast));
        OnPropertyChanged(nameof(Opacity));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(NormalizedTransform));
    }

    public void SetDisplayImage(Guid sourceId, BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (Source?.Id == sourceId)
        {
            Image = image;
        }
    }

    public void UpdateViewport(double viewportWidth, double viewportHeight)
    {
        if (!HasValidViewport(viewportWidth, viewportHeight) ||
            (viewportWidth == _viewportWidth && viewportHeight == _viewportHeight))
        {
            return;
        }

        if (HasImage && _normalizedTransform is null && HasCurrentViewport)
        {
            SynchronizeNormalizedTransform();
        }

        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;

        if (!HasImage || _normalizedTransform is not { } normalized)
        {
            return;
        }

        ApplyTransform(
            ReferenceTransformMath.Denormalize(normalized, viewportWidth, viewportHeight, ImageWidth, ImageHeight),
            updateNormalized: false);
    }

    public void MoveBy(double deltaX, double deltaY)
    {
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY))
        {
            return;
        }

        ApplyTransform(new TransformSnapshot(X + deltaX, Y + deltaY, Scale), updateNormalized: true);
    }

    public void ZoomAt(PointD anchor, double factor)
    {
        if (!HasImage)
        {
            return;
        }

        var result = ReferenceTransformMath.ZoomAt(VisualTransform, anchor, factor, ImageWidth, ImageHeight);
        ApplyTransform(new TransformSnapshot(result.X, result.Y, result.Scale), updateNormalized: true);
    }

    public void RotateBy(double degrees) => Rotation += degrees;

    public void Center(double viewportWidth, double viewportHeight)
    {
        if (!HasImage || !AdoptViewport(viewportWidth, viewportHeight))
        {
            return;
        }

        ApplyTransform(
            ReferenceTransformMath.Center(viewportWidth, viewportHeight, ImageWidth, ImageHeight, Scale),
            updateNormalized: true);
    }

    public void Fit(double viewportWidth, double viewportHeight)
    {
        if (!HasImage || !AdoptViewport(viewportWidth, viewportHeight))
        {
            return;
        }

        var fitScale = ReferenceTransformMath.FitScale(
            viewportWidth, viewportHeight, ImageWidth, ImageHeight, rotationDegrees: Rotation);
        ApplyTransform(
            ReferenceTransformMath.Center(viewportWidth, viewportHeight, ImageWidth, ImageHeight, fitScale),
            updateNormalized: true);
    }

    public void ResetTransform()
    {
        if (!HasImage)
        {
            return;
        }

        var centerX = X + ImageWidth * Scale / 2.0;
        var centerY = Y + ImageHeight * Scale / 2.0;
        Rotation = 0;
        FlipHorizontal = false;
        FlipVertical = false;
        ApplyTransform(
            new TransformSnapshot(centerX - ImageWidth / 2.0, centerY - ImageHeight / 2.0, 1),
            updateNormalized: true);
    }

    public void ResetEffects()
    {
        IsGrayscale = false;
        Contrast = 0;
    }

    private bool HasCurrentViewport => HasValidViewport(_viewportWidth, _viewportHeight);

    private void ApplyTransform(TransformSnapshot transform, bool updateNormalized)
    {
        if (!double.IsFinite(transform.X) || !double.IsFinite(transform.Y))
        {
            return;
        }

        _ = SetProperty(ref _x, transform.X, nameof(X));
        _ = SetProperty(ref _y, transform.Y, nameof(Y));
        _ = SetProperty(ref _scale, ReferenceTransformMath.ClampScale(transform.Scale), nameof(Scale));

        if (updateNormalized)
        {
            SynchronizeNormalizedTransform();
        }
    }

    private void SynchronizeNormalizedTransform()
    {
        if (!HasImage || !HasCurrentViewport)
        {
            return;
        }

        _normalizedTransform = ReferenceTransformMath.Normalize(
            new TransformSnapshot(X, Y, Scale),
            _viewportWidth,
            _viewportHeight,
            ImageWidth,
            ImageHeight);
    }

    private bool AdoptViewport(double viewportWidth, double viewportHeight)
    {
        if (!HasValidViewport(viewportWidth, viewportHeight))
        {
            return false;
        }

        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;
        return true;
    }

    private static bool HasValidViewport(double width, double height) =>
        double.IsFinite(width) && width > 0 && double.IsFinite(height) && height > 0;
}
