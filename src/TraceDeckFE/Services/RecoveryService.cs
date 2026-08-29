using System.Text.Json;
using TraceDeckFE.Models;

namespace TraceDeckFE.Services;

public sealed record RecoverySnapshot
{
    public int Version { get; init; } = 1;
    public ProjectManifest Manifest { get; init; } = new();
    public ProjectStateData State { get; init; } = new();
    public string? ManualPath { get; init; }
    public DateTimeOffset CapturedUtc { get; init; }
    public string? AssetHash { get; init; }
}
public sealed record RecoveryCandidate(string SnapshotPath, RecoverySnapshot Snapshot, TdfProjectPackage Package);

public sealed class RecoveryService(IApplicationPaths paths, ITraceLogger logger)
{
    public const int RetainedSnapshots = 3;
    private readonly SemaphoreSlim _gate = new(1,1);
    private readonly Dictionary<Guid,string> _lastState = [];
    private readonly HashSet<string> _verifiedAssets = new(StringComparer.OrdinalIgnoreCase);
    public string Root => Path.Combine(paths.DataDirectory, "recovery");
    private string Folder(Guid id) => Path.Combine(Root, id.ToString("N"));
    public bool IsWriting { get; private set; }

    public async Task<bool> WriteSnapshotAsync(TdfProjectPackage package, string? manualPath, bool dirty, string stateToken, CancellationToken token = default)
    {
        if (!dirty || !await _gate.WaitAsync(0, token).ConfigureAwait(false)) return false;
        IsWriting = true;
        try
        {
            if (_lastState.TryGetValue(package.Manifest.ProjectId, out var previous) && previous == stateToken) return false;
            ProjectArchiveService.ValidatePackage(package);
            var folder = Folder(package.Manifest.ProjectId);
            Directory.CreateDirectory(folder);
            var hash = package.Manifest.ReferenceSha256;
            if (package.ReferenceBytes is { } bytes && hash is not null)
            {
                var asset = Path.Combine(folder, hash + ".bin");
                if (!_verifiedAssets.Contains(asset) || !File.Exists(asset))
                {
                    var valid = File.Exists(asset) && new FileInfo(asset).Length == bytes.Length;
                    if (valid)
                    {
                        await using var input = File.OpenRead(asset);
                        valid = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(input,token).ConfigureAwait(false)).Equals(hash,StringComparison.OrdinalIgnoreCase);
                    }
                    if (!valid) await AtomicFile.WriteAsync(asset, bytes, token).ConfigureAwait(false);
                    _verifiedAssets.Add(asset);
                }
            }
            var snapshot = new RecoverySnapshot
            {
                Manifest = package.Manifest, State = package.State, ManualPath = manualPath,
                CapturedUtc = package.Manifest.ModifiedUtc, AssetHash = hash
            };
            var name = $"snapshot-{snapshot.CapturedUtc.UtcTicks:D19}-{Guid.NewGuid():N}.json";
            await AtomicFile.WriteAsync(Path.Combine(folder, name), JsonSerializer.SerializeToUtf8Bytes(snapshot, SettingsService.JsonOptions), token).ConfigureAwait(false);
            _lastState[package.Manifest.ProjectId] = stateToken;
            // Cleanup only follows a complete atomic snapshot. A cleanup error never invalidates it.
            try
            {
                foreach (var old in Directory.GetFiles(folder, "snapshot-*.json").OrderByDescending(Path.GetFileName).Skip(RetainedSnapshots)) File.Delete(old);
                CleanupAssets(folder);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { logger.Warning("Old recovery cleanup deferred: " + e.Message); }
            return true;
        }
        finally { IsWriting = false; _gate.Release(); }
    }

    public async Task<IReadOnlyList<RecoveryCandidate>> FindCandidatesAsync(CancellationToken token = default)
    {
        var candidates = new List<RecoveryCandidate>();
        try
        {
            if (!Directory.Exists(Root)) return candidates;
            foreach (var folder in Directory.GetDirectories(Root))
            {
                if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out var id)) continue;
                try
                {
                    var dismissed = ReadDismissed(folder);
                    foreach (var file in Directory.GetFiles(folder, "snapshot-*.json").OrderByDescending(Path.GetFileName))
                    {
                        try
                        {
                            var snapshot = await ReadSnapshotAsync(file, token).ConfigureAwait(false);
                            if (snapshot.Manifest.ProjectId != id || snapshot.CapturedUtc <= dismissed) continue;
                            if (!string.IsNullOrWhiteSpace(snapshot.ManualPath) && File.Exists(snapshot.ManualPath) &&
                                File.GetLastWriteTimeUtc(snapshot.ManualPath) >= snapshot.CapturedUtc.UtcDateTime) continue;
                            var package = await ReadPackageAsync(folder, snapshot, token).ConfigureAwait(false);
                            candidates.Add(new(file, snapshot, package));
                            break;
                        }
                        catch (Exception e) when (IsRecoverable(e)) { logger.Warning("Unreadable recovery skipped: " + e.Message); }
                    }
                }
                catch (Exception e) when (IsRecoverable(e)) { logger.Warning("Recovery folder skipped: " + e.Message); }
            }
        }
        catch (Exception e) when (IsRecoverable(e)) { logger.Warning("Recovery unavailable: " + e.Message); }
        return candidates.OrderByDescending(c => c.Snapshot.CapturedUtc).ToArray();
    }
    public async Task DismissAsync(Guid id, DateTimeOffset throughUtc, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (Directory.Exists(Folder(id))) await AtomicFile.WriteAsync(Path.Combine(Folder(id), "dismissed.json"),
                JsonSerializer.SerializeToUtf8Bytes(throughUtc), token).ConfigureAwait(false);
            _lastState.Remove(id);
        }
        finally { _gate.Release(); }
    }
    public async Task ManualSaveSucceededAsync(Guid id, DateTimeOffset savedUtc, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var folder = Folder(id);
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.GetFiles(folder,"snapshot-*.json"))
            {
                try { if ((await ReadSnapshotAsync(file,token).ConfigureAwait(false)).CapturedUtc <= savedUtc) File.Delete(file); }
                catch (Exception e) when (IsRecoverable(e)) { logger.Warning("Recovery cleanup deferred: " + e.Message); }
            }
            CleanupAssets(folder);
            _lastState.Remove(id);
        }
        finally { _gate.Release(); }
    }
    private static async Task<RecoverySnapshot> ReadSnapshotAsync(string path, CancellationToken token)
    {
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Recovery metadata too large.");
        var snapshot = JsonSerializer.Deserialize<RecoverySnapshot>(await File.ReadAllBytesAsync(path, token).ConfigureAwait(false), SettingsService.JsonOptions)
            ?? throw new InvalidDataException("Missing recovery metadata.");
        if (snapshot.Version != 1 || snapshot.Manifest is null || snapshot.State is null || snapshot.CapturedUtc == default ||
            snapshot.AssetHash is { } hash && (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c))) ||
            snapshot.ManualPath is { } manual && (!Path.IsPathFullyQualified(manual) || manual.IndexOfAny(Path.GetInvalidPathChars()) >= 0))
            throw new InvalidDataException("Invalid recovery metadata.");
        return snapshot;
    }
    private static async Task<TdfProjectPackage> ReadPackageAsync(string folder, RecoverySnapshot snapshot, CancellationToken token)
    {
        byte[]? bytes = null;
        if (snapshot.AssetHash is { } hash)
        {
            var path = Path.Combine(folder, hash + ".bin");
            if (new FileInfo(path).Length > 512L * 1024 * 1024) throw new InvalidDataException("Recovery asset too large.");
            bytes = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
        }
        var package = new TdfProjectPackage(snapshot.Manifest,snapshot.State,bytes);
        ProjectArchiveService.ValidatePackage(package);
        return package;
    }
    private static DateTimeOffset ReadDismissed(string folder)
    {
        var path = Path.Combine(folder,"dismissed.json");
        try { return File.Exists(path) ? JsonSerializer.Deserialize<DateTimeOffset>(File.ReadAllText(path)) : default; }
        catch (Exception e) when (IsRecoverable(e)) { return default; }
    }
    private static void CleanupAssets(string folder)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(folder,"snapshot-*.json"))
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<RecoverySnapshot>(File.ReadAllText(file), SettingsService.JsonOptions);
                if (snapshot?.AssetHash is { } hash) used.Add(hash);
            }
            catch (JsonException) { return; } // Keep assets if a snapshot's references cannot be determined safely.
        }
        foreach (var asset in Directory.GetFiles(folder,"*.bin")) if (!used.Contains(Path.GetFileNameWithoutExtension(asset))) File.Delete(asset);
    }
    public static bool IsRecoverable(Exception e) => e is IOException or UnauthorizedAccessException or JsonException or ProjectArchiveException or ArgumentException or NotSupportedException;
}
