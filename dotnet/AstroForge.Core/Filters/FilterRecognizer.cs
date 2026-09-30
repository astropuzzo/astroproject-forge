using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AstroForge.Core.Filters;

public enum FilterMatchSource
{
    Unknown,
    UserProfile,
    CatalogProduct,
    GenericName,
    NoFilter
}

/// <summary>What Forge believes a raw filter name (from a header or file name) physically is.</summary>
public sealed record FilterIdentity(
    string RawName,
    FilterKind Kind,
    IReadOnlyList<EmissionLine> Lines,
    CatalogFilter? Product,
    double? BandwidthNm,
    double Confidence,
    FilterMatchSource Source,
    string DisplayName)
{
    public bool IsRecognized => Source != FilterMatchSource.Unknown;
    public bool NeedsConfirmation => Source == FilterMatchSource.Unknown || Confidence < 0.75;
}

/// <summary>
/// Maps free-form filter names (N.I.N.A. wheel slots, ASIAIR file-name tokens, SharpCap, Voyager…) to
/// catalogue products or to a generic filter class. Order of authority: the user's wheel profile, a
/// catalogue product, a generic name ("Ha 7nm", "HOO", "L"), then the sensor context (colour camera
/// with no filter). Names it cannot place stay Unknown so the UI can ask once.
/// </summary>
public static partial class FilterRecognizer
{
    private static readonly ConcurrentDictionary<(string, bool, FilterCatalog), FilterIdentity> Cache = new();
    private static readonly Dictionary<string, string> Abbreviations = new(StringComparer.Ordinal)
    {
        ["lext"] = "lextreme", ["lenh"] = "lenhance", ["lult"] = "lultimate", ["lsyn"] = "lsynergy",
        ["lenhanced"] = "lenhance", ["lextream"] = "lextreme"
    };
    private static readonly HashSet<string> NoFilterNames = new(StringComparer.Ordinal)
    {
        "none", "nofilter", "no filter", "nessuno", "nessun filtro", "senza filtro", "empty", "vuoto", "open", "clear", "dark", "blank"
    };

    public static FilterIdentity Recognize(string? rawName, bool colorSensor = false, IReadOnlyDictionary<string, string>? wheelProfile = null, FilterCatalog? catalog = null)
    {
        catalog ??= FilterCatalog.Default;
        var raw = (rawName ?? "").Trim();
        if (wheelProfile is not null && raw.Length > 0 && wheelProfile.TryGetValue(Normalize(raw), out var profileId) && catalog.Find(profileId) is { } chosen)
            return FromProduct(raw, chosen, 1, FilterMatchSource.UserProfile);
        return Cache.GetOrAdd((raw, colorSensor, catalog), key => RecognizeCore(key.Item1, key.Item2, key.Item3));
    }

    /// <summary>Normalized key under which a raw name is stored in a wheel profile.</summary>
    public static string Normalize(string raw)
    {
        var text = raw.Trim().ToLowerInvariant()
            .Replace("α", "a").Replace("β", "b").Replace("ɑ", "a");
        text = RemoveDiacritics(text);
        text = SeparatorRegex().Replace(text, " ");
        return string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static FilterIdentity RecognizeCore(string raw, bool colorSensor, FilterCatalog catalog)
    {
        var normalized = Normalize(raw);
        if (normalized.Length == 0 || NoFilterNames.Contains(normalized))
        {
            return colorSensor || normalized.Length > 0
                ? new(raw, FilterKind.None, [], null, null, normalized.Length == 0 ? 0.85 : 0.9, FilterMatchSource.NoFilter, colorSensor ? "Nessun filtro (OSC)" : "Nessun filtro")
                : Unknown(raw);
        }

        var bandwidth = Bandwidth(normalized);
        var channels = Channels(normalized);
        var product = MatchProduct(normalized, bandwidth, channels, catalog);
        if (product is not null) return product with { RawName = raw };

        return Generic(raw, normalized, bandwidth, channels) ?? Unknown(raw);
    }

    private static FilterIdentity? MatchProduct(string normalized, double? bandwidth, IReadOnlyList<EmissionLine> channels, FilterCatalog catalog)
    {
        var compact = Compact(normalized);
        foreach (var (shortForm, full) in Abbreviations)
            if (compact == shortForm || compact.StartsWith(shortForm, StringComparison.Ordinal) && !compact.StartsWith(full, StringComparison.Ordinal))
                compact = full + compact[shortForm.Length..];

        var candidates = new List<(CatalogFilter Filter, bool BrandMatched)>();
        foreach (var filter in catalog.Filters)
        {
            var brandMatched = filter.Brand != "Generico" && compact.Contains(Compact(Normalize(filter.Brand)), StringComparison.Ordinal);
            if (filter.Kind == FilterKind.Narrowband && filter.Series is not null)
            {
                // A single-line filter from a branded set ("Baader Ha 6.5nm") needs the brand and the line.
                if (brandMatched && channels.Count == 1 && filter.Lines.Contains(channels[0])) candidates.Add((filter, true));
                continue;
            }
            if (filter.Brand == "Generico") continue;
            var stem = Stem(filter.Name);
            var matches = stem.Length >= 4 ? compact.Contains(stem, StringComparison.Ordinal) : Tokens(normalized).Contains(stem);
            if (matches) candidates.Add((filter, brandMatched));
        }
        if (candidates.Count == 0) return null;

        IEnumerable<(CatalogFilter Filter, bool BrandMatched)> narrowed = candidates;
        if (bandwidth is { } width)
        {
            var byWidth = narrowed.Where(item => item.Filter.BandwidthNm is { } w && Math.Abs(w - width) < 0.6).ToArray();
            if (byWidth.Length > 0) narrowed = byWidth;
        }
        if (channels.Count > 0)
        {
            var byLines = narrowed.Where(item => channels.All(item.Filter.Lines.Contains)).ToArray();
            if (byLines.Length > 0) narrowed = byLines;
        }
        var remaining = narrowed.ToArray();
        if (remaining.Length > 1)
        {
            var exact = remaining.Where(item => Compact(Normalize(item.Filter.Name)) == compact || Compact(Normalize(item.Filter.DisplayName)) == compact).ToArray();
            if (exact.Length == 1) remaining = exact;
        }
        if (remaining.Length > 1)
        {
            // Prefer the base model when the name gives no version hint ("L-eXtreme" over a variant).
            var shortest = remaining.MinBy(item => item.Filter.Name.Length);
            var sameFamily = remaining.All(item => item.Filter.Kind == shortest.Filter.Kind && item.Filter.Lines.SequenceEqual(shortest.Filter.Lines));
            if (!sameFamily) return null;
            return FromProduct("", shortest.Filter, 0.6, FilterMatchSource.CatalogProduct);
        }
        var match = remaining[0];
        return FromProduct("", match.Filter, match.BrandMatched ? 0.97 : 0.9, FilterMatchSource.CatalogProduct);
    }

    private static FilterIdentity? Generic(string raw, string normalized, double? bandwidth, IReadOnlyList<EmissionLine> channels)
    {
        var tokens = Tokens(normalized);
        var compact = Compact(normalized);
        if (compact is "uvir" or "uvircut" or "ircut" or "uvirblock" or "ir" || normalized.Contains("uv ir") || normalized.Contains("ir cut"))
            return new(raw, FilterKind.Broadband, [], null, null, 0.85, FilterMatchSource.GenericName, "UV/IR cut");
        if (tokens.Intersect(["lp", "cls", "lps", "skyglow", "antinquinamento"]).Any() || normalized.Contains("light pollution"))
            return new(raw, FilterKind.LightPollution, [], null, null, 0.75, FilterMatchSource.GenericName, "Anti-inquinamento luminoso");

        var palette = compact switch
        {
            "hoo" or "ho" or "hao3" or "hoiii" or "dual" or "duo" or "dualband" or "duoband" or "db" => new[] { EmissionLines.Ha, EmissionLines.Oiii },
            "so" or "s2o3" or "siioiii" or "soo" => [EmissionLines.Sii, EmissionLines.Oiii],
            "tri" or "triband" => [EmissionLines.Ha, EmissionLines.Oiii, EmissionLines.Sii],
            _ => null
        };
        var lines = palette ?? channels.ToArray();
        if (lines.Length == 1)
            return new(raw, FilterKind.Narrowband, lines, null, bandwidth, 0.85, FilterMatchSource.GenericName,
                bandwidth is { } w ? $"{lines[0].Name} {w.ToString("0.#", CultureInfo.GetCultureInfo("it-IT"))} nm" : lines[0].Name);
        if (lines.Length > 1)
            return new(raw, FilterKind.Multiband, lines, null, bandwidth, palette is null ? 0.8 : 0.75, FilterMatchSource.GenericName,
                "Multibanda " + string.Join(" + ", lines.Select(line => line.Name)));

        var broadband = compact switch
        {
            "l" or "lum" or "luminance" or "luminanza" or "lrgbl" => "L",
            "r" or "red" or "rosso" => "R",
            "g" or "green" or "verde" => "G",
            "b" or "blue" or "blu" => "B",
            _ => null
        };
        return broadband is null ? null : new(raw, FilterKind.Broadband, [], null, null, 0.9, FilterMatchSource.GenericName, broadband);
    }

    private static IReadOnlyList<EmissionLine> Channels(string normalized)
    {
        var result = new List<EmissionLine>();
        var spaced = " " + normalized + " ";
        if (HaRegex().IsMatch(spaced)) result.Add(EmissionLines.Ha);
        if (OiiiRegex().IsMatch(spaced)) result.Add(EmissionLines.Oiii);
        if (SiiRegex().IsMatch(spaced)) result.Add(EmissionLines.Sii);
        if (HbRegex().IsMatch(spaced)) result.Add(EmissionLines.Hb);
        if (NiiRegex().IsMatch(spaced)) result.Add(EmissionLines.Nii);
        return result;
    }

    private static double? Bandwidth(string normalized)
    {
        var match = BandwidthRegex().Match(normalized);
        return match.Success && double.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static FilterIdentity FromProduct(string raw, CatalogFilter filter, double confidence, FilterMatchSource source) =>
        new(raw, filter.Kind, filter.Lines, filter, filter.BandwidthNm, confidence, source, filter.DisplayName);

    private static FilterIdentity Unknown(string raw) => new(raw, FilterKind.Unknown, [], null, null, 0, FilterMatchSource.Unknown, raw.Length == 0 ? "Filtro sconosciuto" : raw);

    private static string Stem(string name)
    {
        var withoutNote = name.Split('(')[0];
        var compact = Compact(Normalize(withoutNote));
        var digit = compact.IndexOfAny("0123456789".ToCharArray());
        return digit > 2 ? compact[..digit] : compact;
    }

    private static string Compact(string normalized) => normalized.Replace(" ", "");
    private static HashSet<string> Tokens(string normalized) => normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

    private static string RemoveDiacritics(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark) builder.Append(character);
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"[\s_\-./\\+:;,()\[\]]+")]
    private static partial Regex SeparatorRegex();
    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*nm")]
    private static partial Regex BandwidthRegex();
    [GeneratedRegex(@"(?<![a-z])(ha|h a|halpha|h alpha|hydrogen alpha|idrogeno alfa)(?![a-z])|(?<![a-z])ha\d")]
    private static partial Regex HaRegex();
    [GeneratedRegex(@"(?<![a-z])(oiii|o iii|o3|oxygen|ossigeno)(?![a-z])|(?<![a-z])(oiii|o3)\d")]
    private static partial Regex OiiiRegex();
    [GeneratedRegex(@"(?<![a-z])(sii|s ii|s2|sulfur|sulphur|zolfo)(?![a-z])|(?<![a-z])(sii|s2)\d")]
    private static partial Regex SiiRegex();
    [GeneratedRegex(@"(?<![a-z])(hb|h b|hbeta|h beta)(?![a-z])")]
    private static partial Regex HbRegex();
    [GeneratedRegex(@"(?<![a-z])(nii|n ii|n2)(?![a-z])")]
    private static partial Regex NiiRegex();
}
