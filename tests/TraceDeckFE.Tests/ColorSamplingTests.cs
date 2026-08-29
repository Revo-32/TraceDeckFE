using System.Text;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TraceDeckFE.Models;
using TraceDeckFE.Services;

namespace TraceDeckFE.Tests;

public sealed class ColorSamplingTests
{
    [Theory]
    [InlineData(0, false, false, 1.0)]
    [InlineData(90, false, false, 1.0)]
    [InlineData(37, false, false, 1.0)]
    [InlineData(0, true, false, 1.0)]
    [InlineData(0, false, true, 1.0)]
    [InlineData(0, true, true, 1.0)]
    [InlineData(0, false, false, 2.25)]
    [InlineData(63, true, false, 1.7)]
    [InlineData(-121, false, true, 0.7)]
    [InlineData(147, true, true, 2.4)]
    public async Task SampleDisplayAsync_MapsEveryTransformToOriginalPixel(
        double rotation,
        bool flipHorizontal,
        bool flipVertical,
        double scale)
    {
        var source = CreateDeterministicSource();
        var service = CreateService();
        var transform = new ReferenceVisualTransform(120.25, 83.75, scale, rotation, flipHorizontal, flipVertical);
        var originalPoint = new PointD(2.5, 1.5);
        var displayPoint = ReferenceTransformMath.ImageToDisplay(transform, originalPoint, 4, 4);

        var color = await service.SampleDisplayAsync(source, transform, displayPoint);

        Assert.Equal(PixelColor(2, 1), color);
    }

    [Fact]
    public async Task SampleDisplayAsync_TargetResizeKeepsOriginalPixelMapping()
    {
        var source = CreateDeterministicSource();
        var service = CreateService();
        var original = new TransformSnapshot(430, 180, 40);
        var normalized = ReferenceTransformMath.Normalize(original, 1600, 900, 4, 4);
        var resized = ReferenceTransformMath.Denormalize(normalized, 900, 700, 4, 4);
        var transform = new ReferenceVisualTransform(resized.X, resized.Y, resized.Scale, 73, true, false);
        var display = ReferenceTransformMath.ImageToDisplay(transform, new PointD(1.5, 3.5), 4, 4);

        var color = await service.SampleDisplayAsync(source, transform, display);

        Assert.Equal(PixelColor(1, 3), color);
    }

    [Fact]
    public void TransformRoundTrip_ResizeBackToOriginalHasNoSamplingDrift()
    {
        var initial = new TransformSnapshot(317.125, 112.875, 13.3333333333333);
        var normalized = ReferenceTransformMath.Normalize(initial, 1920, 1080, 4, 4);
        _ = ReferenceTransformMath.Denormalize(normalized, 810, 600, 4, 4);
        var returned = ReferenceTransformMath.Denormalize(normalized, 1920, 1080, 4, 4);

        Assert.Equal(initial.X, returned.X, precision: 10);
        Assert.Equal(initial.Y, returned.Y, precision: 10);
        Assert.Equal(initial.Scale, returned.Scale, precision: 10);
    }

    [Fact]
    public async Task SampleDisplayAsync_OutsideReferenceReturnsNullAndInsideReturnsColor()
    {
        var source = CreateDeterministicSource();
        var service = CreateService();
        var transform = new ReferenceVisualTransform(10, 20, 4, 32, true, true);
        var inside = ReferenceTransformMath.ImageToDisplay(transform, new PointD(0.5, 0.5), 4, 4);

        Assert.Null(await service.SampleDisplayAsync(source, transform, new PointD(-900, -900)));
        Assert.Equal(PixelColor(0, 0), await service.SampleDisplayAsync(source, transform, inside));
    }

    [Fact]
    public async Task Sampling_UsesOriginalBitmapIncludingTransparentRgba()
    {
        var pixels = new byte[] { 33, 22, 11, 0 };
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
        bitmap.Freeze();
        var source = new ReferenceImageSource(Guid.NewGuid(), "alpha.png", null, "PNG", 1, 1, true, false, [1], bitmap);

        var color = await CreateService().SampleDisplayAsync(
            source,
            new ReferenceVisualTransform(0, 0, 1, 0, false, false),
            new PointD(0.5, 0.5));

        Assert.Equal(new RgbaColor(11, 22, 33, 0), color);
    }

    [Fact]
    public async Task SvgSampling_UsesOriginalVectorSourceRegionsAndTransparency()
    {
        var path = TempPath(".svg");
        var svg = """
                  <svg xmlns="http://www.w3.org/2000/svg" width="100" height="40" viewBox="0 0 100 40">
                    <rect x="0" y="0" width="40" height="40" fill="#ff0000"/>
                    <rect x="60" y="0" width="40" height="40" fill="#0000ff"/>
                  </svg>
                  """;
        await File.WriteAllTextAsync(path, svg);
        try
        {
            var imageService = new ReferenceImageService(new NullLogger());
            var source = await imageService.LoadAsync(path, 800, 600);
            var service = new ReferenceColorService(imageService);
            var transform = new ReferenceVisualTransform(20, 30, 3, 25, true, false);

            var red = await service.SampleDisplayAsync(source, transform,
                ReferenceTransformMath.ImageToDisplay(transform, new PointD(20, 20), 100, 40));
            var transparent = await service.SampleDisplayAsync(source, transform,
                ReferenceTransformMath.ImageToDisplay(transform, new PointD(50, 20), 100, 40));
            var blue = await service.SampleDisplayAsync(source, transform,
                ReferenceTransformMath.ImageToDisplay(transform, new PointD(80, 20), 100, 40));

            Assert.True(red is { Red: > 245, Green: < 10, Blue: < 10, Alpha: 255 });
            Assert.True(transparent is { Alpha: 0 });
            Assert.True(blue is { Red: < 10, Green: < 10, Blue: > 245, Alpha: 255 });
        }
        finally { File.Delete(path); }
    }

    private static ReferenceColorService CreateService() =>
        new(new ReferenceImageService(new NullLogger()));

    private static ReferenceImageSource CreateDeterministicSource()
    {
        var pixels = new byte[4 * 4 * 4];
        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 4; x++)
        {
            var color = PixelColor(x, y);
            var index = (y * 4 + x) * 4;
            pixels[index] = color.Blue;
            pixels[index + 1] = color.Green;
            pixels[index + 2] = color.Red;
            pixels[index + 3] = color.Alpha;
        }
        var bitmap = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, pixels, 16);
        bitmap.Freeze();
        return new ReferenceImageSource(Guid.NewGuid(), "fixture.png", null, "PNG", 4, 4, true, false, [1, 2, 3], bitmap);
    }

    private static RgbaColor PixelColor(int x, int y) =>
        new((byte)(15 + x * 50), (byte)(20 + y * 45), (byte)(10 + x * 9 + y * 7), (byte)(255 - (x + y) * 10));

    private static string TempPath(string extension) => Path.Combine(Path.GetTempPath(), $"tracedeck-{Guid.NewGuid():N}{extension}");

    private sealed class NullLogger : ITraceLogger
    {
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
