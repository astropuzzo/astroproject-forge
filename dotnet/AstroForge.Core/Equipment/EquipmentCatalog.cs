using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AstroForge.Core.Equipment;

public enum CameraSensorType
{
    Unknown,
    Mono,
    Color,
    Dslr,
    DslrModified
}

public sealed record CatalogCamera(
    string Id,
    string Name,
    CameraSensorType Type,
    string? Sensor,
    double? PixelUm,
    int? Width,
    int? Height,
    int? QePercent,
    double? ReadNoise,
    int Uses)
{
    public IReadOnlyList<string> Aliases { get; init; } = [];
}

public sealed record TelescopeReducer(string Name, double Factor);

public sealed record CatalogTelescope(
    string Id,
    string Name,
    double ApertureMm,
    double FocalMm,
    double ObstructionPct,
    IReadOnlyList<TelescopeReducer> Reducers,
    int Uses)
{
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public double FocalRatio => FocalMm / ApertureMm;
}

/// <summary>
/// Cameras and telescopes with the specs Forge needs (sensor type, pixel size, focal length, reducers). The data ships as
/// an embedded JSON resource (assets/catalog/equipment.json) built by scripts/import-skyframe-equipment.mjs.
/// </summary>
public sealed class EquipmentCatalog
{
    private static readonly Lazy<EquipmentCatalog> DefaultCatalog = new(LoadEmbedded);

    public EquipmentCatalog(IEnumerable<CatalogCamera> cameras, IEnumerable<CatalogTelescope> telescopes)
    {
        Cameras = cameras.ToArray();
        Telescopes = telescopes.ToArray();
    }

    public static EquipmentCatalog Default => DefaultCatalog.Value;
    public IReadOnlyList<CatalogCamera> Cameras { get; }
    public IReadOnlyList<CatalogTelescope> Telescopes { get; }
    public CatalogCamera? FindCamera(string? id) => Cameras.FirstOrDefault(camera => string.Equals(camera.Id, id, StringComparison.OrdinalIgnoreCase));
    public CatalogTelescope? FindTelescope(string? id) => Telescopes.FirstOrDefault(telescope => string.Equals(telescope.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Reducers and flatteners that fit a telescope, including those listed under a twin entry with the same optics.</summary>
    public IReadOnlyList<TelescopeReducer> ReducersFor(CatalogTelescope telescope) =>
        Telescopes.Where(item => item.ApertureMm == telescope.ApertureMm && item.FocalMm == telescope.FocalMm)
            .SelectMany(item => item.Reducers).DistinctBy(item => item.Factor).OrderBy(item => item.Factor).ToArray();

    /// <summary>Telescopes for a picker, most used first.</summary>
    public IEnumerable<CatalogTelescope> TelescopesByUse => Telescopes.OrderByDescending(item => item.Uses).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase);

    public static EquipmentCatalog Parse(string json)
    {
        var document = JsonSerializer.Deserialize<CatalogDocument>(json, JsonOptions) ?? throw new InvalidDataException("Catalogo attrezzatura vuoto.");
        if (document.Schema != 1) throw new InvalidDataException($"Schema catalogo attrezzatura non supportato: {document.Schema}.");
        return new(
            document.Cameras.Select(item => new CatalogCamera(item.Id, item.Name, ParseType(item.Type), item.Sensor, item.PixelUm, item.Width, item.Height, item.Qe, item.ReadNoise, item.Uses)
            {
                Aliases = item.Aliases ?? []
            }),
            document.Telescopes.Select(item => new CatalogTelescope(item.Id, item.Name, item.ApertureMm, item.FocalMm, item.ObstructionPct,
                (item.Reducers ?? []).Select(reducer => new TelescopeReducer(reducer.Name, reducer.Factor)).ToArray(), item.Uses)
            {
                Aliases = item.Aliases ?? []
            }));
    }

    private static EquipmentCatalog LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AstroForge.Catalog.equipment.json")
            ?? throw new InvalidOperationException("Catalogo attrezzatura non incluso nell'assembly.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private static CameraSensorType ParseType(string? value) => value switch
    {
        "mono" => CameraSensorType.Mono,
        "color" => CameraSensorType.Color,
        "dslr" => CameraSensorType.Dslr,
        "dslrModified" => CameraSensorType.DslrModified,
        _ => CameraSensorType.Unknown
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record CatalogDocument(int Schema, List<CameraItem> Cameras, List<TelescopeItem> Telescopes);
    private sealed record CameraItem(
        string Id, string Name, string? Type, string? Sensor, double? PixelUm, int? Width, int? Height, int? Qe, double? ReadNoise, int Uses, List<string>? Aliases);
    private sealed record TelescopeItem(
        string Id, string Name, double ApertureMm, double FocalMm, double ObstructionPct, List<ReducerItem>? Reducers, int Uses, List<string>? Aliases);
    private sealed record ReducerItem([property: JsonPropertyName("name")] string Name, double Factor);
}
