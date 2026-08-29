using TraceDeckFE.Models;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TraceDeckFE.Tests;

public sealed class ReferenceTransformMathTests
{
    [Fact]
    public void ZoomAt_PreservesTheImagePointUnderTheCursor()
    {
        var original = new TransformSnapshot(100, 50, 2.0);
        var cursor = new PointD(340, 210);
        var imageXBefore = (cursor.X - original.X) / original.Scale;
        var imageYBefore = (cursor.Y - original.Y) / original.Scale;

        var zoomed = ReferenceTransformMath.ZoomAt(original, cursor, 1.10);

        var imageXAfter = (cursor.X - zoomed.X) / zoomed.Scale;
        var imageYAfter = (cursor.Y - zoomed.Y) / zoomed.Scale;
        Assert.Equal(imageXBefore, imageXAfter, precision: 10);
        Assert.Equal(imageYBefore, imageYAfter, precision: 10);
    }

    [Theory]
    [InlineData(37, false, false)]
    [InlineData(-91, true, false)]
    [InlineData(143, false, true)]
    [InlineData(-179, true, true)]
    public void VisualTransform_ImageAndDisplayMappingsAreInvertible(
        double rotation,
        bool flipHorizontal,
        bool flipVertical)
    {
        var transform = new ReferenceVisualTransform(132.5, 87.25, 1.73, rotation, flipHorizontal, flipVertical);
        var original = new PointD(117.25, 63.75);

        var display = ReferenceTransformMath.ImageToDisplay(transform, original, 640, 360);
        var returned = ReferenceTransformMath.DisplayToImage(transform, display, 640, 360);

        Assert.Equal(original.X, returned.X, precision: 9);
        Assert.Equal(original.Y, returned.Y, precision: 9);
    }

    [Fact]
    public void ZoomAt_WithRotationAndFlipsPreservesCursorImagePoint()
    {
        var original = new ReferenceVisualTransform(205, 113, 0.84, 63, true, false);
        var cursor = new PointD(510, 355);
        var before = ReferenceTransformMath.DisplayToImage(original, cursor, 900, 500);

        var zoomed = ReferenceTransformMath.ZoomAt(original, cursor, 1.10, 900, 500);
        var after = ReferenceTransformMath.DisplayToImage(zoomed, cursor, 900, 500);

        Assert.Equal(before.X, after.X, precision: 9);
        Assert.Equal(before.Y, after.Y, precision: 9);
        Assert.Equal(original.RotationDegrees, zoomed.RotationDegrees);
        Assert.Equal(original.FlipHorizontal, zoomed.FlipHorizontal);
        Assert.Equal(original.FlipVertical, zoomed.FlipVertical);
    }

    [Fact]
    public void RotationAndFlip_PreserveVisualCenter()
    {
        var transform = new ReferenceVisualTransform(90, 70, 1.25, 0, false, false);
        var imageCenter = new PointD(320, 180);
        var before = ReferenceTransformMath.ImageToDisplay(transform, imageCenter, 640, 360);
        var changed = transform with { RotationDegrees = 127, FlipHorizontal = true, FlipVertical = true };

        var after = ReferenceTransformMath.ImageToDisplay(changed, imageCenter, 640, 360);

        Assert.Equal(before.X, after.X, precision: 10);
        Assert.Equal(before.Y, after.Y, precision: 10);
    }

    [Fact]
    public void ContainsDisplayPoint_UsesRotatedImageBounds()
    {
        var transform = new ReferenceVisualTransform(100, 100, 1, 45, false, false);
        var inside = ReferenceTransformMath.ImageToDisplay(transform, new PointD(10, 10), 200, 100);

        Assert.True(ReferenceTransformMath.ContainsDisplayPoint(transform, inside, 200, 100));
        Assert.False(ReferenceTransformMath.ContainsDisplayPoint(transform, new PointD(-500, -500), 200, 100));
    }

    [Theory]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(0.001, ReferenceTransformMath.MinimumScale)]
    [InlineData(100.0, ReferenceTransformMath.MaximumScale)]
    public void ClampScale_RejectsNonFiniteAndExtremeValues(double input, double expected)
    {
        Assert.Equal(expected, ReferenceTransformMath.ClampScale(input), precision: 10);
    }

    [Fact]
    public void FitScale_UsesBothViewportDimensionsAndKeepsMargins()
    {
        var scale = ReferenceTransformMath.FitScale(1000, 500, 2000, 500, marginRatio: 0.10);

        Assert.Equal(0.4, scale, precision: 10);
    }

    [Fact]
    public void Center_PlacesScaledImageAtViewportCenter()
    {
        var centered = ReferenceTransformMath.Center(1000, 600, 400, 200, 1.5);

        Assert.Equal(200, centered.X, precision: 10);
        Assert.Equal(150, centered.Y, precision: 10);
    }

    [Fact]
    public void NormalizedTransform_PreservesRelativeCenterAndVisualWidthAcrossResize()
    {
        var original = new TransformSnapshot(410, 230, 0.8);
        var normalized = ReferenceTransformMath.Normalize(
            original, 1600, 900, imageWidth: 800, imageHeight: 400);

        var resized = ReferenceTransformMath.Denormalize(
            normalized, 1000, 700, imageWidth: 800, imageHeight: 400);
        var resizedNormalized = ReferenceTransformMath.Normalize(
            resized, 1000, 700, imageWidth: 800, imageHeight: 400);

        Assert.Equal(normalized.CenterXRatio, resizedNormalized.CenterXRatio, precision: 10);
        Assert.Equal(normalized.CenterYRatio, resizedNormalized.CenterYRatio, precision: 10);
        Assert.Equal(normalized.VisualWidthRatio, resizedNormalized.VisualWidthRatio, precision: 10);
    }

    [Fact]
    public void NormalizedTransform_RepeatedResizeUsesCanonicalStateWithoutDrift()
    {
        var original = new TransformSnapshot(307.25, 146.75, 0.73);
        var normalized = ReferenceTransformMath.Normalize(
            original, 1920, 1080, imageWidth: 1000, imageHeight: 500);

        _ = ReferenceTransformMath.Denormalize(
            normalized, 960, 540, imageWidth: 1000, imageHeight: 500);
        _ = ReferenceTransformMath.Denormalize(
            normalized, 1366, 768, imageWidth: 1000, imageHeight: 500);
        var returned = ReferenceTransformMath.Denormalize(
            normalized, 1920, 1080, imageWidth: 1000, imageHeight: 500);

        Assert.Equal(original.X, returned.X, precision: 10);
        Assert.Equal(original.Y, returned.Y, precision: 10);
        Assert.Equal(original.Scale, returned.Scale, precision: 10);
    }

    [Fact]
    public void Denormalize_UsesUniformScaleAndPreservesImageAspectRatio()
    {
        var normalized = new NormalizedReferenceTransform(0.5, 0.5, 0.4);
        var result = ReferenceTransformMath.Denormalize(
            normalized, 1200, 700, imageWidth: 800, imageHeight: 400);

        var visualWidth = 800 * result.Scale;
        var visualHeight = 400 * result.Scale;
        Assert.Equal(0.4, visualWidth / 1200, precision: 10);
        Assert.Equal(2.0, visualWidth / visualHeight, precision: 10);
    }

    [Fact]
    public void ReferenceState_RepeatedViewportUpdatesDoNotRewriteCanonicalTransform()
    {
        var reference = new ReferenceState();
        reference.SetImage(CreateTestSource(), 1600, 900);
        reference.MoveBy(137.25, -62.5);
        reference.ZoomAt(new PointD(800, 450), 0.73);
        var original = new TransformSnapshot(reference.X, reference.Y, reference.Scale);
        var canonical = reference.NormalizedTransform;

        reference.UpdateViewport(900, 600);
        Assert.Equal(canonical, reference.NormalizedTransform);
        reference.UpdateViewport(1280, 720);
        Assert.Equal(canonical, reference.NormalizedTransform);
        reference.UpdateViewport(1600, 900);

        Assert.Equal(canonical, reference.NormalizedTransform);
        Assert.Equal(original.X, reference.X, precision: 10);
        Assert.Equal(original.Y, reference.Y, precision: 10);
        Assert.Equal(original.Scale, reference.Scale, precision: 10);
    }

    [Fact]
    public void ReferenceState_MoveOnlyViewportUpdateLeavesPixelTransformUntouched()
    {
        var reference = new ReferenceState();
        reference.SetImage(CreateTestSource(), 1280, 720);
        reference.MoveBy(30, -20);
        var original = new TransformSnapshot(reference.X, reference.Y, reference.Scale);

        reference.UpdateViewport(1280, 720);

        Assert.Equal(original.X, reference.X);
        Assert.Equal(original.Y, reference.Y);
        Assert.Equal(original.Scale, reference.Scale);
    }

    [Fact]
    public void ReferenceState_FirstImageLoadNotifiesHasImageAfterBitmapAssignment()
    {
        var reference = new ReferenceState();
        var observedValues = new List<bool>();
        reference.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ReferenceState.HasImage))
            {
                observedValues.Add(reference.HasImage);
            }
        };

        reference.SetImage(CreateTestSource(), 1280, 720);

        Assert.True(reference.HasImage);
        Assert.True(observedValues.Last());
    }

    [Fact]
    public void ReferenceState_RejectsStaleDisplayResultAfterSourceReplacement()
    {
        var reference = new ReferenceState();
        var first = CreateTestSource();
        var second = CreateTestSource() with { Id = Guid.NewGuid(), Name = "second.png" };
        reference.SetImage(first, 1280, 720);
        reference.SetImage(second, 1280, 720);
        var currentDisplay = reference.Image;

        reference.SetDisplayImage(first.Id, first.OriginalBitmap);

        Assert.Same(currentDisplay, reference.Image);
        Assert.Equal(second.Id, reference.Source?.Id);
    }

    [Fact]
    public void ReferenceInputSettings_EnablesReplacementConfirmationByDefault()
    {
        Assert.True(new ReferenceInputSettings().ConfirmReplacement);
    }

    [Fact]
    public void ReferenceState_ResizeWithRotationAndBothFlipsReturnsWithoutDrift()
    {
        var reference = new ReferenceState();
        reference.SetImage(CreateTestSource(), 1920, 1080);
        reference.MoveBy(81.25, -47.5);
        reference.ZoomAt(new PointD(930, 520), 0.83);
        reference.Rotation = 73;
        reference.FlipHorizontal = true;
        reference.FlipVertical = true;
        var original = reference.VisualTransform;
        var canonical = reference.NormalizedTransform;

        reference.UpdateViewport(900, 700);
        reference.UpdateViewport(1366, 768);
        reference.UpdateViewport(1920, 1080);

        Assert.Equal(canonical, reference.NormalizedTransform);
        Assert.Equal(original.X, reference.X, precision: 9);
        Assert.Equal(original.Y, reference.Y, precision: 9);
        Assert.Equal(original.Scale, reference.Scale, precision: 9);
        Assert.Equal(original.RotationDegrees, reference.Rotation);
        Assert.True(reference.FlipHorizontal);
        Assert.True(reference.FlipVertical);
    }

    [Fact]
    public void ReferenceState_ResetTransformKeepsCenterAndResetsOnlyTransformComponents()
    {
        var reference = new ReferenceState();
        reference.SetImage(CreateTestSource(), 1280, 720);
        reference.MoveBy(123, -45);
        reference.ZoomAt(new PointD(640, 360), 1.7);
        reference.Rotation = 45;
        reference.FlipHorizontal = true;
        var centerX = reference.X + reference.ImageWidth * reference.Scale / 2.0;
        var centerY = reference.Y + reference.ImageHeight * reference.Scale / 2.0;

        reference.ResetTransform();

        Assert.Equal(1, reference.Scale);
        Assert.Equal(0, reference.Rotation);
        Assert.False(reference.FlipHorizontal);
        Assert.False(reference.FlipVertical);
        Assert.Equal(centerX, reference.X + reference.ImageWidth * reference.Scale / 2.0, precision: 9);
        Assert.Equal(centerY, reference.Y + reference.ImageHeight * reference.Scale / 2.0, precision: 9);
    }

    private static BitmapSource CreateTestImage()
    {
        const int width = 1000;
        const int height = 500;
        var image = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Gray8,
            palette: null,
            new byte[width * height],
            stride: width);
        image.Freeze();
        return image;
    }

    private static ReferenceImageSource CreateTestSource()
    {
        var image = CreateTestImage();
        return new ReferenceImageSource(
            Guid.NewGuid(),
            "reference.png",
            "reference.png",
            "PNG",
            image.PixelWidth,
            image.PixelHeight,
            false,
            false,
            [1, 2, 3],
            image);
    }
}
