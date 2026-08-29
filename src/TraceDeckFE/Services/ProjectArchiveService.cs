using TraceDeckFE.Localization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TraceDeckFE.Models;

namespace TraceDeckFE.Services;

public sealed class ProjectArchiveException : Exception
{
    public ProjectArchiveException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class ProjectArchiveService
{
    public const int CurrentFormatVersion = 1;
    private const int MaximumMetadataBytes = 4 * 1024 * 1024;
    private const long MaximumReferenceBytes = 512L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };

    public static string FileDialogFilter => L.Get("File.Project");

    public async Task SaveAsync(
        string path,
        TdfProjectPackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(package);
        path = EnsureExtension(System.IO.Path.GetFullPath(path));
        var directory = System.IO.Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new IOException("The project destination folder does not exist.");
        }

        ValidatePackage(package);
        var tempPath = System.IO.Path.Combine(
            directory,
            $".{System.IO.Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await WriteArchiveAsync(tempPath, package, cancellationToken).ConfigureAwait(false);
            _ = await LoadCoreAsync(tempPath, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                catch (Exception exception) when (exception is PlatformNotSupportedException or IOException)
                {
                    File.Move(tempPath, path, overwrite: true);
                }
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // A stale same-directory temp is safer than damaging the destination project.
            }
        }
    }

    public Task<TdfProjectPackage> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return LoadCoreAsync(path, cancellationToken);
    }

    public static string EnsureExtension(string path) =>
        System.IO.Path.GetExtension(path).Equals(".tdfe", StringComparison.OrdinalIgnoreCase)
            ? path
            : path + ".TDFE";

    public static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string CreateReferenceEntry(string originalFilename, string format)
    {
        var extension = System.IO.Path.GetExtension(originalFilename).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8 || extension.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            extension = format.Trim().TrimStart('.').ToLowerInvariant();
        }

        extension = extension switch
        {
            "jpeg" => "jpg",
            "tiff" => "tiff",
            _ => extension
        };
        if (string.IsNullOrWhiteSpace(extension) || extension.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            extension = "bin";
        }

        return $"reference/source.{extension}";
    }

    private static async Task WriteArchiveAsync(
        string tempPath,
        TdfProjectPackage package,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            tempPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            1024 * 128,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteJsonEntryAsync(archive, "manifest.json", package.Manifest, cancellationToken).ConfigureAwait(false);
            await WriteJsonEntryAsync(archive, "project/state.json", package.State, cancellationToken).ConfigureAwait(false);
            if (package.ReferenceBytes is { } bytes && package.Manifest.ReferenceEntry is { } referenceEntry)
            {
                var entry = archive.CreateEntry(referenceEntry, CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            }
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static async Task WriteJsonEntryAsync<T>(
        ZipArchive archive,
        string name,
        T value,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<TdfProjectPackage> LoadCoreAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 128,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            ValidateEntries(archive);

            var manifestEntry = archive.GetEntry("manifest.json")
                ?? throw new ProjectArchiveException(L.Get("Error.MissingManifest"));
            var manifest = await DeserializeEntryAsync<ProjectManifest>(manifestEntry, MaximumMetadataBytes, cancellationToken)
                .ConfigureAwait(false);
            ValidateManifest(manifest);

            var stateEntry = archive.GetEntry("project/state.json")
                ?? throw new ProjectArchiveException(L.Get("Error.MissingState"));
            var state = await DeserializeEntryAsync<ProjectStateData>(stateEntry, MaximumMetadataBytes, cancellationToken)
                .ConfigureAwait(false);
            ValidateState(state);

            byte[]? referenceBytes = null;
            if (state.Reference is not null)
            {
                if (string.IsNullOrWhiteSpace(manifest.ReferenceEntry))
                {
                    throw new ProjectArchiveException(L.Get("Error.MissingReferenceEntry"));
                }

                var sourceEntry = archive.GetEntry(manifest.ReferenceEntry)
                    ?? throw new ProjectArchiveException(L.Get("Error.MissingReference"));
                referenceBytes = await ReadEntryAsync(sourceEntry, MaximumReferenceBytes, cancellationToken).ConfigureAwait(false);
                if (referenceBytes.Length == 0)
                {
                    throw new ProjectArchiveException(L.Get("Error.EmptyReference"));
                }

                if (!string.IsNullOrWhiteSpace(manifest.ReferenceSha256) &&
                    !ComputeSha256(referenceBytes).Equals(manifest.ReferenceSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ProjectArchiveException(L.Get("Error.Integrity"));
                }
            }
            else if (!string.IsNullOrWhiteSpace(manifest.ReferenceEntry))
            {
                throw new ProjectArchiveException(L.Get("Error.ManifestMismatch"));
            }

            return new TdfProjectPackage(manifest, state, referenceBytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ProjectArchiveException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            throw new ProjectArchiveException(L.Get("Error.ProjectRead"), exception);
        }
    }

    private static async Task<T> DeserializeEntryAsync<T>(
        ZipArchiveEntry entry,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (entry.Length < 0 || entry.Length > maximumBytes)
        {
            throw new ProjectArchiveException(L.Get("Error.MetadataSize"));
        }

        await using var stream = entry.Open();
        var result = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        return result ?? throw new ProjectArchiveException(L.Get("Error.MalformedMetadata"));
    }

    private static async Task<byte[]> ReadEntryAsync(
        ZipArchiveEntry entry,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (entry.Length < 0 || entry.Length > maximumBytes || entry.CompressedLength < 0)
        {
            throw new ProjectArchiveException(L.Get("Error.ReferenceSize"));
        }

        await using var input = entry.Open();
        using var output = new MemoryStream(entry.Length > int.MaxValue ? 0 : (int)entry.Length);
        var buffer = new byte[1024 * 128];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > maximumBytes)
            {
                throw new ProjectArchiveException(L.Get("Error.ReferenceSize"));
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return output.ToArray();
    }

    private static void ValidateEntries(ZipArchive archive)
    {
        if (archive.Entries.Count > 32)
        {
            throw new ProjectArchiveException(L.Get("Error.EntryCount"));
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            var segments = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith('/') || System.IO.Path.IsPathRooted(name) ||
                name.Contains(':') || segments.Any(segment => segment is "." or "..") || !names.Add(name))
            {
                throw new ProjectArchiveException(L.Get("Error.UnsafeEntry"));
            }
        }
    }

    private static void ValidateManifest(ProjectManifest manifest)
    {
        if (!string.Equals(manifest.Format, "TraceDeckFE", StringComparison.Ordinal))
        {
            throw new ProjectArchiveException(L.Get("Error.NotProject"));
        }

        if (manifest.FormatVersion > CurrentFormatVersion)
        {
            throw new ProjectArchiveException(L.Get("Error.NewerVersion"));
        }

        if (manifest.FormatVersion != CurrentFormatVersion || manifest.ProjectId == Guid.Empty ||
            manifest.CreatedUtc == default || manifest.ModifiedUtc == default)
        {
            throw new ProjectArchiveException(L.Get("Error.InvalidManifest"));
        }

        if (manifest.ReferenceEntry is { } entry && !IsSafeReferenceEntry(entry))
        {
            throw new ProjectArchiveException(L.Get("Error.UnsafeReference"));
        }
    }

    private static void ValidateState(ProjectStateData state)
    {
        if (state.Palette is null || state.Overlay is null || state.Guides is null || state.Color is null || state.Ui is null)
        {
            throw new ProjectArchiveException(L.Get("Error.MalformedState"));
        }

        if (!double.IsFinite(state.Overlay.Opacity) || state.Overlay.Opacity is < 0 or > 1 ||
            !double.IsFinite(state.Guides.GridSpacing) || state.Guides.GridSpacing is < 16 or > 400 ||
            !double.IsFinite(state.Guides.Opacity) || state.Guides.Opacity is < 0.05 or > 1 ||
            state.AutoPaletteColorCount is < 2 or > 12 ||
            !double.IsFinite(state.Ui.ControllerWidth) || state.Ui.ControllerWidth is < 280 or > 1200)
        {
            throw new ProjectArchiveException(L.Get("Error.InvalidState"));
        }

        if (state.Reference is { } reference &&
            (reference.PixelWidth <= 0 || reference.PixelHeight <= 0 ||
             !double.IsFinite(reference.NormalizedCenterX) || !double.IsFinite(reference.NormalizedCenterY) ||
             !double.IsFinite(reference.NormalizedVisualWidth) || reference.NormalizedVisualWidth <= 0 ||
             !double.IsFinite(reference.Rotation) || !double.IsFinite(reference.Contrast) ||
             reference.Contrast is < -100 or > 100 || string.IsNullOrWhiteSpace(reference.SourceFormat)))
        {
            throw new ProjectArchiveException(L.Get("Error.InvalidReferenceState"));
        }

        var ids = new HashSet<Guid>();
        foreach (var item in state.Palette)
        {
            if (item is null || item.Id == Guid.Empty || !ids.Add(item.Id) ||
                string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 80)
            {
                throw new ProjectArchiveException(L.Get("Error.MalformedPalette"));
            }
        }
    }

    public static void ValidatePackage(TdfProjectPackage package)
    {
        ValidateManifest(package.Manifest);
        ValidateState(package.State);
        if (package.State.Reference is null)
        {
            if (package.ReferenceBytes is not null || package.Manifest.ReferenceEntry is not null)
            {
                throw new ProjectArchiveException(L.Get("Error.InconsistentReference"));
            }
            return;
        }

        if (package.ReferenceBytes is not { Length: > 0 } bytes || package.Manifest.ReferenceEntry is null ||
            !ComputeSha256(bytes).Equals(package.Manifest.ReferenceSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProjectArchiveException(L.Get("Error.InvalidReferenceData"));
        }
    }

    private static bool IsSafeReferenceEntry(string entry)
    {
        var normalized = entry.Replace('\\', '/');
        return normalized.StartsWith("reference/", StringComparison.Ordinal) &&
               !normalized.StartsWith('/') && !normalized.Contains(':') &&
               !normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or "..");
    }
}
