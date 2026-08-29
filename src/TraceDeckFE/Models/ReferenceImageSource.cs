using System.Windows.Media.Imaging;

namespace TraceDeckFE.Models;

public sealed record ReferenceImageSource(
    Guid Id,
    string Name,
    string? SourcePath,
    string Format,
    int PixelWidth,
    int PixelHeight,
    bool HasAlpha,
    bool IsVector,
    byte[] OriginalBytes,
    BitmapSource OriginalBitmap,
    ReferenceSourceKind SourceKind = ReferenceSourceKind.File);

public enum ReferenceSourceKind
{
    File,
    Clipboard,
    Project
}

public sealed class ReferenceInputSettings : ObservableObject
{
    private bool _confirmReplacement = true;

    public bool ConfirmReplacement
    {
        get => _confirmReplacement;
        set => SetProperty(ref _confirmReplacement, value);
    }
}
