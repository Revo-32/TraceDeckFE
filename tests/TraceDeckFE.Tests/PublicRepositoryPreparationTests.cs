using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TraceDeckFE.Tests;

public sealed class PublicRepositoryPreparationTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void PublicIdentityAndMitLicenseAreExplicit()
    {
        var license = Read("LICENSE");
        Assert.StartsWith("MIT License", license, StringComparison.Ordinal);
        Assert.Contains("Copyright (c) 2026 Revo*32", license, StringComparison.Ordinal);

        foreach (var relativePath in new[]
                 {
                     "README.md", "README.ko.md", "CHANGELOG.md", "CONTRIBUTING.md",
                     "SECURITY.md", "THIRD_PARTY_NOTICES.md", "GITHUB_PUBLICATION_REPORT.md"
                 })
        {
            Assert.True(File.Exists(Path.Combine(Root, relativePath)), relativePath);
        }
    }

    [Fact]
    public void EnglishAndKoreanReadmesDescribeTheSamePublicRelease()
    {
        var english = Read("README.md");
        var korean = Read("README.ko.md");

        foreach (var marker in new[] { "v1.0.0", "TraceDeckFE-v1.0.0-win-x64-portable.zip", ".TDFE" })
        {
            Assert.Contains(marker, english, StringComparison.Ordinal);
            Assert.Contains(marker, korean, StringComparison.Ordinal);
        }

        Assert.Contains("README.ko.md", english, StringComparison.Ordinal);
        Assert.Contains("README.md", korean, StringComparison.Ordinal);
        Assert.Contains("unofficial community tool", english, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("비공식 커뮤니티 도구", korean, StringComparison.Ordinal);
    }

    [Fact]
    public void GitPolicyExcludesGeneratedBinariesAndPrivateRuntimeData()
    {
        var ignore = Read(".gitignore");
        foreach (var marker in new[] { "**/bin/", "**/obj/", "artifacts/", "output/", "**/data/", "*.TDFE", "**/TraceDeckFE.exe" })
        {
            Assert.Contains(marker, ignore, StringComparison.Ordinal);
        }

        var attributes = Read(".gitattributes");
        foreach (var marker in new[] { "*.png binary", "*.ico binary", "*.otf binary", "*.ttf binary", "*.pdf binary", "*.zip binary", "*.exe binary" })
        {
            Assert.Contains(marker, attributes, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ReleaseNotesAndThirdPartyPayloadsArePinned()
    {
        const string expectedReleaseHash = "CAF960FC730F38C96B52F7CF96190F84BB359942E0573D3FDF44EBC1E9D05116";
        Assert.Contains(expectedReleaseHash, Read(Path.Combine("docs", "releases", "v1.0.0.md")), StringComparison.Ordinal);

        var notices = Read("THIRD_PARTY_NOTICES.md");
        foreach (var marker in new[] { "Pretendard 1.3.9", "Magick.NET 14.16.0", "Microsoft .NET 8 Windows Desktop Runtime" })
        {
            Assert.Contains(marker, notices, StringComparison.Ordinal);
        }

        var licenseFiles = Directory.GetFiles(Path.Combine(Root, "licenses"));
        Assert.Equal(5, licenseFiles.Length);
        Assert.All(licenseFiles, path => Assert.True(SHA256.HashData(File.ReadAllBytes(path)).Length == 32, Path.GetFileName(path)));
    }

    [Fact]
    public void PublicTextContainsNoDeveloperMachinePath()
    {
        var privateProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var privateDocumentsRoot = Path.Combine(privateProfile, "Documents");
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".csproj", ".json", ".md", ".ps1", ".py", ".sln", ".svg", ".txt", ".xaml", ".xml", ".yaml", ".yml"
        };
        var publicRoots = new[] { ".github", "docs", "src", "tests", "tools", "test-fixtures" };
        var files = publicRoots
            .SelectMany(relative => Directory.EnumerateFiles(Path.Combine(Root, relative), "*", SearchOption.AllDirectories))
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(Path.Combine("docs", "USER_GUIDE_REPORT.md"), StringComparison.OrdinalIgnoreCase));

        foreach (var path in files)
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            Assert.DoesNotContain(privateProfile, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(privateDocumentsRoot, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath), Encoding.UTF8);

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
