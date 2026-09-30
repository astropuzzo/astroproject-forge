using System.Text.Json.Serialization;

namespace AstroForge.Core.Equipment;

/// <summary>Where an instrument value comes from: the FITS headers, the equipment catalogue, or the user's own profile.</summary>
public enum EquipmentSource
{
    Header,
    Catalog,
    User
}

/// <summary>
/// What the user says their train really is, stored per camera key next to the filter wheel profile. Every field is
/// optional: null keeps what Forge detected. A catalogue <see cref="TelescopeId"/> names the optics; <see cref="TelescopeName"/>,
/// <see cref="ApertureMm"/> and <see cref="FocalMm"/> describe optics the catalogue lacks, or correct its values.
/// </summary>
public sealed record EquipmentOverride
{
    /// <summary>Catalogue telescope id (<see cref="CatalogTelescope.Id"/>).</summary>
    public string? TelescopeId { get; init; }
    /// <summary>Free name for optics the catalogue does not list.</summary>
    public string? TelescopeName { get; init; }
    public double? ApertureMm { get; init; }
    /// <summary>Native focal length, without reducer; wins over the catalogue value when both are set.</summary>
    public double? FocalMm { get; init; }
    /// <summary>Reducer or barlow factor on the native focal (0.8 = reducer, 1 = none); null keeps the detected one.</summary>
    public double? ReducerFactor { get; init; }
    /// <summary>Native (unbinned) pixel pitch in µm.</summary>
    public double? PixelUm { get; init; }

    /// <summary>True when the user named the optics or gave their aperture or focal length.</summary>
    [JsonIgnore]
    public bool HasTelescope => !string.IsNullOrWhiteSpace(TelescopeId) || !string.IsNullOrWhiteSpace(TelescopeName) || ApertureMm is > 0 || FocalMm is > 0;

    [JsonIgnore]
    public bool IsEmpty => !HasTelescope && ReducerFactor is not > 0 && PixelUm is not > 0;

    /// <summary>A case-insensitive copy of a deserialized profile map, dropping empty entries; null (an older state file) gives an empty map.</summary>
    public static Dictionary<string, EquipmentOverride> Normalize(IEnumerable<KeyValuePair<string, EquipmentOverride>>? profiles)
    {
        var result = new Dictionary<string, EquipmentOverride>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in profiles ?? [])
            if (!string.IsNullOrWhiteSpace(key) && value is { IsEmpty: false }) result[key] = value;
        return result;
    }
}
