using System.Windows.Media;
using System.Windows.Media.Imaging;
using TraceDeckFE.Models;

namespace TraceDeckFE.Services;

public sealed class AutoPaletteService
{
    private const int MaximumAnalyzedPixels = 262_144;
    private readonly ReferenceColorService _colorService;

    public AutoPaletteService(ReferenceColorService colorService)
    {
        _colorService = colorService;
    }

    public async Task<IReadOnlyList<RgbaColor>> GenerateAsync(
        ReferenceImageSource source,
        int colorCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        colorCount = Math.Clamp(colorCount, 2, 12);
        var surface = await _colorService.GetOriginalAnalysisSurfaceAsync(source, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => Analyze(surface, colorCount, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<RgbaColor> Analyze(
        BitmapSource source,
        int colorCount,
        CancellationToken cancellationToken)
    {
        var converted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        if (converted.CanFreeze && !converted.IsFrozen)
        {
            converted.Freeze();
        }

        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        converted.CopyPixels(pixels, stride, 0);
        var step = Math.Max(1, (int)Math.Floor(Math.Sqrt((double)width * height / MaximumAnalyzedPixels)));
        var bins = new Dictionary<int, ColorBin>();

        for (var y = 0; y < height; y += step)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x += step)
            {
                var index = y * stride + x * 4;
                var alpha = pixels[index + 3];
                if (alpha == 0)
                {
                    continue;
                }

                var blue = pixels[index];
                var green = pixels[index + 1];
                var red = pixels[index + 2];
                var key = (red >> 3) << 10 | (green >> 3) << 5 | (blue >> 3);
                bins.TryGetValue(key, out var bin);
                bins[key] = bin.Add(red, green, blue, alpha);
            }
        }

        var results = new List<RgbaColor>(colorCount);
        foreach (var bin in bins.Values.OrderByDescending(value => value.Count))
        {
            var candidate = bin.ToColor();
            if (results.Any(existing => ColorDistance(existing, candidate) < 24))
            {
                continue;
            }

            results.Add(candidate);
            if (results.Count == colorCount)
            {
                break;
            }
        }

        return results;
    }

    private static double ColorDistance(RgbaColor left, RgbaColor right)
    {
        var red = left.Red - right.Red;
        var green = left.Green - right.Green;
        var blue = left.Blue - right.Blue;
        return Math.Sqrt(red * red + green * green + blue * blue);
    }

    private readonly record struct ColorBin(long Red, long Green, long Blue, long Alpha, long Count)
    {
        public ColorBin Add(byte red, byte green, byte blue, byte alpha) =>
            new(Red + red, Green + green, Blue + blue, Alpha + alpha, Count + 1);

        public RgbaColor ToColor() => new(
            (byte)(Red / Count),
            (byte)(Green / Count),
            (byte)(Blue / Count),
            (byte)(Alpha / Count));
    }
}
