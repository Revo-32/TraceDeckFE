using ImageMagick;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TraceDeckFE.Models;
using TraceDeckFE.Services;

namespace TraceDeckFE.Tests;

public sealed class ReferenceImageServiceTests
{
    [Theory]
    [InlineData(".png", MagickFormat.Png)]
    [InlineData(".jpg", MagickFormat.Jpeg)]
    [InlineData(".jpeg", MagickFormat.Jpeg)]
    [InlineData(".webp", MagickFormat.WebP)]
    [InlineData(".bmp", MagickFormat.Bmp)]
    [InlineData(".tiff", MagickFormat.Tiff)]
    [InlineData(".tif", MagickFormat.Tiff)]
    [InlineData(".ico", MagickFormat.Ico)]
    [InlineData(".avif", MagickFormat.Avif)]
    [InlineData(".gif", MagickFormat.Gif)]
    public async Task LoadAsync_DecodesSupportedRasterFormat(string extension, MagickFormat format)
    {
        var path = TempPath(extension);
        using (var fixture = new MagickImage(new MagickColor("#80D05020"), 7, 5))
        {
            fixture.Write(path, format);
        }

        try
        {
            var service = new ReferenceImageService(new NullLogger());
            var source = await service.LoadAsync(path, 1280, 720);

            Assert.Equal(7, source.PixelWidth);
            Assert.Equal(5, source.PixelHeight);
            Assert.True(source.OriginalBitmap.IsFrozen);
            Assert.False(source.IsVector);
            Assert.NotEmpty(source.OriginalBytes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_PreservesPngAlpha()
    {
        var path = TempPath(".png");
        using (var fixture = new MagickImage(MagickColors.Transparent, 3, 2))
        {
            fixture.Write(path, MagickFormat.Png);
        }

        try
        {
            var source = await new ReferenceImageService(new NullLogger()).LoadAsync(path, 800, 600);

            Assert.True(source.HasAlpha);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(".gif")]
    [InlineData(".webp")]
    [InlineData(".tiff")]
    public async Task LoadAsync_UsesFirstFrameOrPage(string extension)
    {
        var path = TempPath(extension);
        using (var frames = new MagickImageCollection())
        {
            frames.Add(new MagickImage(MagickColors.Red, 4, 3));
            frames.Add(new MagickImage(MagickColors.Blue, 4, 3));
            frames.Write(path);
        }

        try
        {
            var source = await new ReferenceImageService(new NullLogger()).LoadAsync(path, 800, 600);
            var pixel = FirstBgraPixel(source.OriginalBitmap);

            Assert.True(pixel.Red > pixel.Blue, "The first red frame/page should be selected.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_IcoSelectsLargestFrame()
    {
        var path = TempPath(".ico");
        using (var frames = new MagickImageCollection())
        {
            frames.Add(new MagickImage(MagickColors.Red, 16, 16));
            frames.Add(new MagickImage(MagickColors.Blue, 64, 64));
            frames.Write(path);
        }

        try
        {
            var source = await new ReferenceImageService(new NullLogger()).LoadAsync(path, 800, 600);

            Assert.Equal(64, source.PixelWidth);
            Assert.Equal(64, source.PixelHeight);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAndRenderSvg_PreservesVectorSourceAndRendersRequestedResolution()
    {
        var path = TempPath(".svg");
        var svg = """
                  <svg xmlns="http://www.w3.org/2000/svg" width="200" height="100" viewBox="0 0 200 100">
                    <rect width="200" height="100" fill="#80c0ff"/>
                    <circle cx="100" cy="50" r="30" fill="#202020"/>
                  </svg>
                  """;
        await File.WriteAllTextAsync(path, svg);

        try
        {
            var service = new ReferenceImageService(new NullLogger());
            var source = await service.LoadAsync(path, 1000, 600);
            var rendered = await service.RenderDisplayAsync(source, false, 0, 600, 300);

            Assert.True(source.IsVector);
            Assert.Equal(200, source.PixelWidth);
            Assert.Equal(100, source.PixelHeight);
            Assert.Equal(svg, System.Text.Encoding.UTF8.GetString(source.OriginalBytes));
            Assert.Equal(600, rendered.PixelWidth);
            Assert.Equal(300, rendered.PixelHeight);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RenderDisplayAsync_AppliesEffectsWithoutMutatingOriginal()
    {
        var path = TempPath(".png");
        using (var fixture = new MagickImage(new MagickColor("#FF8040"), 4, 3))
        {
            fixture.Write(path, MagickFormat.Png);
        }

        try
        {
            var service = new ReferenceImageService(new NullLogger());
            var source = await service.LoadAsync(path, 800, 600);
            var original = source.OriginalBitmap;
            var result = await service.RenderDisplayAsync(source, true, 30, 4, 3);

            Assert.NotSame(original, result);
            Assert.Same(original, source.OriginalBitmap);
            Assert.True(result.IsFrozen);
            var pixel = FirstBgraPixel(result);
            Assert.Equal(pixel.Red, pixel.Green);
            Assert.Equal(pixel.Green, pixel.Blue);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_CorruptedImageFailsWithoutReturningReplacement()
    {
        var path = TempPath(".png");
        await File.WriteAllBytesAsync(path, [0x89, 0x50, 0x4e, 0x47, 0x00]);

        try
        {
            var service = new ReferenceImageService(new NullLogger());

            await Assert.ThrowsAsync<InvalidDataException>(() => service.LoadAsync(path, 1280, 720));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_HonorsPreCanceledRequest()
    {
        var path = TempPath(".png");
        using (var fixture = new MagickImage(MagickColors.Red, 2, 2))
        {
            fixture.Write(path, MagickFormat.Png);
        }

        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var service = new ReferenceImageService(new NullLogger());

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.LoadAsync(path, 1280, 720, cancellation.Token));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_RejectsUnsupportedExtension()
    {
        var service = new ReferenceImageService(new NullLogger());

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => service.LoadAsync("reference.psd", 1280, 720));

        Assert.Contains("not supported", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClipboardBitmap_UsesLosslessCanonicalPngAndRoundTripsPixels()
    {
        var pixels = new byte[]
        {
            30, 20, 10, 255,
            60, 50, 40, 128
        };
        var bitmap = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
        bitmap.Freeze();
        var service = new ReferenceImageService(new NullLogger());

        var clipboardSource = await service.LoadClipboardBitmapAsync(bitmap, 1280, 720);
        var reopened = await service.LoadEmbeddedAsync(
            clipboardSource.OriginalBytes,
            clipboardSource.Name,
            clipboardSource.Format,
            clipboardSource.SourceKind,
            1280,
            720);

        Assert.Equal(ReferenceSourceKind.Clipboard, clipboardSource.SourceKind);
        Assert.Equal("PNG", clipboardSource.Format);
        Assert.Equal(clipboardSource.OriginalBytes, reopened.OriginalBytes);
        Assert.Equal(ReadBgraPixels(clipboardSource.OriginalBitmap), ReadBgraPixels(reopened.OriginalBitmap));
    }

    [Fact]
    public void SupportedPath_RecognizesEveryMilestoneTwoExtension()
    {
        foreach (var extension in new[]
                 {
                     ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tiff", ".tif", ".svg", ".ico", ".avif", ".gif"
                 })
        {
            Assert.True(ReferenceImageService.IsSupportedPath("reference" + extension));
        }
    }

    private static string TempPath(string extension) =>
        Path.Combine(Path.GetTempPath(), $"tracedeck-{Guid.NewGuid():N}{extension}");

    private static (byte Blue, byte Green, byte Red, byte Alpha) FirstBgraPixel(BitmapSource source)
    {
        var converted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        converted.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
        return (pixel[0], pixel[1], pixel[2], pixel[3]);
    }

    private static byte[] ReadBgraPixels(BitmapSource source)
    {
        var converted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private sealed class NullLogger : ITraceLogger
    {
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
