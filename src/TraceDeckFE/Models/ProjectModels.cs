using TraceDeckFE.Localization;
namespace TraceDeckFE.Models;

public sealed class ProjectSession : ObservableObject
{
    private Guid _projectId = Guid.NewGuid();
    private DateTimeOffset _createdUtc = DateTimeOffset.UtcNow;
    private DateTimeOffset _modifiedUtc = DateTimeOffset.UtcNow;
    private string? _path;
    private bool _isDirty;
    private long _revision;

    public Guid ProjectId => _projectId;
    public DateTimeOffset CreatedUtc => _createdUtc;
    public DateTimeOffset ModifiedUtc => _modifiedUtc;
    public string? Path => _path;
    public bool IsDirty => _isDirty;
    public long Revision => _revision;
    public string ProjectName => string.IsNullOrWhiteSpace(Path) ? L.Get("Status.Untitled") : System.IO.Path.GetFileNameWithoutExtension(Path);
    public string DisplayName => ProjectName + (IsDirty ? " *" : string.Empty);

    public void MarkDirty()
    {
        _revision++;
        if (!_isDirty)
        {
            _isDirty = true;
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public void SetEditDirty(bool dirty)
    {
        _revision++;
        _isDirty = dirty;
        RaiseAll();
    }

    public void ResetNew()
    {
        _projectId = Guid.NewGuid();
        _createdUtc = DateTimeOffset.UtcNow;
        _modifiedUtc = _createdUtc;
        _path = null;
        _isDirty = false;
        _revision++;
        RaiseAll();
    }

    public void AdoptLoaded(ProjectManifest manifest, string? path)
    {
        _projectId = manifest.ProjectId;
        _createdUtc = manifest.CreatedUtc;
        _modifiedUtc = manifest.ModifiedUtc;
        _path = path;
        _isDirty = false;
        _revision++;
        RaiseAll();
    }

    public void MarkSaved(string path, DateTimeOffset modifiedUtc, long savedRevision)
    {
        _path = path;
        _modifiedUtc = modifiedUtc;
        if (_revision == savedRevision)
        {
            _isDirty = false;
        }

        RaiseAll();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(ProjectId));
        OnPropertyChanged(nameof(CreatedUtc));
        OnPropertyChanged(nameof(ModifiedUtc));
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(DisplayName));
    }
}

public sealed class ProjectUiState : ObservableObject
{
    private double _controllerWidth = 370;
    private bool _projectExpanded = true;
    private bool _overlayExpanded = true;
    private bool _transformExpanded = true;
    private bool _positionExpanded = true;
    private bool _imageAssistExpanded = true;
    private bool _guidesExpanded;
    private bool _colorExpanded = true;
    private bool _paletteExpanded = true;
    private bool _advancedExpanded;

    public event EventHandler? ContentChanged;

    public double ControllerWidth { get => _controllerWidth; set => Set(ref _controllerWidth, LayoutPolicy.ClampWidth(value)); }
    public bool ProjectExpanded { get => _projectExpanded; set => Set(ref _projectExpanded, value); }
    public bool OverlayExpanded { get => _overlayExpanded; set => Set(ref _overlayExpanded, value); }
    public bool TransformExpanded { get => _transformExpanded; set => Set(ref _transformExpanded, value); }
    public bool PositionExpanded { get => _positionExpanded; set => Set(ref _positionExpanded, value); }
    public bool ImageAssistExpanded { get => _imageAssistExpanded; set => Set(ref _imageAssistExpanded, value); }
    public bool GuidesExpanded { get => _guidesExpanded; set => Set(ref _guidesExpanded, value); }
    public bool ColorExpanded { get => _colorExpanded; set => Set(ref _colorExpanded, value); }
    public bool PaletteExpanded { get => _paletteExpanded; set => Set(ref _paletteExpanded, value); }
    public bool AdvancedExpanded { get => _advancedExpanded; set => Set(ref _advancedExpanded, value); }

    public void Restore(ProjectUiStateData? state)
    {
        state ??= new ProjectUiStateData();
        _controllerWidth = LayoutPolicy.ClampWidth(state.ControllerWidth);
        _projectExpanded = state.ProjectExpanded;
        _overlayExpanded = state.OverlayExpanded;
        _transformExpanded = state.TransformExpanded;
        _positionExpanded = state.PositionExpanded;
        _imageAssistExpanded = state.ImageAssistExpanded;
        _guidesExpanded = state.GuidesExpanded;
        _colorExpanded = state.ColorExpanded;
        _paletteExpanded = state.PaletteExpanded;
        _advancedExpanded = state.AdvancedExpanded;
        foreach (var property in new[] { nameof(ControllerWidth), nameof(ProjectExpanded), nameof(OverlayExpanded), nameof(TransformExpanded), nameof(PositionExpanded), nameof(ImageAssistExpanded), nameof(GuidesExpanded), nameof(ColorExpanded), nameof(PaletteExpanded), nameof(AdvancedExpanded) })
        {
            OnPropertyChanged(property);
        }
    }

    public ProjectUiStateData Capture() => new()
    {
        ControllerWidth = ControllerWidth,
        ProjectExpanded = ProjectExpanded,
        OverlayExpanded = OverlayExpanded,
        TransformExpanded = TransformExpanded,
        PositionExpanded = PositionExpanded,
        ImageAssistExpanded = ImageAssistExpanded,
        GuidesExpanded = GuidesExpanded,
        ColorExpanded = ColorExpanded,
        PaletteExpanded = PaletteExpanded,
        AdvancedExpanded = AdvancedExpanded
    };

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (SetProperty(ref field, value, name))
        {
            ContentChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed record ProjectManifest
{
    public string Format { get; init; } = "TraceDeckFE";
    public int FormatVersion { get; init; } = 1;
    public string AppVersion { get; init; } = "1.0.0";
    public Guid ProjectId { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset ModifiedUtc { get; init; }
    public string? ReferenceEntry { get; init; }
    public string? ReferenceSha256 { get; init; }
}

public sealed record ProjectStateData
{
    public ReferenceProjectState? Reference { get; init; }
    public OverlayProjectState Overlay { get; init; } = new();
    public GuideProjectState Guides { get; init; } = new();
    public ColorProjectState Color { get; init; } = new();
    public List<PaletteItemData> Palette { get; init; } = [];
    public int AutoPaletteColorCount { get; init; } = 6;
    public ProjectUiStateData Ui { get; init; } = new();
}

public sealed record ReferenceProjectState
{
    public string OriginalFilename { get; init; } = "Reference";
    public string SourceFormat { get; init; } = string.Empty;
    public ReferenceSourceKind SourceKind { get; init; }
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }
    public bool IsVector { get; init; }
    public double NormalizedCenterX { get; init; } = 0.5;
    public double NormalizedCenterY { get; init; } = 0.5;
    public double NormalizedVisualWidth { get; init; } = 0.5;
    public double UserScale { get; init; } = 1;
    public double SavedViewportWidth { get; init; } = 1280;
    public double SavedViewportHeight { get; init; } = 720;
    public double Rotation { get; init; }
    public bool FlipHorizontal { get; init; }
    public bool FlipVertical { get; init; }
    public bool Grayscale { get; init; }
    public double Contrast { get; init; }
}

public sealed record OverlayProjectState
{
    public bool Visible { get; init; } = true;
    public bool Locked { get; init; } = true;
    public double Opacity { get; init; } = 0.62;
}

public sealed record GuideProjectState
{
    public bool GridEnabled { get; init; }
    public double GridSpacing { get; init; } = 100;
    public double Opacity { get; init; } = 0.28;
    public bool HorizontalCenterGuide { get; init; }
    public bool VerticalCenterGuide { get; init; }
}

public sealed record ColorProjectState
{
    public RgbaColor? Current { get; init; }
    public bool MagnifierEnabled { get; init; } = true;
}

public sealed record PaletteItemData
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "Untitled Color";
    public RgbaColor Color { get; init; }
    public bool IsGenerated { get; init; }
}

public sealed record ProjectUiStateData
{
    public double ControllerWidth { get; init; } = 370;
    public bool ProjectExpanded { get; init; } = true;
    public bool OverlayExpanded { get; init; } = true;
    public bool TransformExpanded { get; init; } = true;
    public bool PositionExpanded { get; init; } = true;
    public bool ImageAssistExpanded { get; init; } = true;
    public bool GuidesExpanded { get; init; }
    public bool ColorExpanded { get; init; } = true;
    public bool PaletteExpanded { get; init; } = true;
    public bool AdvancedExpanded { get; init; }
}

public sealed record TdfProjectPackage(ProjectManifest Manifest, ProjectStateData State, byte[]? ReferenceBytes);
