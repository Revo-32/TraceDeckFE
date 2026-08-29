using TraceDeckFE.Models;

namespace TraceDeckFE.Tests;

public sealed class ColorConversionTests
{
    [Theory]
    [InlineData(255, 0, 0, 0, 1, 1)]
    [InlineData(0, 255, 0, 0.3333333333333333, 1, 1)]
    [InlineData(0, 0, 255, 0.6666666666666666, 1, 1)]
    [InlineData(255, 255, 255, 0, 0, 1)]
    [InlineData(0, 0, 0, 0, 0, 0)]
    [InlineData(128, 128, 128, 0, 0, 0.5019607843137255)]
    public void FromRgb_KnownValues(int red, int green, int blue, double hue, double saturation, double brightness)
    {
        var result = ForzaColorConverter.FromRgb(new RgbaColor((byte)red, (byte)green, (byte)blue));

        Assert.Equal(hue, result.Hue, precision: 12);
        Assert.Equal(saturation, result.Saturation, precision: 12);
        Assert.Equal(brightness, result.Brightness, precision: 12);
    }

    [Fact]
    public void FromRgb_ArbitraryColorUsesStandardNormalizedHsv()
    {
        var result = ForzaColorConverter.FromRgb(new RgbaColor(231, 44, 49));

        Assert.Equal(0.9955436720142602, result.Hue, precision: 12);
        Assert.Equal(0.8095238095238095, result.Saturation, precision: 12);
        Assert.Equal(0.9058823529411765, result.Brightness, precision: 12);
    }

    [Fact]
    public void InternalPrecision_IsNotRoundedToDisplayPrecision()
    {
        var result = ForzaColorConverter.FromRgb(new RgbaColor(1, 2, 3));

        Assert.Equal(3.0 / 255.0, result.Brightness, precision: 14);
        Assert.Equal("0.012", ColorState.FormatComponent(result.Brightness));
        Assert.NotEqual(0.012, result.Brightness);
    }

    [Fact]
    public void Alpha_DoesNotAffectHsbConversion()
    {
        var opaque = ForzaColorConverter.FromRgb(new RgbaColor(20, 100, 220, 255));
        var transparent = ForzaColorConverter.FromRgb(new RgbaColor(20, 100, 220, 0));

        Assert.Equal(opaque, transparent);
    }
}
