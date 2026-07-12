using System.Text.Json;
using System.IO;

namespace AstroForge.App.Services;

public sealed class AstroForgeProjectDocument
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public string ProjectName { get; set; } = "";
    public List<string> SourcePaths { get; set; } = [];
    public string LibraryPath { get; set; } = "";
    public string DestinationPath { get; set; } = "";
    public int SessionBoundaryHour { get; set; } = 12;
    public double? DefaultGain { get; set; }
    public double? DefaultOffset { get; set; }
    public double? DefaultTemperatureC { get; set; }
    public Dictionary<string, FrameOverrides> Overrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class ProjectDocumentStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static AstroForgeProjectDocument Load(string path)
    {
        var document = JsonSerializer.Deserialize<AstroForgeProjectDocument>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("Il file progetto è vuoto o non valido.");
        if (document.SchemaVersion != 1) throw new InvalidDataException($"Versione progetto non supportata: {document.SchemaVersion}.");
        return document;
    }
    public static void Save(string path, AstroForgeProjectDocument document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        document.UpdatedAt = DateTimeOffset.Now;
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(document, Options));
        File.Move(temporary, path, true);
    }
}
