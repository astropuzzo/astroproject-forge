using System.Text.Json;

namespace AstroForge.Core.Diagnostics;

public sealed record RecoveryJournalEntry<TSnapshot>(
    int SchemaVersion,
    string OperationId,
    string Operation,
    DateTimeOffset StartedAtUtc,
    TSnapshot Snapshot);

public sealed class RecoveryJournalStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public RecoveryJournalStore(string? path = null)
    {
        _path = path ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AstroProjectForge", "Recovery", "current.json");
    }

    public string FilePath => _path;

    public RecoveryJournalEntry<TSnapshot> Begin<TSnapshot>(string operation, TSnapshot snapshot, string? operationId = null)
    {
        var entry = new RecoveryJournalEntry<TSnapshot>(1, operationId ?? Guid.NewGuid().ToString("N"), operation, DateTimeOffset.UtcNow, snapshot);
        lock (_gate)
        {
            var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_path))!;
            Directory.CreateDirectory(directory);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(entry, _json));
            File.Move(temporary, _path, true);
        }
        return entry;
    }

    public RecoveryJournalEntry<TSnapshot>? Read<TSnapshot>()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return null;
            try
            {
                var entry = JsonSerializer.Deserialize<RecoveryJournalEntry<TSnapshot>>(File.ReadAllText(_path), _json);
                return entry?.SchemaVersion == 1 ? entry : null;
            }
            catch (JsonException) { return null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }

    public bool Complete(string operationId)
    {
        lock (_gate)
        {
            try
            {
                var current = ReadIdentifier();
                if (current is null || !current.Equals(operationId, StringComparison.Ordinal)) return false;
                File.Delete(_path);
                var temporary = _path + ".tmp";
                if (File.Exists(temporary)) File.Delete(temporary);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    public void Discard()
    {
        lock (_gate)
        {
            if (File.Exists(_path)) File.Delete(_path);
            var temporary = _path + ".tmp";
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private string? ReadIdentifier()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            return document.RootElement.TryGetProperty("operationId", out var value) ? value.GetString() : null;
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
