namespace TraceDeckFE.Models;

public sealed record WindowInfo(
    nint Handle,
    int ProcessId,
    string ProcessName,
    string Title,
    IntRect ClientBounds)
{
    public string DisplayLabel => $"{Title}  —  {ProcessName} ({ClientBounds.Width} × {ClientBounds.Height})";
}

public sealed record TargetWindowSnapshot(
    nint Handle,
    string Title,
    string ProcessName,
    IntRect ClientBounds,
    bool Exists,
    bool IsVisible,
    bool IsMinimized)
{
    public static TargetWindowSnapshot Disconnected { get; } = new(
        0, string.Empty, string.Empty, new IntRect(0, 0, 0, 0), false, false, false);
}

public static class OverlayVisibilityPolicy
{
    public static bool ShouldShow(TargetWindowSnapshot target, bool referenceVisible, bool hasImage) =>
        target.Exists &&
        target.IsVisible &&
        !target.IsMinimized &&
        !target.ClientBounds.IsEmpty &&
        referenceVisible &&
        hasImage;
}

public static class ReferenceViewportPolicy
{
    public static bool ShouldUpdate(TargetWindowSnapshot target) =>
        target.Exists &&
        target.IsVisible &&
        !target.IsMinimized &&
        !target.ClientBounds.IsEmpty;
}

public enum OverlayStackingMode
{
    AboveTarget,
    BehindForeground
}

public static class OverlayStackingPolicy
{
    public static OverlayStackingMode Decide(nint targetHandle, nint foregroundHandle) =>
        foregroundHandle == 0 || foregroundHandle == targetHandle
            ? OverlayStackingMode.AboveTarget
            : OverlayStackingMode.BehindForeground;
}
