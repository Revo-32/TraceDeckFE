using TraceDeckFE.Localization;
using ImageMagick;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TraceDeckFE.Models;

namespace TraceDeckFE.Services;

public sealed class ReferenceImageService
{
    private static readonly HashSet<string> SupportedExtensionSet = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tiff", ".tif", ".svg", ".ico", ".avif", ".gif"
    };

    private readonly ITraceLogger _logger;

    public ReferenceImageService(ITraceLogger logger)
    {
        _logger = logger;
    }

    public static string OpenFileDialogFilter =>
        L.Get("File.Images") + "|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.tiff;*.tif;*.svg;*.ico;*.avif;*.gif|" +
        "PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|WebP (*.webp)|*.webp|" +
        "BMP (*.bmp)|*.bmp|TIFF (*.tiff;*.tif)|*.tiff;*.tif|SVG (*.svg)|*.svg|" +
        "Icon (*.ico)|*.ico|AVIF (*.avif)|*.avif|GIF (*.gif)|*.gif";

    public static bool IsSupportedPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && SupportedExtensionSet.Contains(Path.GetExtension(path));

    public async Task<ReferenceImageSource> LoadAsync(
        string path,
        double viewportWidth,
        double viewportHeight,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!IsSupportedPath(path))
        {
            throw new NotSupportedException(L.Get("Error.UnsupportedImage"));
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Error($"Could not read reference image '{path}'.", exception);
            throw new InvalidDataException(L.Get("Error.ImageRead"), exception);
        }

        return await DecodeSourceAsync(
            bytes,
            Path.GetFileName(path),
            path,
            Path.GetExtension(path),
            viewportWidth,
            viewportHeight,
            cancellationToken,
            ReferenceSourceKind.File).ConfigureAwait(false);
    }

    public Task<ReferenceImageSource> LoadClipboardBitmapAsync(
        BitmapSource bitmap,
        double viewportWidth,
        double viewportHeight,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        cancellationToken.ThrowIfCancellationRequested();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return DecodeSourceAsync(
            stream.ToArray(),
            "Clipboard.png",
            sourcePath: null,
            ".png",
            viewportWidth,
            viewportHeight,
            cancellationToken,
            ReferenceSourceKind.Clipboard);
    }

    public Task<ReferenceImageSource> LoadEmbeddedAsync(
        byte[] bytes,
        string originalFilename,
        string sourceFormat,
        ReferenceSourceKind sourceKind,
        double viewportWidth,
        double viewportHeight,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFilename);
        var extension = NormalizeExtension(sourceFormat, originalFilename);
        if (!SupportedExtensionSet.Contains(extension))
        {
            throw new NotSupportedException(L.Get("Error.EmbeddedFormat"));
        }

        return DecodeSourceAsync(
            (byte[])bytes.Clone(),
            Path.GetFileName(originalFilename),
            sourcePath: null,
            extension,
            viewportWidth,
            viewportHeight,
            cancellationToken,
            sourceKind);
    }

    public Task<BitmapSource> RenderOriginalSourceAsync(
        ReferenceImageSource source,
        int targetPixelWidth,
        int targetPixelHeight,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.IsVector)
        {
            return Task.FromResult(source.OriginalBitmap);
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DecodeMagickBitmap(
                source.OriginalBytes,
                source.Format,
                Math.Clamp(targetPixelWidth, 1, 16384),
                Math.Clamp(targetPixelHeight, 1, 16384)).Bitmap;
        }, cancellationToken);
    }

    public Task<BitmapSource> RenderDisplayAsync(
        ReferenceImageSource source,
        bool grayscale,
        double contrast,
        int targetPixelWidth,
        int targetPixelHeight,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var baseImage = source.IsVector
                ? DecodeMagickBitmap(
                    source.OriginalBytes,
                    source.Format,
                    Math.Clamp(targetPixelWidth, 1, 16384),
                    Math.Clamp(targetPixelHeight, 1, 16384)).Bitmap
                : source.OriginalBitmap;
            cancellationToken.ThrowIfCancellationRequested();
            return ApplyEffects(baseImage, grayscale, contrast, cancellationToken);
        }, cancellationToken);
    }

    private static Task<ReferenceImageSource> DecodeSourceAsync(
        byte[] bytes,
        string name,
        string? sourcePath,
        string extension,
        double viewportWidth,
        double viewportHeight,
        CancellationToken cancellationToken,
        ReferenceSourceKind sourceKind)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (bytes.Length == 0)
            {
                throw new InvalidDataException(L.Get("Error.EmptyImage"));
            }

            try
            {
                var isVector = extension.Equals(".svg", StringComparison.OrdinalIgnoreCase);
                var logicalWidth = 0;
                var logicalHeight = 0;
                int? renderWidth = null;
                int? renderHeight = null;

                if (isVector)
                {
                    var info = new MagickImageInfo(bytes);
                    logicalWidth = checked((int)info.Width);
                    logicalHeight = checked((int)info.Height);
                    if (logicalWidth <= 0 || logicalHeight <= 0)
                    {
                        throw new InvalidDataException(L.Get("Error.SvgDimensions"));
                    }

                    var fit = ReferenceTransformMath.FitScale(
                        Math.Max(1, viewportWidth),
                        Math.Max(1, viewportHeight),
                        logicalWidth,
                        logicalHeight);
                    renderWidth = Math.Max(1, (int)Math.Ceiling(logicalWidth * fit));
                    renderHeight = Math.Max(1, (int)Math.Ceiling(logicalHeight * fit));
                }

                var decoded = DecodeMagickBitmap(bytes, extension, renderWidth, renderHeight);
                logicalWidth = isVector ? logicalWidth : decoded.Width;
                logicalHeight = isVector ? logicalHeight : decoded.Height;
                return new ReferenceImageSource(
                    Guid.NewGuid(),
                    name,
                    sourcePath,
                    extension.TrimStart('.').ToUpperInvariant(),
                    logicalWidth,
                    logicalHeight,
                    decoded.HasAlpha,
                    isVector,
                    (byte[])bytes.Clone(),
                    decoded.Bitmap,
                    sourceKind);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception exception) when (exception is MagickException or OverflowException or ArgumentException)
            {
                throw new InvalidDataException(L.Get("Error.MalformedImage"), exception);
            }
        }, cancellationToken);
    }

    private static DecodedBitmap DecodeMagickBitmap(
        byte[] bytes,
        string extension,
        int? targetWidth,
        int? targetHeight)
    {
        var settings = new MagickReadSettings
        {
            BackgroundColor = MagickColors.Transparent,
            Format = GetMagickFormat(extension)
        };
        if (targetWidth is > 0 && targetHeight is > 0)
        {
            settings.Width = checked((uint)targetWidth.Value);
            settings.Height = checked((uint)targetHeight.Value);
        }

        using var images = new MagickImageCollection();
        images.Read(bytes, settings);
        if (images.Count == 0)
        {
            throw new InvalidDataException(L.Get("Error.NoFrame"));
        }

        var frame = extension.Equals(".ico", StringComparison.OrdinalIgnoreCase)
            ? images.OrderByDescending(image => (long)image.Width * image.Height).First()
            : images[0];
        frame.AutoOrient();
        var width = checked((int)frame.Width);
        var height = checked((int)frame.Height);
        var hasAlpha = frame.HasAlpha;
        var pngBytes = frame.ToByteArray(MagickFormat.Png32);
        return new DecodedBitmap(DecodePngBitmap(pngBytes), width, height, hasAlpha);
    }

    private static BitmapSource DecodePngBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var decoder = new PngBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
        {
            throw new InvalidDataException(L.Get("Error.NoDecodedFrame"));
        }

        var bitmap = decoder.Frames[0];
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource ApplyEffects(
        BitmapSource source,
        bool grayscale,
        double contrast,
        CancellationToken cancellationToken)
    {
        var normalizedContrast = Math.Clamp(contrast, -100, 100);
        if (!grayscale && Math.Abs(normalizedContrast) < 0.5)
        {
            return source;
        }

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
        var factor = normalizedContrast >= 0
            ? 1.0 + normalizedContrast / 50.0
            : 1.0 + normalizedContrast / 100.0;

        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                var index = y * stride + x * 4;
                var blue = pixels[index];
                var green = pixels[index + 1];
                var red = pixels[index + 2];
                if (grayscale)
                {
                    var gray = ClampByte(red * 0.2126 + green * 0.7152 + blue * 0.0722);
                    red = green = blue = gray;
                }

                pixels[index] = ApplyContrast(blue, factor);
                pixels[index + 1] = ApplyContrast(green, factor);
                pixels[index + 2] = ApplyContrast(red, factor);
            }
        }

        var result = BitmapSource.Create(
            width,
            height,
            converted.DpiX,
            converted.DpiY,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        result.Freeze();
        return result;
    }

    private static byte ApplyContrast(byte value, double factor) =>
        ClampByte((value - 127.5) * factor + 127.5);

    private static MagickFormat GetMagickFormat(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => MagickFormat.Png,
        ".jpg" or ".jpeg" => MagickFormat.Jpeg,
        ".webp" => MagickFormat.WebP,
        ".bmp" => MagickFormat.Bmp,
        ".tiff" or ".tif" => MagickFormat.Tiff,
        ".svg" => MagickFormat.Svg,
        ".ico" => MagickFormat.Ico,
        ".avif" => MagickFormat.Avif,
        ".gif" => MagickFormat.Gif,
        _ => MagickFormat.Unknown
    };

    private static byte ClampByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    private static string NormalizeExtension(string sourceFormat, string filename)
    {
        var extension = Path.GetExtension(filename);
        if (SupportedExtensionSet.Contains(extension))
        {
            return extension.ToLowerInvariant();
        }

        var normalized = sourceFormat.Trim().TrimStart('.').ToLowerInvariant();
        return normalized switch
        {
            "jpeg" => ".jpg",
            "tif" => ".tif",
            _ => "." + normalized
        };
    }

    private sealed record DecodedBitmap(BitmapSource Bitmap, int Width, int Height, bool HasAlpha);
}
