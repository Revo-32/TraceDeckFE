using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Xml.Linq;

namespace TraceDeckFE.Tests;

public sealed class ReleasePolishTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void ProjectMetadataAndReleaseResourcesAreExplicit()
    {
        var projectPath = Path.Combine(Root, "src", "TraceDeckFE", "TraceDeckFE.csproj");
        var project = XDocument.Load(projectPath);

        Assert.Equal("1.0.0", Property(project, "Version"));
        Assert.Equal("1.0.0.0", Property(project, "AssemblyVersion"));
        Assert.Equal("1.0.0.0", Property(project, "FileVersion"));
        Assert.Equal("1.0.0", Property(project, "InformationalVersion"));
        Assert.Equal("TraceDeck FE", Property(project, "AssemblyTitle"));
        Assert.Equal("TraceDeck FE", Property(project, "Product"));
        Assert.Equal("TraceDeck FE", Property(project, "Description"));
        Assert.Equal("false", Property(project, "GenerateAssemblyCompanyAttribute"));
        Assert.Equal(@"Assets\TraceDeckFE.ico", Property(project, "ApplicationIcon"));

        var resourceIncludes = project.Descendants("Resource")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(value => value is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(@"Assets\TraceDeck_FE_Mini_logo.png", resourceIncludes);
        Assert.Contains(@"Assets\TraceDeckFE.ico", resourceIncludes);
        Assert.Equal(3, resourceIncludes.Count(value => value!.StartsWith(@"Assets\Fonts\Pretendard-", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void ApplicationIconContainsAllRequiredThirtyTwoBitFrames()
    {
        var iconPath = Path.Combine(Root, "src", "TraceDeckFE", "Assets", "TraceDeckFE.ico");
        using var stream = File.OpenRead(iconPath);
        using var reader = new BinaryReader(stream);

        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        var count = reader.ReadUInt16();
        Assert.Equal(9, count);

        var dimensions = new List<int>();
        for (var index = 0; index < count; index++)
        {
            var widthByte = reader.ReadByte();
            var heightByte = reader.ReadByte();
            reader.ReadByte();
            reader.ReadByte();
            reader.ReadUInt16();
            var bitDepth = reader.ReadUInt16();
            var byteCount = reader.ReadUInt32();
            var offset = reader.ReadUInt32();
            var width = widthByte == 0 ? 256 : widthByte;
            var height = heightByte == 0 ? 256 : heightByte;

            Assert.Equal(width, height);
            Assert.Equal(32, bitDepth);
            Assert.True(byteCount > 0);
            Assert.InRange((long)offset + byteCount, 1, stream.Length);
            dimensions.Add(width);
        }

        Assert.Equal(new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 }, dimensions);
    }

    [Theory]
    [InlineData("Pretendard-Regular.otf", "3FFBACDE6AB8411F1D2DB54BB9B1F0B3EE2A738932033722CF0388C06AED1C93", 400)]
    [InlineData("Pretendard-Medium.otf", "D39E50E4BB52B4993B6A4EEB821A171254745BD824446AF01E1F616B89FFACE0", 500)]
    [InlineData("Pretendard-SemiBold.otf", "C89BC43027DC7CDE5726E96223376F8EEC09302B2FC1F8147FD5B57CFC376118", 600)]
    public void PretendardStaticFontsAreOfficialAssetsWithRequiredCoverage(string fileName, string expectedHash, int expectedWeight)
    {
        var fontPath = Path.Combine(Root, "src", "TraceDeckFE", "Assets", "Fonts", fileName);
        var bytes = File.ReadAllBytes(fontPath);
        Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(bytes)));

        var typeface = new GlyphTypeface(new Uri(fontPath));
        Assert.Contains("Pretendard", typeface.FamilyNames.Values);
        Assert.Equal(expectedWeight, typeface.Weight.ToOpenTypeWeight());

        const string releaseText = "TraceDeck FE Reference 참조 오버레이 색상 저장 열기 설정 0123456789 #%";
        foreach (var rune in releaseText.EnumerateRunes())
        {
            Assert.True(typeface.CharacterToGlyphMap.ContainsKey(rune.Value), $"{fileName} lacks U+{rune.Value:X4}.");
        }
    }

    [Fact]
    public void TypographyUsesOnlyBundledPretendardForReleaseUi()
    {
        var appXaml = File.ReadAllText(Path.Combine(Root, "src", "TraceDeckFE", "App.xaml"));
        Assert.Contains("Assets/Fonts/#Pretendard", appXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Segoe UI", appXaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FontWeight\" Value=\"Regular", appXaml, StringComparison.Ordinal);
        Assert.Contains("FontWeight\" Value=\"Medium", appXaml, StringComparison.Ordinal);

        foreach (var relativePath in new[]
                 {
                     "MainWindow.xaml",
                     "Views/WindowPickerDialog.xaml",
                     "Views/UnsavedChangesDialog.xaml",
                     "Views/ReferenceReplacementDialog.xaml"
                 })
        {
            var xaml = File.ReadAllText(Path.Combine(Root, "src", "TraceDeckFE", relativePath));
            Assert.Contains("FontFamily=\"{StaticResource PretendardFontFamily}\"", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PublicRepositoryContainsGuidesAndUnmodifiedLicensePayloads()
    {
        var englishReadme = File.ReadAllText(Path.Combine(Root, "README.md"));
        var koreanReadme = File.ReadAllText(Path.Combine(Root, "README.ko.md"));
        var notices = File.ReadAllText(Path.Combine(Root, "THIRD_PARTY_NOTICES.md"));
        Assert.Contains("v1.0.0", englishReadme, StringComparison.Ordinal);
        Assert.Contains("Windows 10/11 x64", englishReadme, StringComparison.Ordinal);
        Assert.Contains(".TDFE", englishReadme, StringComparison.Ordinal);
        Assert.Contains("unofficial community tool", englishReadme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("v1.0.0", koreanReadme, StringComparison.Ordinal);
        Assert.Contains("비공식 커뮤니티 도구", koreanReadme, StringComparison.Ordinal);
        Assert.Contains("Magick.NET", notices, StringComparison.Ordinal);
        Assert.Contains("Pretendard", notices, StringComparison.Ordinal);

        var licenseDirectory = Path.Combine(Root, "licenses");
        foreach (var fileName in new[]
                 {
                     "Pretendard-OFL.txt",
                     "Magick.NET-Apache-2.0.txt",
                     "Magick.NET-NOTICE.txt",
                     "dotnet-runtime-LICENSE.txt",
                     "dotnet-runtime-THIRD-PARTY-NOTICES.txt"
                 })
        {
            var path = Path.Combine(licenseDirectory, fileName);
            Assert.True(File.Exists(path), fileName);
            Assert.True(new FileInfo(path).Length > 1_000, fileName);
        }
    }

    private static string Property(XDocument project, string name) =>
        project.Descendants(name).Single().Value.Trim();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TraceDeckFE.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("TraceDeckFE.sln was not found above the test output directory.");
    }
}
