using System.IO.Compression;
using System.IO;
using System.Text;
using System.Text.Json;
using TraceDeckFE.Models;
using TraceDeckFE.Services;

namespace TraceDeckFE.Tests;

public sealed class ProjectArchiveServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [Fact]
    public async Task ProjectRoundTrip_PreservesFullStateAndDoublePrecision()
    {
        var path = TempPath();
        var package = CreatePackage([1, 2, 3, 4, 5, 6]);
        try
        {
            var service = new ProjectArchiveService();
            await service.SaveAsync(path, package);
            var loaded = await service.LoadAsync(path);

            Assert.Equal(package.Manifest, loaded.Manifest);
            Assert.Equal(package.State.Reference!.NormalizedCenterX, loaded.State.Reference!.NormalizedCenterX);
            Assert.Equal(package.State.Reference.NormalizedCenterY, loaded.State.Reference.NormalizedCenterY);
            Assert.Equal(package.State.Reference.NormalizedVisualWidth, loaded.State.Reference.NormalizedVisualWidth);
            Assert.Equal(package.State.Reference.Rotation, loaded.State.Reference.Rotation);
            Assert.Equal(package.State.Overlay, loaded.State.Overlay);
            Assert.Equal(package.State.Guides, loaded.State.Guides);
            Assert.Equal(package.State.Color, loaded.State.Color);
            Assert.Equal(package.State.Ui, loaded.State.Ui);
            Assert.Equal(package.State.Palette, loaded.State.Palette);
            Assert.Equal(package.ReferenceBytes, loaded.ReferenceBytes);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("logo.png", "PNG")]
    [InlineData("logo.svg", "SVG")]
    [InlineData("photo.jpg", "JPEG")]
    [InlineData("reference.webp", "WEBP")]
    public async Task OriginalSourceRoundTrip_PreservesExactBytes(string filename, string format)
    {
        var path = TempPath();
        var bytes = Enumerable.Range(0, 257).Select(index => (byte)(index * 37)).ToArray();
        var package = CreatePackage(bytes, filename, format);
        try
        {
            var service = new ProjectArchiveService();
            await service.SaveAsync(path, package);

            var loaded = await service.LoadAsync(path);

            Assert.Equal(bytes, loaded.ReferenceBytes);
            Assert.Equal(ProjectArchiveService.ComputeSha256(bytes), loaded.Manifest.ReferenceSha256);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task AtomicSave_InvalidReplacementLeavesExistingProjectUnchanged()
    {
        var path = TempPath();
        var service = new ProjectArchiveService();
        try
        {
            await service.SaveAsync(path, CreatePackage([10, 20, 30]));
            var before = await File.ReadAllBytesAsync(path);
            var invalid = CreatePackage([90, 80, 70]);
            invalid = invalid with { Manifest = invalid.Manifest with { ReferenceSha256 = "wrong" } };

            await Assert.ThrowsAsync<ProjectArchiveException>(() => service.SaveAsync(path, invalid));

            Assert.Equal(before, await File.ReadAllBytesAsync(path));
            Assert.Equal(new byte[] { 10, 20, 30 }, (await service.LoadAsync(path)).ReferenceBytes);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Load_InvalidArchiveFailsWithoutMutatingCallerState()
    {
        var path = TempPath();
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("not a zip"));
        var currentProjectMarker = Guid.NewGuid();
        try
        {
            var service = new ProjectArchiveService();

            await Assert.ThrowsAsync<ProjectArchiveException>(() => service.LoadAsync(path));

            Assert.NotEqual(Guid.Empty, currentProjectMarker);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Load_MissingManifestIsRejected()
    {
        var path = TempPath();
        WriteRawArchive(path, null, new ProjectStateData(), null, null);
        try
        {
            var error = await Assert.ThrowsAsync<ProjectArchiveException>(() => new ProjectArchiveService().LoadAsync(path));
            Assert.Contains("manifest", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Load_BadFormatIsRejected()
    {
        var path = TempPath();
        var manifest = BaseManifest() with { Format = "DifferentApp" };
        WriteRawArchive(path, manifest, new ProjectStateData(), null, null);
        try
        {
            await Assert.ThrowsAsync<ProjectArchiveException>(() => new ProjectArchiveService().LoadAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Load_FutureVersionHasSpecificSafeFailure()
    {
        var path = TempPath();
        var manifest = BaseManifest() with { FormatVersion = ProjectArchiveService.CurrentFormatVersion + 1 };
        WriteRawArchive(path, manifest, new ProjectStateData(), null, null);
        try
        {
            var error = await Assert.ThrowsAsync<ProjectArchiveException>(() => new ProjectArchiveService().LoadAsync(path));
            Assert.Contains("newer version", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Load_CorruptedReferenceHashIsRejected()
    {
        var path = TempPath();
        var package = CreatePackage([1, 3, 5, 7]);
        WriteRawArchive(path, package.Manifest with { ReferenceSha256 = new string('0', 64) }, package.State,
            package.Manifest.ReferenceEntry, package.ReferenceBytes);
        try
        {
            var error = await Assert.ThrowsAsync<ProjectArchiveException>(() => new ProjectArchiveService().LoadAsync(path));
            Assert.Contains("integrity", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Load_InvalidJsonIsRejected()
    {
        var path = TempPath();
        using (var stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "manifest.json", "{ broken");
            WriteEntry(archive, "project/state.json", "{}");
        }
        try
        {
            await Assert.ThrowsAsync<ProjectArchiveException>(() => new ProjectArchiveService().LoadAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Load_UnsafeArchiveEntryIsRejectedWithoutExtraction()
    {
        var path = TempPath();
        using (var stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "manifest.json", JsonSerializer.Serialize(BaseManifest(), JsonOptions));
            WriteEntry(archive, "project/state.json", JsonSerializer.Serialize(new ProjectStateData(), JsonOptions));
            WriteEntry(archive, "../outside.txt", "unsafe");
        }
        try
        {
            var error = await Assert.ThrowsAsync<ProjectArchiveException>(() => new ProjectArchiveService().LoadAsync(path));
            Assert.Contains("unsafe", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void EnsureExtension_AddsTdfeOnlyWhenNeeded()
    {
        Assert.EndsWith(".TDFE", ProjectArchiveService.EnsureExtension("Logo"), StringComparison.Ordinal);
        Assert.Equal("Logo.tdfe", ProjectArchiveService.EnsureExtension("Logo.tdfe"));
    }

    private static TdfProjectPackage CreatePackage(byte[] bytes, string filename = "reference.png", string format = "PNG")
    {
        var manifest = BaseManifest() with
        {
            ReferenceEntry = ProjectArchiveService.CreateReferenceEntry(filename, format),
            ReferenceSha256 = ProjectArchiveService.ComputeSha256(bytes)
        };
        var state = new ProjectStateData
        {
            Reference = new ReferenceProjectState
            {
                OriginalFilename = filename,
                SourceFormat = format,
                SourceKind = ReferenceSourceKind.File,
                PixelWidth = 640,
                PixelHeight = 360,
                NormalizedCenterX = 0.512345678901234,
                NormalizedCenterY = 0.487654321098765,
                NormalizedVisualWidth = 0.412345678901234,
                UserScale = 1.23456789012345,
                SavedViewportWidth = 1920,
                SavedViewportHeight = 1080,
                Rotation = 73.1234567890123,
                FlipHorizontal = true,
                FlipVertical = false,
                Grayscale = true,
                Contrast = 27
            },
            Overlay = new OverlayProjectState { Visible = true, Locked = false, Opacity = 0.456789012345678 },
            Guides = new GuideProjectState { GridEnabled = true, GridSpacing = 84, Opacity = 0.37, HorizontalCenterGuide = true, VerticalCenterGuide = false },
            Color = new ColorProjectState { Current = new RgbaColor(231, 44, 49, 180), MagnifierEnabled = false },
            Palette =
            [
                new PaletteItemData { Id = Guid.NewGuid(), Name = "Main Red", Color = new RgbaColor(231, 44, 49) },
                new PaletteItemData { Id = Guid.NewGuid(), Name = "Glass", Color = new RgbaColor(20, 50, 80, 100), IsGenerated = true }
            ],
            AutoPaletteColorCount = 9,
            Ui = new ProjectUiStateData { ControllerWidth = 444, ColorExpanded = true, PaletteExpanded = false, GuidesExpanded = true }
        };
        return new TdfProjectPackage(manifest, state, bytes);
    }

    private static ProjectManifest BaseManifest()
    {
        var created = new DateTimeOffset(2026, 8, 29, 1, 2, 3, TimeSpan.Zero);
        return new ProjectManifest
        {
            ProjectId = Guid.NewGuid(),
            AppVersion = "1.0.0",
            CreatedUtc = created,
            ModifiedUtc = created.AddMinutes(5)
        };
    }

    private static void WriteRawArchive(
        string path,
        ProjectManifest? manifest,
        ProjectStateData state,
        string? referenceEntry,
        byte[]? referenceBytes)
    {
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        if (manifest is not null) WriteEntry(archive, "manifest.json", JsonSerializer.Serialize(manifest, JsonOptions));
        WriteEntry(archive, "project/state.json", JsonSerializer.Serialize(state, JsonOptions));
        if (referenceEntry is not null && referenceBytes is not null)
        {
            var entry = archive.CreateEntry(referenceEntry);
            using var output = entry.Open();
            output.Write(referenceBytes);
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"tracedeck-{Guid.NewGuid():N}.TDFE");
}
