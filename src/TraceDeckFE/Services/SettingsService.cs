using System.Text.Json;
using TraceDeckFE.Models;

namespace TraceDeckFE.Services;

public interface IApplicationPaths { string DataDirectory { get; } }
public sealed class PortableApplicationPaths : IApplicationPaths
{
    public string DataDirectory { get; }
    public PortableApplicationPaths(string? applicationDirectory = null) => DataDirectory = Path.Combine(applicationDirectory ?? AppContext.BaseDirectory, "data");
}

public static class AtomicFile
{
    public static async Task WriteAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken token = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
                stream.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            if (File.Exists(path)) File.Move(temporary, path, true); else File.Move(temporary, path);
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } }
    }
}

public sealed class SettingsService(IApplicationPaths paths)
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    public string SettingsPath => Path.Combine(paths.DataDirectory, "settings.json");
    public ApplicationSettings Load(out string? warning)
    {
        warning = null;
        var settings = new ApplicationSettings();
        if (!File.Exists(SettingsPath)) return settings;
        try
        {
            if (new FileInfo(SettingsPath).Length > 1024 * 1024) throw new JsonException("Settings too large.");
            using var document = JsonDocument.Parse(File.ReadAllBytes(SettingsPath));
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            foreach (var property in typeof(ApplicationSettings).GetProperties().Where(p => p.CanWrite))
            {
                var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
                if (!document.RootElement.TryGetProperty(name, out var value)) continue;
                if (value.ValueKind == JsonValueKind.Null && property.Name != nameof(ApplicationSettings.LastProjectPath))
                { warning = "Settings.InvalidFields"; continue; }
                try { property.SetValue(settings, value.Deserialize(property.PropertyType, JsonOptions)); }
                catch (Exception e) when (e is JsonException or ArgumentException or System.Reflection.TargetInvocationException)
                { warning = "Settings.InvalidFields"; }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        { warning = "Settings.Unreadable"; }
        return settings;
    }
    public async Task SaveAsync(byte[] snapshot, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { await AtomicFile.WriteAsync(SettingsPath, snapshot, token).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    public Task SaveAsync(ApplicationSettings settings, CancellationToken token = default) => SaveAsync(JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions), token);
}
