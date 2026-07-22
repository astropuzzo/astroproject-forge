using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text.Json;
using AstroForge.Core.Scanning;
using AstroForge.Core.IO;

namespace AstroForge.App.Services;

public sealed class JsonHeaderCache : IHeaderCache
{
    private sealed record Entry(long Length, long LastWriteUtcTicks, Dictionary<string, string?> Headers);
    private readonly string _path;
    private readonly ConcurrentDictionary<string, Entry> _entries;
    private int _dirty;

    public JsonHeaderCache(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AstroProjectForge", "header-cache-v1.json");
        _entries = new(Load(_path), PathIdentity.Comparer);
    }

    public bool TryGet(string path, long length, long lastWriteUtcTicks, out Dictionary<string, object?> headers)
    {
        if (_entries.TryGetValue(Path.GetFullPath(path), out var entry) && entry.Length == length && entry.LastWriteUtcTicks == lastWriteUtcTicks)
        {
            headers = entry.Headers.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.OrdinalIgnoreCase);
            return true;
        }
        headers = [];
        return false;
    }

    public void Put(string path, long length, long lastWriteUtcTicks, Dictionary<string, object?> headers)
    {
        var normalized = headers.ToDictionary(pair => pair.Key, pair => Normalize(pair.Value), StringComparer.OrdinalIgnoreCase);
        _entries[Path.GetFullPath(path)] = new(length, lastWriteUtcTicks, normalized);
        Interlocked.Exchange(ref _dirty, 1);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _dirty, 0) == 0) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = false }), cancellationToken);
        File.Move(temporary, _path, true);
    }

    public void Clear()
    {
        _entries.Clear();
        if (File.Exists(_path)) File.Delete(_path);
    }

    private static Dictionary<string, Entry> Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path)) ?? [] : []; }
        catch { return []; }
    }

    private static string? Normalize(object? value) => value switch
    {
        null => null,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };
}
