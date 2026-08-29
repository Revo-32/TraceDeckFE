[CmdletBinding()]
param(
    [string]$DotnetPath,
    [string]$PythonPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$version = '1.0.0'
$packageName = "TraceDeckFE-v$version-win-x64-portable"
$guideName = 'TraceDeck FE 사용방법.pdf'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$publishRoot = [IO.Path]::GetFullPath((Join-Path $artifactsRoot '.publish-v1.0.0-single-file'))
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $artifactsRoot $packageName))
$zipPath = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "$packageName.zip"))
$projectPath = Join-Path $repoRoot 'src\TraceDeckFE\TraceDeckFE.csproj'
$profilePath = Join-Path $repoRoot 'src\TraceDeckFE\Properties\PublishProfiles\Portable.pubxml'
$guidePath = Join-Path $repoRoot "output\pdf\$guideName"
$guideValidatorPath = Join-Path $repoRoot 'tools\validate_user_guide.py'
$licenseSource = Join-Path $repoRoot 'licenses'

function Assert-ExactArtifactPath {
    param([string]$Path, [string]$ExpectedLeaf)
    $fullPath = [IO.Path]::GetFullPath($Path)
    $prefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($fullPath) -cne $ExpectedLeaf) {
        throw "Unsafe artifact path: $fullPath"
    }
}

function Remove-ExactArtifact {
    param([string]$Path, [string]$ExpectedLeaf)
    Assert-ExactArtifactPath -Path $Path -ExpectedLeaf $ExpectedLeaf
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Assert-Equal {
    param([object]$Actual, [object]$Expected, [string]$Label)
    if ($Actual -ne $Expected) {
        throw "$Label mismatch. Expected '$Expected', found '$Actual'."
    }
}

function Assert-True {
    param([bool]$Condition, [string]$Label)
    if (-not $Condition) {
        throw "Validation failed: $Label"
    }
}

if ([string]::IsNullOrWhiteSpace($DotnetPath)) {
    if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_HOST_PATH) -and (Test-Path -LiteralPath $env:DOTNET_HOST_PATH)) {
        $DotnetPath = $env:DOTNET_HOST_PATH
    }
    else {
        $DotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
    }
}
if ([string]::IsNullOrWhiteSpace($PythonPath)) {
    $PythonPath = (Get-Command python -ErrorAction Stop).Source
}

$DotnetPath = [IO.Path]::GetFullPath($DotnetPath)
$PythonPath = [IO.Path]::GetFullPath($PythonPath)
Assert-True (Test-Path -LiteralPath $DotnetPath -PathType Leaf) 'dotnet executable exists'
Assert-True (Test-Path -LiteralPath $PythonPath -PathType Leaf) 'Python executable exists'
Assert-True (Test-Path -LiteralPath $projectPath -PathType Leaf) 'application project exists'
Assert-True (Test-Path -LiteralPath $profilePath -PathType Leaf) 'portable publish profile exists'
Assert-True (Test-Path -LiteralPath $guidePath -PathType Leaf) 'Korean user guide exists'
Assert-True (Test-Path -LiteralPath $guideValidatorPath -PathType Leaf) 'guide validator exists'
Assert-True (Test-Path -LiteralPath $licenseSource -PathType Container) 'license source exists'

[xml]$profile = Get-Content -LiteralPath $profilePath -Raw -Encoding utf8
$properties = $profile.Project.PropertyGroup
Assert-Equal ([string]$properties.RuntimeIdentifier) 'win-x64' 'publish RuntimeIdentifier'
Assert-Equal ([string]$properties.SelfContained) 'true' 'publish SelfContained'
Assert-Equal ([string]$properties.PublishSingleFile) 'true' 'publish PublishSingleFile'
Assert-Equal ([string]$properties.IncludeNativeLibrariesForSelfExtract) 'true' 'publish native self-extraction'
Assert-Equal ([string]$properties.PublishTrimmed) 'false' 'publish trimming policy'

& $PythonPath $guideValidatorPath $guidePath
if ($LASTEXITCODE -ne 0) {
    throw "User-guide validation failed with exit code $LASTEXITCODE."
}

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
Remove-ExactArtifact -Path $publishRoot -ExpectedLeaf '.publish-v1.0.0-single-file'
Remove-ExactArtifact -Path $releaseRoot -ExpectedLeaf $packageName
Remove-ExactArtifact -Path $zipPath -ExpectedLeaf "$packageName.zip"
New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null

& $DotnetPath publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -p:PublishReadyToRun=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    --output $publishRoot
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$publishedItems = @(Get-ChildItem -LiteralPath $publishRoot -Force)
Assert-Equal $publishedItems.Count 1 'single-file publish item count'
Assert-Equal $publishedItems[0].Name 'TraceDeckFE.exe' 'single-file publish output name'
Assert-True (-not $publishedItems[0].PSIsContainer) 'single-file publish output is a file'

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
$releaseLicenseRoot = Join-Path $releaseRoot 'licenses'
New-Item -ItemType Directory -Path $releaseLicenseRoot -Force | Out-Null
Copy-Item -LiteralPath $publishedItems[0].FullName -Destination (Join-Path $releaseRoot 'TraceDeckFE.exe') -Force
Copy-Item -LiteralPath $guidePath -Destination (Join-Path $releaseRoot $guideName) -Force
Get-ChildItem -LiteralPath $licenseSource -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $releaseLicenseRoot -Force
}

$exePath = Join-Path $releaseRoot 'TraceDeckFE.exe'
$packagedGuidePath = Join-Path $releaseRoot $guideName
Assert-True (Test-Path -LiteralPath $exePath -PathType Leaf) 'TraceDeckFE.exe is present'
Assert-True (Test-Path -LiteralPath $packagedGuidePath -PathType Leaf) 'Korean user guide is present'
Assert-Equal (Get-FileHash -LiteralPath $packagedGuidePath -Algorithm SHA256).Hash (Get-FileHash -LiteralPath $guidePath -Algorithm SHA256).Hash 'packaged guide hash'

$rootItems = @(Get-ChildItem -LiteralPath $releaseRoot -Force)
Assert-Equal $rootItems.Count 3 'clean package root item count'
Assert-True (@($rootItems | Where-Object Name -CEQ 'TraceDeckFE.exe').Count -eq 1) 'clean root contains TraceDeckFE.exe once'
Assert-True (@($rootItems | Where-Object Name -CEQ $guideName).Count -eq 1) 'clean root contains guide once'
Assert-True (@($rootItems | Where-Object { $_.Name -ceq 'licenses' -and $_.PSIsContainer }).Count -eq 1) 'clean root contains licenses directory once'

$fileInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
Assert-Equal $fileInfo.ProductName 'TraceDeck FE' 'Product name'
Assert-Equal $fileInfo.FileDescription 'TraceDeck FE' 'File description'
Assert-Equal $fileInfo.FileVersion '1.0.0.0' 'File version'
Assert-Equal $fileInfo.ProductVersion '1.0.0' 'Product version'
Assert-True ([string]::IsNullOrWhiteSpace($fileInfo.CompanyName)) 'company metadata is empty'

$exeStream = [IO.File]::OpenRead($exePath)
$exeReader = [IO.BinaryReader]::new($exeStream)
try {
    $exeStream.Position = 0x3c
    $peOffset = $exeReader.ReadInt32()
    $exeStream.Position = $peOffset + 4
    $machine = $exeReader.ReadUInt16()
}
finally {
    $exeReader.Dispose()
    $exeStream.Dispose()
}
Assert-Equal $machine ([UInt16]0x8664) 'PE machine type (x64)'

if (-not ('TraceDeckRelease.NativeIconProbe' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace TraceDeckRelease
{
    public static class NativeIconProbe
    {
        private delegate bool EnumResNameProc(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryExW(string fileName, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool EnumResourceNamesW(IntPtr module, IntPtr type, EnumResNameProc callback, IntPtr parameter);
        [DllImport("kernel32.dll")]
        private static extern bool FreeLibrary(IntPtr module);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindResourceW(IntPtr module, IntPtr name, IntPtr type);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LockResource(IntPtr resourceData);

        public static int[] GetGroupIconDimensions(string path)
        {
            const uint LoadLibraryAsDataFile = 0x00000002;
            IntPtr module = LoadLibraryExW(path, IntPtr.Zero, LoadLibraryAsDataFile);
            if (module == IntPtr.Zero) return Array.Empty<int>();
            try
            {
                IntPtr resourceName = IntPtr.Zero;
                EnumResNameProc callback = delegate(IntPtr ignoredModule, IntPtr ignoredType, IntPtr name, IntPtr ignoredParameter)
                {
                    resourceName = name;
                    return false;
                };
                EnumResourceNamesW(module, new IntPtr(14), callback, IntPtr.Zero);
                GC.KeepAlive(callback);
                if (resourceName == IntPtr.Zero) return Array.Empty<int>();
                IntPtr resource = FindResourceW(module, resourceName, new IntPtr(14));
                IntPtr loaded = resource == IntPtr.Zero ? IntPtr.Zero : LoadResource(module, resource);
                IntPtr data = loaded == IntPtr.Zero ? IntPtr.Zero : LockResource(loaded);
                if (data == IntPtr.Zero) return Array.Empty<int>();
                int count = (ushort)Marshal.ReadInt16(data, 4);
                int[] dimensions = new int[count];
                for (int index = 0; index < count; index++)
                {
                    int width = Marshal.ReadByte(data, 6 + (index * 14));
                    dimensions[index] = width == 0 ? 256 : width;
                }
                Array.Sort(dimensions);
                return dimensions;
            }
            finally { FreeLibrary(module); }
        }
    }

    public static class BinaryProbe
    {
        public static bool ContainsUtf8(string path, string value)
        {
            byte[] needle = System.Text.Encoding.UTF8.GetBytes(value);
            byte[] buffer = new byte[(1024 * 1024) + needle.Length];
            int carry = 0;
            using (var stream = System.IO.File.OpenRead(path))
            {
                while (true)
                {
                    int read = stream.Read(buffer, carry, buffer.Length - carry);
                    int total = carry + read;
                    for (int index = 0; index <= total - needle.Length; index++)
                    {
                        bool match = true;
                        for (int part = 0; part < needle.Length; part++)
                        {
                            if (buffer[index + part] != needle[part]) { match = false; break; }
                        }
                        if (match) return true;
                    }
                    if (read == 0) return false;
                    carry = Math.Min(needle.Length - 1, total);
                    Buffer.BlockCopy(buffer, total - carry, buffer, 0, carry);
                }
            }
        }
    }
}
'@
}

$nativeIconDimensions = [TraceDeckRelease.NativeIconProbe]::GetGroupIconDimensions($exePath)
Assert-Equal ([string]::Join(',', $nativeIconDimensions)) '16,20,24,32,40,48,64,128,256' 'native EXE icon dimensions'
foreach ($bundleToken in @('TraceDeckFE.g.resources', 'TraceDeckFE.Resources.Strings.en.json', 'TraceDeckFE.Resources.Strings.ko.json', 'Magick.Native-Q8-x64.dll')) {
    Assert-True ([TraceDeckRelease.BinaryProbe]::ContainsUtf8($exePath, $bundleToken)) "single-file bundle contains $bundleToken"
}
Assert-True (-not [TraceDeckRelease.BinaryProbe]::ContainsUtf8($exePath, 'C:\Users\')) 'single-file bundle contains no Windows user profile path'

$preBundleAssemblyPath = Join-Path $repoRoot 'src\TraceDeckFE\bin\Release\net8.0-windows\win-x64\TraceDeckFE.dll'
Assert-True (Test-Path -LiteralPath $preBundleAssemblyPath -PathType Leaf) 'pre-bundle assembly exists for resource audit'
$assembly = [Reflection.Assembly]::LoadFile($preBundleAssemblyPath)
$resourceNames = @($assembly.GetManifestResourceNames())
Assert-True ($resourceNames -contains 'TraceDeckFE.g.resources') 'compiled WPF resource manifest exists'
Assert-True ($resourceNames -contains 'TraceDeckFE.Resources.Strings.en.json') 'English localization is embedded'
Assert-True ($resourceNames -contains 'TraceDeckFE.Resources.Strings.ko.json') 'Korean localization is embedded'

$resourceStream = $assembly.GetManifestResourceStream('TraceDeckFE.g.resources')
$resourceReader = [Resources.ResourceReader]::new($resourceStream)
$wpfEntries = @()
$enumerator = $resourceReader.GetEnumerator()
while ($enumerator.MoveNext()) {
    $wpfEntries += ([string]$enumerator.Key).ToLowerInvariant()
}
$resourceReader.Dispose()
$resourceStream.Dispose()
$fontEntries = @($wpfEntries | Where-Object { $_ -like 'assets/fonts/*.otf' })
Assert-Equal $fontEntries.Count 3 'embedded application font count'
foreach ($fontName in @('pretendard-regular.otf', 'pretendard-medium.otf', 'pretendard-semibold.otf')) {
    Assert-True ($fontEntries -contains "assets/fonts/$fontName") "embedded $fontName"
}
Assert-True ($wpfEntries -contains 'assets/tracedeck_fe_mini_logo.png') 'upper-left brand logo is embedded'
Assert-True ($wpfEntries -contains 'assets/tracedeckfe.ico') 'application ICO is embedded'

$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Recurse -File -Include *.cs,*.xaml,*.csproj
$fontInstallHits = $sourceFiles | Select-String -Pattern 'AddFontResource|PrivateFontCollection|RegistryKey.*Fonts|Windows\\CurrentVersion\\Fonts'
Assert-Equal @($fontInstallHits).Count 0 'system-font installation code count'

$requiredLicenseFiles = @(
    'Pretendard-OFL.txt',
    'Magick.NET-Apache-2.0.txt',
    'Magick.NET-NOTICE.txt',
    'dotnet-runtime-LICENSE.txt',
    'dotnet-runtime-THIRD-PARTY-NOTICES.txt'
)
foreach ($licenseFile in $requiredLicenseFiles) {
    $path = Join-Path $releaseLicenseRoot $licenseFile
    Assert-True (Test-Path -LiteralPath $path -PathType Leaf) "license $licenseFile is present"
    Assert-True ((Get-Item -LiteralPath $path).Length -gt 1KB) "license $licenseFile is nonempty"
}

$packagedFiles = @(Get-ChildItem -LiteralPath $releaseRoot -Recurse -File -Force)
$packagedDirectories = @(Get-ChildItem -LiteralPath $releaseRoot -Recurse -Directory -Force)
Assert-Equal $packagedFiles.Count 7 'packaged file count'
Assert-Equal $packagedDirectories.Count 1 'packaged directory count'
Assert-Equal $packagedDirectories[0].Name 'licenses' 'only packaged directory'
$forbiddenFiles = @($packagedFiles | Where-Object {
    $_.Extension -in @('.pdb', '.dll', '.json', '.tdfe', '.log') -or $_.Name -eq 'README.md'
})
Assert-Equal $forbiddenFiles.Count 0 'forbidden packaged file count'

$leakPatterns = @('C:\Users\', '\AppData\', '\Downloads\', '\Documents\')
$leaks = @()
foreach ($textFile in @($packagedFiles | Where-Object Extension -eq '.txt')) {
    foreach ($pattern in $leakPatterns) {
        if (Select-String -LiteralPath $textFile.FullName -SimpleMatch -Pattern $pattern -Quiet) {
            $leaks += "$($textFile.FullName): $pattern"
        }
    }
}
Assert-Equal $leaks.Count 0 'developer-path leak count'

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($releaseRoot, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $true)
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $archiveEntries = @($archive.Entries)
    $archiveFiles = @($archiveEntries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) })
    Assert-Equal $archiveFiles.Count 7 'ZIP file count'
    $invalidEntries = @($archiveEntries | Where-Object {
        -not $_.FullName.Replace('\', '/').StartsWith("$packageName/", [StringComparison]::Ordinal)
    })
    Assert-Equal $invalidEntries.Count 0 'ZIP root-folder violation count'
    Assert-True (@($archiveFiles | Where-Object { $_.FullName.Replace('\', '/') -ceq "$packageName/TraceDeckFE.exe" }).Count -eq 1) 'ZIP contains TraceDeckFE.exe once'
    Assert-True (@($archiveFiles | Where-Object { $_.FullName.Replace('\', '/') -ceq "$packageName/$guideName" }).Count -eq 1) 'ZIP contains guide once'
    foreach ($licenseFile in $requiredLicenseFiles) {
        Assert-True (@($archiveFiles | Where-Object { $_.FullName.Replace('\', '/') -ceq "$packageName/licenses/$licenseFile" }).Count -eq 1) "ZIP contains license $licenseFile once"
    }
}
finally {
    $archive.Dispose()
}

Remove-ExactArtifact -Path $publishRoot -ExpectedLeaf '.publish-v1.0.0-single-file'

$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
$zipSize = (Get-Item -LiteralPath $zipPath).Length
$exeHash = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash
$guideHash = (Get-FileHash -LiteralPath $packagedGuidePath -Algorithm SHA256).Hash
Write-Host "Release folder: $releaseRoot"
Write-Host "Release ZIP:    $zipPath"
Write-Host "ZIP bytes:      $zipSize"
Write-Host "ZIP SHA-256:    $zipHash"
Write-Host "EXE SHA-256:    $exeHash"
Write-Host "PDF SHA-256:    $guideHash"
Write-Host "Files:          $($packagedFiles.Count)"
Write-Host 'Release validation: PASS'
