using System.Windows.Media;
using System.Windows.Media.Imaging;
using TraceDeckFE.Models;
using TraceDeckFE.Services;

namespace TraceDeckFE.Tests;

public sealed class PaletteTests
{
    [Fact]
    public void Palette_AddRenameDeleteAndReorderUseStableIds()
    {
        var palette = new PaletteState();
        var first = palette.Add(new RgbaColor(200, 10, 20), "Main Red");
        var second = palette.Add(new RgbaColor(10, 20, 200), "Blue");
        var firstId = first.Id;

        first.Name = "Outline Red";
        Assert.True(palette.Move(second, 0));
        Assert.True(palette.Delete(first));

        Assert.Equal("Outline Red", first.Name);
        Assert.Equal(firstId, first.Id);
        Assert.Single(palette.Items);
        Assert.Same(second, palette.Items[0]);
    }

    [Fact]
    public void Palette_ManualDuplicateColorsArePreserved()
    {
        var palette = new PaletteState();
        var color = new RgbaColor(231, 44, 49);

        palette.Add(color, "Main Red");
        palette.Add(color, "Outline Red");

        Assert.Equal(2, palette.Items.Count);
        Assert.Equal(color, palette.Items[0].Color);
        Assert.Equal(color, palette.Items[1].Color);
        Assert.NotEqual(palette.Items[0].Id, palette.Items[1].Id);
    }

    [Fact]
    public void PaletteSelection_LoadsExactSourceRgbaIntoColorState()
    {
        var colorState = new ColorState();
        var item = new PaletteItem(Guid.NewGuid(), "Glass", new RgbaColor(20, 40, 60, 90));

        colorState.SetColor(item.Color);

        Assert.Equal(item.Color, colorState.Current);
        Assert.Equal("#14283C", colorState.Hex);
        Assert.Equal("90", colorState.Alpha);
    }

    [Fact]
    public async Task AutoPalette_ReturnsRequestedPrevalentColorsAndIgnoresTransparentPixels()
    {
        const int width = 80;
        const int height = 20;
        var pixels = new byte[width * height * 4];
        var colors = new[]
        {
            new RgbaColor(240, 20, 20),
            new RgbaColor(20, 230, 30),
            new RgbaColor(20, 30, 220),
            new RgbaColor(255, 0, 255, 0)
        };
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var color = colors[Math.Min(3, x / 20)];
            var index = (y * width + x) * 4;
            pixels[index] = color.Blue;
            pixels[index + 1] = color.Green;
            pixels[index + 2] = color.Red;
            pixels[index + 3] = color.Alpha;
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        var source = new ReferenceImageSource(Guid.NewGuid(), "palette.png", null, "PNG", width, height, true, false, [1], bitmap);
        var imageService = new ReferenceImageService(new NullLogger());
        var service = new AutoPaletteService(new ReferenceColorService(imageService));

        var generated = await service.GenerateAsync(source, 3);

        Assert.Equal(3, generated.Count);
        Assert.DoesNotContain(generated, color => color.Alpha == 0 || color.Red > 245 && color.Blue > 245);
    }

    [Fact]
    public async Task AutoPaletteAppend_DoesNotDeleteExistingManualPalette()
    {
        var palette = new PaletteState();
        var manual = palette.Add(new RgbaColor(1, 2, 3), "Manual");
        var bitmap = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 0, 0, 255, 255, 255, 0, 0, 255 }, 8);
        bitmap.Freeze();
        var source = new ReferenceImageSource(Guid.NewGuid(), "two.png", null, "PNG", 2, 1, false, false, [1], bitmap);
        var service = new AutoPaletteService(new ReferenceColorService(new ReferenceImageService(new NullLogger())));

        foreach (var color in await service.GenerateAsync(source, 2)) palette.Add(color, isGenerated: true);

        Assert.Contains(manual, palette.Items);
        Assert.Equal("Manual", palette.Items[0].Name);
        Assert.True(palette.Items.Count >= 2);
    }

    [Fact]
    public void PaletteReplaceAll_RestoresSerializationOrderAndMetadata()
    {
        var items = new[]
        {
            new PaletteItem(Guid.NewGuid(), "One", new RgbaColor(1, 2, 3, 4)),
            new PaletteItem(Guid.NewGuid(), "Two", new RgbaColor(5, 6, 7, 8), true)
        };
        var palette = new PaletteState();

        palette.ReplaceAll(items, autoColorCount: 11);

        Assert.Equal(items, palette.Items);
        Assert.Equal(11, palette.AutoColorCount);
        Assert.True(palette.Items[1].IsGenerated);
    }

    private sealed class NullLogger : ITraceLogger
    {
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
