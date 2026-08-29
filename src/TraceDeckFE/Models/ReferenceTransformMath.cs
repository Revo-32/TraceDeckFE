namespace TraceDeckFE.Models;

public readonly record struct TransformSnapshot(double X, double Y, double Scale);

public readonly record struct ReferenceVisualTransform(
    double X,
    double Y,
    double Scale,
    double RotationDegrees,
    bool FlipHorizontal,
    bool FlipVertical);

public readonly record struct NormalizedReferenceTransform(
    double CenterXRatio,
    double CenterYRatio,
    double VisualWidthRatio);

public static class ReferenceTransformMath
{
    public const double MinimumScale = 0.05;
    public const double MaximumScale = 20.0;

    public static TransformSnapshot ZoomAt(TransformSnapshot current, PointD anchor, double factor) =>
        ZoomAt(
            new ReferenceVisualTransform(current.X, current.Y, current.Scale, 0, false, false),
            anchor,
            factor,
            imageWidth: 1,
            imageHeight: 1).ToSnapshot();

    public static ReferenceVisualTransform ZoomAt(
        ReferenceVisualTransform current,
        PointD anchor,
        double factor,
        double imageWidth,
        double imageHeight)
    {
        if (!double.IsFinite(factor) || factor <= 0)
        {
            return current;
        }

        var oldScale = ClampScale(current.Scale);
        var newScale = ClampScale(oldScale * factor);
        if (Math.Abs(newScale - oldScale) < 0.000001)
        {
            return current with { Scale = newScale };
        }

        var imagePoint = DisplayToImage(current with { Scale = oldScale }, anchor, imageWidth, imageHeight);
        var transformedOffset = TransformImageOffset(
            imagePoint.X - imageWidth / 2.0,
            imagePoint.Y - imageHeight / 2.0,
            newScale,
            current.RotationDegrees,
            current.FlipHorizontal,
            current.FlipVertical);
        var centerX = anchor.X - transformedOffset.X;
        var centerY = anchor.Y - transformedOffset.Y;
        return current with
        {
            X = centerX - imageWidth * newScale / 2.0,
            Y = centerY - imageHeight * newScale / 2.0,
            Scale = newScale
        };
    }

    public static PointD ImageToDisplay(
        ReferenceVisualTransform transform,
        PointD imagePoint,
        double imageWidth,
        double imageHeight)
    {
        ValidateImageDimensions(imageWidth, imageHeight);
        var scale = ClampScale(transform.Scale);
        var centerX = transform.X + imageWidth * scale / 2.0;
        var centerY = transform.Y + imageHeight * scale / 2.0;
        var offset = TransformImageOffset(
            imagePoint.X - imageWidth / 2.0,
            imagePoint.Y - imageHeight / 2.0,
            scale,
            transform.RotationDegrees,
            transform.FlipHorizontal,
            transform.FlipVertical);
        return new PointD(centerX + offset.X, centerY + offset.Y);
    }

    public static PointD DisplayToImage(
        ReferenceVisualTransform transform,
        PointD displayPoint,
        double imageWidth,
        double imageHeight)
    {
        ValidateImageDimensions(imageWidth, imageHeight);
        var scale = ClampScale(transform.Scale);
        var centerX = transform.X + imageWidth * scale / 2.0;
        var centerY = transform.Y + imageHeight * scale / 2.0;
        var radians = -NormalizeRotation(transform.RotationDegrees) * Math.PI / 180.0;
        var dx = displayPoint.X - centerX;
        var dy = displayPoint.Y - centerY;
        var rotatedX = dx * Math.Cos(radians) - dy * Math.Sin(radians);
        var rotatedY = dx * Math.Sin(radians) + dy * Math.Cos(radians);
        var flipX = transform.FlipHorizontal ? -1.0 : 1.0;
        var flipY = transform.FlipVertical ? -1.0 : 1.0;
        return new PointD(
            imageWidth / 2.0 + rotatedX / (scale * flipX),
            imageHeight / 2.0 + rotatedY / (scale * flipY));
    }

    public static bool ContainsDisplayPoint(
        ReferenceVisualTransform transform,
        PointD displayPoint,
        double imageWidth,
        double imageHeight)
    {
        var imagePoint = DisplayToImage(transform, displayPoint, imageWidth, imageHeight);
        const double tolerance = 0.000001;
        return imagePoint.X >= -tolerance && imagePoint.X <= imageWidth + tolerance &&
               imagePoint.Y >= -tolerance && imagePoint.Y <= imageHeight + tolerance;
    }

    public static bool TryMapDisplayToImagePixel(
        ReferenceVisualTransform transform,
        PointD displayPoint,
        int imageWidth,
        int imageHeight,
        out IntPoint pixel,
        out PointD imagePoint)
    {
        imagePoint = DisplayToImage(transform, displayPoint, imageWidth, imageHeight);
        if (!double.IsFinite(imagePoint.X) || !double.IsFinite(imagePoint.Y) ||
            imagePoint.X < 0 || imagePoint.Y < 0 ||
            imagePoint.X >= imageWidth || imagePoint.Y >= imageHeight)
        {
            pixel = default;
            return false;
        }

        pixel = new IntPoint(
            Math.Clamp((int)Math.Floor(imagePoint.X), 0, imageWidth - 1),
            Math.Clamp((int)Math.Floor(imagePoint.Y), 0, imageHeight - 1));
        return true;
    }

    public static TransformSnapshot Center(
        double viewportWidth,
        double viewportHeight,
        double imageWidth,
        double imageHeight,
        double scale)
    {
        var safeScale = ClampScale(scale);
        return new TransformSnapshot(
            (viewportWidth - imageWidth * safeScale) / 2.0,
            (viewportHeight - imageHeight * safeScale) / 2.0,
            safeScale);
    }

    public static double FitScale(
        double viewportWidth,
        double viewportHeight,
        double imageWidth,
        double imageHeight,
        double marginRatio = 0.08,
        double rotationDegrees = 0)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
        {
            return 1.0;
        }

        var usableWidth = viewportWidth * Math.Clamp(1.0 - marginRatio * 2.0, 0.1, 1.0);
        var usableHeight = viewportHeight * Math.Clamp(1.0 - marginRatio * 2.0, 0.1, 1.0);
        var radians = NormalizeRotation(rotationDegrees) * Math.PI / 180.0;
        var rotatedWidth = Math.Abs(imageWidth * Math.Cos(radians)) + Math.Abs(imageHeight * Math.Sin(radians));
        var rotatedHeight = Math.Abs(imageWidth * Math.Sin(radians)) + Math.Abs(imageHeight * Math.Cos(radians));
        return ClampScale(Math.Min(usableWidth / rotatedWidth, usableHeight / rotatedHeight));
    }

    public static NormalizedReferenceTransform Normalize(
        TransformSnapshot transform,
        double viewportWidth,
        double viewportHeight,
        double imageWidth,
        double imageHeight)
    {
        ValidateDimensions(viewportWidth, viewportHeight, imageWidth, imageHeight);

        var scale = ClampScale(transform.Scale);
        var visualWidth = imageWidth * scale;
        var visualHeight = imageHeight * scale;
        return new NormalizedReferenceTransform(
            (transform.X + visualWidth / 2.0) / viewportWidth,
            (transform.Y + visualHeight / 2.0) / viewportHeight,
            visualWidth / viewportWidth);
    }

    public static TransformSnapshot Denormalize(
        NormalizedReferenceTransform normalized,
        double viewportWidth,
        double viewportHeight,
        double imageWidth,
        double imageHeight)
    {
        ValidateDimensions(viewportWidth, viewportHeight, imageWidth, imageHeight);
        if (!double.IsFinite(normalized.CenterXRatio) ||
            !double.IsFinite(normalized.CenterYRatio) ||
            !double.IsFinite(normalized.VisualWidthRatio) ||
            normalized.VisualWidthRatio <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(normalized),
                "Normalized reference values must be finite and the visual width ratio must be positive.");
        }

        var scale = ClampScale(normalized.VisualWidthRatio * viewportWidth / imageWidth);
        var visualWidth = imageWidth * scale;
        var visualHeight = imageHeight * scale;
        var centerX = normalized.CenterXRatio * viewportWidth;
        var centerY = normalized.CenterYRatio * viewportHeight;
        return new TransformSnapshot(
            centerX - visualWidth / 2.0,
            centerY - visualHeight / 2.0,
            scale);
    }

    public static double ClampScale(double scale)
    {
        if (!double.IsFinite(scale))
        {
            return 1.0;
        }

        return Math.Clamp(scale, MinimumScale, MaximumScale);
    }

    public static double NormalizeRotation(double degrees)
    {
        if (!double.IsFinite(degrees))
        {
            return 0;
        }

        var normalized = degrees % 360.0;
        if (normalized >= 180)
        {
            normalized -= 360;
        }
        else if (normalized < -180)
        {
            normalized += 360;
        }

        return Math.Abs(normalized) < 0.000001 ? 0 : normalized;
    }

    private static PointD TransformImageOffset(
        double offsetX,
        double offsetY,
        double scale,
        double rotationDegrees,
        bool flipHorizontal,
        bool flipVertical)
    {
        var radians = NormalizeRotation(rotationDegrees) * Math.PI / 180.0;
        var scaledX = offsetX * scale * (flipHorizontal ? -1.0 : 1.0);
        var scaledY = offsetY * scale * (flipVertical ? -1.0 : 1.0);
        return new PointD(
            scaledX * Math.Cos(radians) - scaledY * Math.Sin(radians),
            scaledX * Math.Sin(radians) + scaledY * Math.Cos(radians));
    }

    private static void ValidateImageDimensions(double imageWidth, double imageHeight)
    {
        if (!double.IsFinite(imageWidth) || imageWidth <= 0 ||
            !double.IsFinite(imageHeight) || imageHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(imageWidth), "Image dimensions must be finite and positive.");
        }
    }

    private static void ValidateDimensions(
        double viewportWidth,
        double viewportHeight,
        double imageWidth,
        double imageHeight)
    {
        if (!double.IsFinite(viewportWidth) || viewportWidth <= 0 ||
            !double.IsFinite(viewportHeight) || viewportHeight <= 0 ||
            !double.IsFinite(imageWidth) || imageWidth <= 0 ||
            !double.IsFinite(imageHeight) || imageHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(viewportWidth),
                "Viewport and image dimensions must be finite and positive.");
        }
    }
}

internal static class ReferenceVisualTransformExtensions
{
    public static TransformSnapshot ToSnapshot(this ReferenceVisualTransform transform) =>
        new(transform.X, transform.Y, transform.Scale);
}
