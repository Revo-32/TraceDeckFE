using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TraceDeckFE.Models;

namespace TraceDeckFE.Services;

public sealed class ReferenceColorService
{
    private readonly ReferenceImageService _imageService;
    private readonly ConcurrentDictionary<SurfaceKey, Lazy<Task<BitmapSource>>> _vectorSurfaces = new();

    public ReferenceColorService(ReferenceImageService imageService)
    {
        _imageService = imageService;
    }

    public async Task<RgbaColor?> SampleDisplayAsync(
        ReferenceImageSource source,
        ReferenceVisualTransform transform,
        PointD displayPoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!ReferenceTransformMath.TryMapDisplayToImagePixel(
                transform,
                displayPoint,
                source.PixelWidth,
                source.PixelHeight,
                out _,
                out var imagePoint))
        {
            return null;
        }

        var surface = await GetSurfaceAsync(source, transform.Scale, cancellationToken).ConfigureAwait(false);
        return ReadLogicalPixel(surface, source.PixelWidth, source.PixelHeight, imagePoint);
    }

    public async Task<BitmapSource?> CreateMagnifierAsync(
        ReferenceImageSource source,
        ReferenceVisualTransform transform,
        PointD displayPoint,
        int diameter = 11,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        diameter = Math.Clamp(diameter | 1, 5, 31);
        if (!ReferenceTransformMath.TryMapDisplayToImagePixel(
                transform,
                displayPoint,
                source.PixelWidth,
                source.PixelHeight,
                out var center,
                out _))
        {
            return null;
        }

        var surface = await GetSurfaceAsync(source, transform.Scale, cancellationToken).ConfigureAwait(false);
        var pixels = new byte[diameter * diameter * 4];
        var radius = diameter / 2;
        for (var y = 0; y < diameter; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < diameter; x++)
            {
                var logicalX = center.X + x - radius;
                var logicalY = center.Y + y - radius;
                if (logicalX < 0 || logicalY < 0 || logicalX >= source.PixelWidth || logicalY >= source.PixelHeight)
                {
                    continue;
                }

                var color = ReadLogicalPixel(
                    surface,
                    source.PixelWidth,
                    source.PixelHeight,
                    new PointD(logicalX + 0.5, logicalY + 0.5));
                var index = (y * diameter + x) * 4;
                pixels[index] = color.Blue;
                pixels[index + 1] = color.Green;
                pixels[index + 2] = color.Red;
                pixels[index + 3] = color.Alpha;
            }
        }

        var bitmap = BitmapSource.Create(
            diameter,
            diameter,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            diameter * 4);
        bitmap.Freeze();
        return bitmap;
    }

    public async Task<BitmapSource> GetOriginalAnalysisSurfaceAsync(
        ReferenceImageSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return await GetSurfaceAsync(source, 1, cancellationToken).ConfigureAwait(false);
    }

    private async Task<BitmapSource> GetSurfaceAsync(
        ReferenceImageSource source,
        double visualScale,
        CancellationToken cancellationToken)
    {
        if (!source.IsVector)
        {
            return EnsureBgra32(source.OriginalBitmap);
        }

        var quality = Math.Max(1, ReferenceTransformMath.ClampScale(visualScale));
        var width = Math.Clamp((int)Math.Ceiling(source.PixelWidth * quality), source.PixelWidth, 16384);
        var height = Math.Clamp((int)Math.Ceiling(source.PixelHeight * quality), source.PixelHeight, 16384);
        var key = new SurfaceKey(source.Id, width, height);
        var lazy = _vectorSurfaces.GetOrAdd(key, _ => new Lazy<Task<BitmapSource>>(
            async () => EnsureBgra32(await _imageService.RenderOriginalSourceAsync(
                source,
                width,
                height,
                CancellationToken.None).ConfigureAwait(false)),
            LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _vectorSurfaces.TryRemove(key, out _);
            throw;
        }
    }

    private static RgbaColor ReadLogicalPixel(
        BitmapSource surface,
        int logicalWidth,
        int logicalHeight,
        PointD logicalPoint)
    {
        var x = Math.Clamp((int)Math.Floor(logicalPoint.X * surface.PixelWidth / logicalWidth), 0, surface.PixelWidth - 1);
        var y = Math.Clamp((int)Math.Floor(logicalPoint.Y * surface.PixelHeight / logicalHeight), 0, surface.PixelHeight - 1);
        var pixel = new byte[4];
        surface.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return new RgbaColor(pixel[2], pixel[1], pixel[0], pixel[3]);
    }

    private static BitmapSource EnsureBgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Bgra32)
        {
            return source;
        }

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private readonly record struct SurfaceKey(Guid SourceId, int Width, int Height);
}
