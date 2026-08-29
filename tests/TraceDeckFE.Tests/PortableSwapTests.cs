using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using TraceDeckFE.Services;

namespace TraceDeckFE.Tests;

public sealed class PortableSwapTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void PortablePublishProfileIsSelfContainedSingleFileWithoutTrimming()
    {
        var profilePath = Path.Combine(Root, "src", "TraceDeckFE", "Properties", "PublishProfiles", "Portable.pubxml");
        var profile = XDocument.Load(profilePath);

        Assert.Equal("Release", Property(profile, "Configuration"));
        Assert.Equal("win-x64", Property(profile, "RuntimeIdentifier"));
        Assert.Equal("true", Property(profile, "SelfContained"));
        Assert.Equal("true", Property(profile, "PublishSingleFile"));
        Assert.Equal("true", Property(profile, "IncludeNativeLibrariesForSelfExtract"));
        Assert.Equal("false", Property(profile, "PublishTrimmed"));
        Assert.Equal("none", Property(profile, "DebugType"));
        Assert.Equal("false", Property(profile, "DebugSymbols"));
    }

    [Theory]
    [InlineData("Pretendard-Regular.ttf", "6D0AF5258997AEC7354A6E340FC2325BA321C410CA48B3AF858C8C3D6E92A324")]
    [InlineData("Pretendard-Medium.ttf", "3BAE579377EB8E9AC412CB4809EBC3DE1D956ED75995C1E346F0C1311053F4E2")]
    [InlineData("Pretendard-SemiBold.ttf", "5E1C548732AF70873103066C16E1369B9A8A871F0B38C321A1D5BC73E43CEA2D")]
    public void UserGuideUsesPinnedOfficialPretendardStaticFonts(string fileName, string expectedHash)
    {
        var path = Path.Combine(Root, "docs", "assets", "fonts", fileName);
        Assert.True(File.Exists(path), fileName);
        Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }

    [Fact]
    public void PortableServicesStayBelowTheProvidedApplicationDirectory()
    {
        var applicationDirectory = Path.Combine(Path.GetTempPath(), "TraceDeckFE-portable-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new PortableApplicationPaths(applicationDirectory);
            var expectedData = Path.Combine(applicationDirectory, "data");
            Assert.Equal(expectedData, paths.DataDirectory);

            var settings = new SettingsService(paths);
            Assert.Equal(Path.Combine(expectedData, "settings.json"), settings.SettingsPath);

            var logger = new TraceLogger(paths);
            logger.Info("portable path regression");
            Assert.True(Directory.Exists(Path.Combine(expectedData, "logs")));

            var recovery = new RecoveryService(paths, logger);
            Assert.Equal(Path.Combine(expectedData, "recovery"), recovery.Root);

            var normalizedRoot = Path.GetFullPath(applicationDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var path in new[] { paths.DataDirectory, settings.SettingsPath, recovery.Root })
            {
                Assert.StartsWith(normalizedRoot, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            if (Directory.Exists(applicationDirectory))
            {
                Directory.Delete(applicationDirectory, true);
            }
        }
    }

    [Fact]
    public void KoreanUserGuideToolchainIsReproducibleFromTrackedInputs()
    {
        var guide = Path.Combine(Root, "output", "pdf", "TraceDeck FE 사용방법.pdf");
        if (File.Exists(guide))
        {
            Assert.True(new FileInfo(guide).Length > 100_000);
            Assert.Equal("%PDF-", Encoding.ASCII.GetString(File.ReadAllBytes(guide), 0, 5));
        }

        var generator = File.ReadAllText(Path.Combine(Root, "tools", "build_user_guide.py"), Encoding.UTF8);
        var validator = File.ReadAllText(Path.Combine(Root, "tools", "validate_user_guide.py"), Encoding.UTF8);
        var requirements = File.ReadAllText(Path.Combine(Root, "tools", "requirements-docs.txt"), Encoding.UTF8);
        Assert.Contains("TraceDeck FE v1.0.0 사용방법", generator, StringComparison.Ordinal);
        Assert.Contains("EXPECTED_CHAPTERS", validator, StringComparison.Ordinal);
        Assert.Contains("Pretendard-SemiBold", validator, StringComparison.Ordinal);
        Assert.Contains("reportlab==", requirements, StringComparison.Ordinal);
        Assert.Contains("pypdf==", requirements, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseBuilderDefinesTheCleanPortableRootContract()
    {
        var script = File.ReadAllText(Path.Combine(Root, "tools", "Build-V1Release.ps1"), Encoding.UTF8);
        Assert.Contains("PublishSingleFile=true", script, StringComparison.Ordinal);
        Assert.Contains("IncludeNativeLibrariesForSelfExtract=true", script, StringComparison.Ordinal);
        Assert.Contains("PublishTrimmed=false", script, StringComparison.Ordinal);
        Assert.Contains("$guideName = 'TraceDeck FE 사용방법.pdf'", script, StringComparison.Ordinal);
        Assert.Contains("$releaseLicenseRoot = Join-Path $releaseRoot 'licenses'", script, StringComparison.Ordinal);
        Assert.Contains("Assert-Equal $rootItems.Count 3", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Copy-Item -LiteralPath (Join-Path $distributionRoot 'README.md')", script, StringComparison.Ordinal);
    }

    private static string Property(XDocument document, string name) =>
        document.Descendants(name).Single().Value.Trim();

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
