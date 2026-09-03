using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using AstroForge.Core.IO;

namespace AstroForge.App.Services;

public sealed class AstroForgeProjectDocument
{
    public int SchemaVersion { get; set; } = 2;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public string ProjectName { get; set; } = "";
    public List<string> SourcePaths { get; set; } = [];
    // Schema 1 compatibility only. Calibration libraries are application settings
    // and are intentionally omitted from new project documents.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LibraryPath { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<MasterLibraryDefinition>? MasterLibraries { get; set; }
    public string DestinationPath { get; set; } = "";
    public int SessionBoundaryHour { get; set; } = 12;
    public double? DefaultGain { get; set; }
    public double? DefaultOffset { get; set; }
    public double? DefaultTemperatureC { get; set; }
    public double QualitySigmaThreshold { get; set; } = 3.5;
    public List<string> ExcludedQualityPaths { get; set; } = [];
    public Dictionary<string, FrameOverrides> Overrides { get; set; } = new(PathIdentity.Comparer);
}

public sealed class ProjectRecoverySnapshot
{
    public string ProjectFile { get; set; } = "";
    public AstroForgeProjectDocument Document { get; set; } = new();
}

public static class ProjectDocumentStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static AstroForgeProjectDocument Load(string path)
    {
        var document = JsonSerializer.Deserialize<AstroForgeProjectDocument>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("Il file progetto è vuoto o non valido.");
        if (document.SchemaVersion is < 1 or > 2) throw new InvalidDataException($"Versione progetto non supportata: {document.SchemaVersion}.");
        document.Overrides = new(document.Overrides, PathIdentity.Comparer);
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
