using System.Text.Json;

namespace TraceDeckFE.Models;

public sealed class ProjectEditSnapshot
{
    private ProjectEditSnapshot(ReferenceImageSource? source, ProjectStateData state)
    {
        Source = source;
        State = state;
        // Canonical geometry is client-relative. Display pixel scale/viewport and app UI are not edits.
        var canonical = state with
        {
            Reference = state.Reference is { } r ? r with { UserScale = 1, SavedViewportWidth = 1280, SavedViewportHeight = 720 } : null,
            Ui = new(), Color = state.Color with { MagnifierEnabled = true }
        };
        Fingerprint = JsonSerializer.Serialize(canonical);
    }
    public ReferenceImageSource? Source { get; }
    public ProjectStateData State { get; }
    public string Fingerprint { get; }
    public static bool Equivalent(ProjectEditSnapshot left, ProjectEditSnapshot right) =>
        left.Source?.Id == right.Source?.Id && left.Fingerprint == right.Fingerprint;

    public static ProjectEditSnapshot Capture(ReferenceState reference, GuideState guides, ColorState colors, PaletteState palette)
    {
        var source = reference.Source;
        var normalized = reference.NormalizedTransform;
        ReferenceProjectState? state = source is null ? null : new()
        {
            OriginalFilename = source.Name, SourceFormat = source.Format, SourceKind = source.SourceKind,
            PixelWidth = source.PixelWidth, PixelHeight = source.PixelHeight, IsVector = source.IsVector,
            NormalizedCenterX = normalized?.CenterXRatio ?? .5, NormalizedCenterY = normalized?.CenterYRatio ?? .5,
            NormalizedVisualWidth = normalized?.VisualWidthRatio ?? .5,
            UserScale = reference.Scale, SavedViewportWidth = reference.ViewportWidth, SavedViewportHeight = reference.ViewportHeight,
            Rotation = reference.Rotation, FlipHorizontal = reference.FlipHorizontal, FlipVertical = reference.FlipVertical,
            Grayscale = reference.IsGrayscale, Contrast = reference.Contrast
        };
        return new(source, new()
        {
            Reference = state,
            Overlay = new() { Visible = reference.IsVisible, Locked = reference.IsLocked, Opacity = reference.Opacity },
            Guides = new() { GridEnabled = guides.IsGridVisible, GridSpacing = guides.GridSpacing, Opacity = guides.Opacity,
                HorizontalCenterGuide = guides.IsHorizontalCenterVisible, VerticalCenterGuide = guides.IsVerticalCenterVisible },
            Color = new() { Current = colors.Current, MagnifierEnabled = colors.MagnifierEnabled },
            Palette = palette.Items.Select(i => new PaletteItemData { Id = i.Id, Name = i.Name, Color = i.Color, IsGenerated = i.IsGenerated }).ToList(),
            AutoPaletteColorCount = palette.AutoColorCount
        });
    }

    public void Apply(ReferenceState reference, GuideState guides, ColorState colors, PaletteState palette, double width, double height)
    {
        if (Source is not null && State.Reference is { } state) reference.RestoreProject(Source, state, State.Overlay, width, height);
        else
        {
            reference.Clear(); reference.IsVisible = State.Overlay.Visible;
            reference.IsLocked = State.Overlay.Locked; reference.Opacity = State.Overlay.Opacity;
        }
        guides.Restore(State.Guides);
        colors.Restore(State.Color.Current, colors.MagnifierEnabled);
        palette.ReplaceAll(State.Palette.Select(i => new PaletteItem(i.Id, i.Name, i.Color, i.IsGenerated)), State.AutoPaletteColorCount);
    }
}
