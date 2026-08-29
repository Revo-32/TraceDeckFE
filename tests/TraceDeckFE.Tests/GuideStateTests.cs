using TraceDeckFE.Models;

namespace TraceDeckFE.Tests;

public sealed class GuideStateTests
{
    [Fact]
    public void HasVisibleGuide_TracksIndependentGridAndCenterSwitches()
    {
        var guides = new GuideState();
        Assert.False(guides.HasVisibleGuide);

        guides.IsHorizontalCenterVisible = true;
        Assert.True(guides.HasVisibleGuide);

        guides.IsHorizontalCenterVisible = false;
        guides.IsVerticalCenterVisible = true;
        Assert.True(guides.HasVisibleGuide);

        guides.IsVerticalCenterVisible = false;
        guides.IsGridVisible = true;
        Assert.True(guides.HasVisibleGuide);
    }

    [Theory]
    [InlineData(-1, 16)]
    [InlineData(32, 32)]
    [InlineData(1000, 400)]
    public void GridSpacing_UsesSafeBounds(double input, double expected)
    {
        var guides = new GuideState { GridSpacing = input };

        Assert.Equal(expected, guides.GridSpacing);
    }
}
