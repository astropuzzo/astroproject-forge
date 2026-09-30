using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AstroForge.Core.Filters;

public enum FilterKind
{
    Unknown,
    None,
    Broadband,
    LightPollution,
    Multiband,
    Narrowband
}

public sealed record FilterBand(double FromNm, double ToNm, double Peak)
{
    public double WidthNm => ToNm - FromNm;
    public bool Contains(double wavelengthNm) => wavelengthNm >= FromNm - 0.5 && wavelengthNm <= ToNm + 0.5;
}

public sealed record EmissionLine(string Name, double WavelengthNm);

public sealed record CatalogFilter(
    string Id,
    string Brand,
    string Name,
    string? Series,
    FilterKind Kind,
    string? Channel,
    string Camera,
    IReadOnlyList<FilterBand> Bands,
    bool Approximate,
    string? Source)
{
    public string DisplayName => Brand == "Generico" ? Name : $"{Brand} {Name}";

    /// <summary>Emission lines that fall inside the declared passbands (only meaningful for narrow and multi band filters).</summary>
    /// NII sits 2 nm from Hα and rides along in any Hα band, so it is listed only for filters built around it.
    public IReadOnlyList<EmissionLine> Lines => _lines ??= Kind is FilterKind.Narrowband or FilterKind.Multiband
        ? EmissionLines.All.Where(line => Bands.Any(band => band.Contains(line.WavelengthNm)))
            .Where((line, _) => line != EmissionLines.Nii || !Bands.Any(band => band.Contains(EmissionLines.Ha.WavelengthNm))).ToArray()
        : [];
    private IReadOnlyList<EmissionLine>? _lines;

    /// <summary>Widest passband, used to tell apart products of the same family (3 nm vs 7 nm).</summary>
    public double? BandwidthNm => Kind is FilterKind.Narrowband or FilterKind.Multiband && Bands.Count > 0 ? Bands.Max(band => band.WidthNm) : null;
}

public static class EmissionLines
{
    public static readonly EmissionLine Ha = new("Hα", 656.3);
    public static readonly EmissionLine Oiii = new("OIII", 500.7);
    public static readonly EmissionLine Sii = new("SII", 671.6);
    public static readonly EmissionLine Hb = new("Hβ", 486.1);
    public static readonly EmissionLine Nii = new("NII", 658.4);
    public static readonly IReadOnlyList<EmissionLine> All = [Hb, Oiii, Ha, Nii, Sii];
}

/// <summary>
/// Catalogue of commercial astronomy filters with the passbands declared by their makers.
/// The data ships as an embedded JSON resource (assets/catalog/filters.json) so it can be
/// refreshed without touching code.
/// </summary>
public sealed class FilterCatalog
{
    private static readonly Lazy<FilterCatalog> DefaultCatalog = new(LoadEmbedded);
    private readonly Dictionary<string, CatalogFilter> _byId;

    public FilterCatalog(IEnumerable<CatalogFilter> filters)
    {
        Filters = filters.ToArray();
        _byId = Filters.ToDictionary(filter => filter.Id, StringComparer.OrdinalIgnoreCase);
    }

    public static FilterCatalog Default => DefaultCatalog.Value;
    public IReadOnlyList<CatalogFilter> Filters { get; }
    public CatalogFilter? Find(string? id) => id is not null && _byId.TryGetValue(id, out var filter) ? filter : null;

    public static FilterCatalog Parse(string json)
    {
        var document = JsonSerializer.Deserialize<CatalogDocument>(json, JsonOptions) ?? throw new InvalidDataException("Catalogo filtri vuoto.");
        if (document.Schema != 1) throw new InvalidDataException($"Schema catalogo filtri non supportato: {document.Schema}.");
        return new(document.Filters.Select(item => new CatalogFilter(
            item.Id, item.Brand, item.Name, item.Series, ParseKind(item.Kind), item.Channel, item.Camera ?? "both",
            item.Bands.Select(band => new FilterBand(band.FromNm, band.ToNm, band.Peak)).ToArray(), item.Approximate, item.Source)));
    }

    private static FilterCatalog LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AstroForge.Catalog.filters.json")
            ?? throw new InvalidOperationException("Catalogo filtri non incluso nell'assembly.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private static FilterKind ParseKind(string? value) => value switch
    {
        "broadband" => FilterKind.Broadband,
        "lightPollution" => FilterKind.LightPollution,
        "multiband" => FilterKind.Multiband,
        "narrowband" => FilterKind.Narrowband,
        _ => FilterKind.Unknown
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record CatalogDocument(int Schema, List<CatalogItem> Filters);
    private sealed record CatalogItem(
        string Id, string Brand, string Name, string? Series, string? Kind, string? Channel, string? Camera,
        List<CatalogBand> Bands, bool Approximate, string? Source);
    private sealed record CatalogBand([property: JsonPropertyName("fromNm")] double FromNm, [property: JsonPropertyName("toNm")] double ToNm, double Peak);
}
