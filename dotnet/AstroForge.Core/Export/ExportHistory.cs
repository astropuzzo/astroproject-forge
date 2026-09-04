using System.Text;
using System.Text.Json;

namespace AstroForge.Core.Export;

public sealed record ExportHistoryGroup(
    string Filter,
    string Session,
    int LightCount,
    double IntegrationSeconds,
    int CalibrationCount);

public sealed record ExportHistoryEntry(
    string Id,
    DateTimeOffset CreatedAtUtc,
    string Mode,
    int AddedFiles,
    int UnchangedFiles,
    long AddedBytes,
    double AddedIntegrationSeconds,
    IReadOnlyList<ExportHistoryGroup> Groups,
    IReadOnlyList<string> AddedPaths);

public sealed record ExportHistoryDocument(
    int Schema,
    string Product,
    string ProjectName,
    IReadOnlyList<ExportHistoryEntry> Entries);

public static class ExportHistoryStore
{
    public const string JsonFileName = "export-history.json";
    public const string MarkdownFileName = "export-history.md";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static ExportHistoryDocument Read(string projectRoot)
    {
        var path = Path.Combine(projectRoot, "_AstroForge", JsonFileName);
        if (!File.Exists(path)) return new(1, "AstroProject Forge", Path.GetFileName(projectRoot), []);
        try
        {
            return JsonSerializer.Deserialize<ExportHistoryDocument>(File.ReadAllText(path), Json)
                ?? new(1, "AstroProject Forge", Path.GetFileName(projectRoot), []);
        }
        catch (JsonException)
        {
            return new(1, "AstroProject Forge", Path.GetFileName(projectRoot), []);
        }
    }

    public static ExportHistoryEntry CreateEntry(ProjectPlan plan, IReadOnlyCollection<PlannedFile> added, int unchangedFiles, string mode)
    {
        var groups = added
            .GroupBy(item => new
            {
                Filter = item.Frame.FilterName.Value ?? "Senza filtro",
                Session = item.Frame.SessionId.Value ?? "Senza sessione"
            })
            .Select(group => new ExportHistoryGroup(
                group.Key.Filter,
                group.Key.Session,
                group.Count(item => item.Role == "light"),
                group.Where(item => item.Role == "light").Sum(item => item.Frame.ExposureSeconds.Value ?? 0),
                group.Count(item => item.Role is "flat" or "dark" or "bias")))
            .OrderBy(group => group.Filter, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Session, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            mode,
            added.Count,
            unchangedFiles,
            added.Sum(item => new FileInfo(item.Frame.Path).Length),
            groups.Sum(group => group.IntegrationSeconds),
            groups,
            added.Select(item => item.RelativePath.Replace('\\', '/')).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static async Task AppendAsync(string projectRoot, string projectName, params ExportHistoryEntry[] entries)
    {
        if (entries.Length == 0) return;
        var existing = Read(projectRoot);
        var document = new ExportHistoryDocument(1, "AstroProject Forge", projectName, existing.Entries.Concat(entries).ToArray());
        var control = Path.Combine(projectRoot, "_AstroForge");
        Directory.CreateDirectory(control);
        await WriteAtomicAsync(Path.Combine(control, JsonFileName), JsonSerializer.Serialize(document, Json));
        await WriteAtomicAsync(Path.Combine(control, MarkdownFileName), Markdown(document));
    }

    private static string Markdown(ExportHistoryDocument document)
    {
        var text = new StringBuilder($"# Cronologia dati — {document.ProjectName}\n\n");
        var cumulative = new Dictionary<string, double>(StringComparer.Ordinal);
        var runningSeconds = 0d;
        foreach (var entry in document.Entries.OrderBy(item => item.CreatedAtUtc))
        {
            runningSeconds += entry.AddedIntegrationSeconds;
            cumulative[entry.Id] = runningSeconds;
        }
        foreach (var entry in document.Entries.OrderByDescending(item => item.CreatedAtUtc))
        {
            text.AppendLine($"## {entry.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} — {(entry.Mode == "initial" ? "Prima esportazione" : entry.Mode == "baseline" ? "Dati precedenti" : "Aggiornamento")}");
            text.AppendLine();
            text.AppendLine($"- {entry.AddedFiles} file aggiunti · {Duration(entry.AddedIntegrationSeconds)} di integrazione · {entry.UnchangedFiles} invariati · totale {Duration(cumulative[entry.Id])}");
            foreach (var group in entry.Groups)
                text.AppendLine($"- {group.Filter} / {group.Session}: {group.LightCount} Light, {Duration(group.IntegrationSeconds)}, {group.CalibrationCount} calibrazioni");
            text.AppendLine();
        }
        return text.ToString();
    }

    private static string Duration(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes:00} min" : $"{span.Minutes} min";
    }

    private static async Task WriteAtomicAsync(string path, string content)
    {
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }
}
