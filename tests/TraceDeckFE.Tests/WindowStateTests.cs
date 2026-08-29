using TraceDeckFE.Models;

namespace TraceDeckFE.Tests;

public sealed class WindowStateTests
{
    [Fact]
    public void ClientAreaCalculator_OffsetsClientRectIntoScreenCoordinates()
    {
        var result = ClientAreaCalculator.ToScreenRect(
            new ClientRect(0, 0, 2560, 1440),
            new IntPoint(108, 64));

        Assert.Equal(new IntRect(108, 64, 2560, 1440), result);
    }

    [Fact]
    public void VisibilityPolicy_IsIndependentFromForegroundAndRequiresUsableReference()
    {
        var target = ConnectedTarget();

        Assert.True(OverlayVisibilityPolicy.ShouldShow(target, referenceVisible: true, hasImage: true));
        Assert.False(OverlayVisibilityPolicy.ShouldShow(target with { IsMinimized = true }, true, true));
        Assert.False(OverlayVisibilityPolicy.ShouldShow(target, referenceVisible: false, hasImage: true));
        Assert.False(OverlayVisibilityPolicy.ShouldShow(target, referenceVisible: true, hasImage: false));
    }

    [Fact]
    public void StackingPolicy_PutsOverlayAboveTargetWhenTargetIsForeground()
    {
        Assert.Equal(
            OverlayStackingMode.AboveTarget,
            OverlayStackingPolicy.Decide(targetHandle: 42, foregroundHandle: 42));
    }

    [Fact]
    public void StackingPolicy_PutsOverlayBehindAnotherForegroundApplication()
    {
        Assert.Equal(
            OverlayStackingMode.BehindForeground,
            OverlayStackingPolicy.Decide(targetHandle: 42, foregroundHandle: 84));
    }

    [Fact]
    public void VisibilityPolicy_HidesDisconnectedAndZeroSizedTargets()
    {
        Assert.False(OverlayVisibilityPolicy.ShouldShow(
            TargetWindowSnapshot.Disconnected,
            referenceVisible: true,
            hasImage: true));

        Assert.False(OverlayVisibilityPolicy.ShouldShow(
            ConnectedTarget() with { ClientBounds = new IntRect(10, 20, 0, 0) },
            referenceVisible: true,
            hasImage: true));
    }

    [Fact]
    public void ViewportPolicy_IgnoresMinimizedIconBounds()
    {
        var minimized = ConnectedTarget() with
        {
            ClientBounds = new IntRect(-32000, -32000, 148, 22),
            IsMinimized = true
        };

        Assert.False(ReferenceViewportPolicy.ShouldUpdate(minimized));
        Assert.True(ReferenceViewportPolicy.ShouldUpdate(ConnectedTarget()));
    }

    private static TargetWindowSnapshot ConnectedTarget() => new(
        Handle: 42,
        Title: "Forza Horizon 6",
        ProcessName: "ForzaHorizon6",
        ClientBounds: new IntRect(100, 100, 1920, 1080),
        Exists: true,
        IsVisible: true,
        IsMinimized: false);
}
