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

        // Read from the raw text: normalizing turns the decimal point of "6.5nm" into a separator.
        var bandwidth = Bandwidth(raw.ToLowerInvariant());
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

        var colour = channels.Count == 0 ? BroadbandColour(normalized) : null;
        var candidates = new List<(CatalogFilter Filter, bool BrandMatched, int Specificity)>();
        foreach (var filter in catalog.Filters)
        {
            var brandMatched = filter.Brand != "Generico" && compact.Contains(Compact(Normalize(filter.Brand)), StringComparison.Ordinal);
            if (filter.Kind == FilterKind.Narrowband && filter.Series is not null)
            {
                // A single-line filter from a branded set ("Baader Ha 6.5nm") needs the brand and the line.
                if (brandMatched && channels.Count == 1 && filter.Lines.Contains(channels[0])) candidates.Add((filter, true, 0));
                continue;
            }
            if (filter.Kind == FilterKind.Broadband && filter.Series is not null)
            {
                // An LRGB set needs the brand, the colour and a word of the set ("CMOS"): "Baader Red (R-CCD)" is another set.
                if (brandMatched && colour == filter.Channel && filter.Aliases.Any(alias => ContainsAlias(normalized, compact, Normalize(alias))))
                    candidates.Add((filter, true, 0));
                continue;
            }
            if (filter.Brand == "Generico") continue;
            var keys = Keys(filter);
            // A model name of three letters or fewer ("CLS", "UHC") is a generic word too: "SVBony CLS" is not the Astronomik one.
            if (!keys.Stems.Any(stem => stem.Length >= 4 ? compact.Contains(stem, StringComparison.Ordinal) : brandMatched && Tokens(normalized).Contains(stem))
                && !keys.Aliases.Any(alias => ContainsAlias(normalized, compact, alias))) continue;
            var specificity = keys.Full.Where(key => compact.Contains(key, StringComparison.Ordinal)).Select(key => key.Length).DefaultIfEmpty(0).Max();
            candidates.Add((filter, brandMatched, specificity));
        }
        if (candidates.Count == 0) return null;

        IEnumerable<(CatalogFilter Filter, bool BrandMatched, int Specificity)> narrowed = candidates;
        if (candidates.Any(item => item.BrandMatched)) narrowed = candidates.Where(item => item.BrandMatched).ToArray();
        if (bandwidth is { } width)
        {
            // Closest declared width wins, so "3nm" picks the 3 nm model over the 3.5 nm one.
            var byWidth = narrowed.Where(item => item.Filter.BandwidthNm is { } w && Math.Abs(w - width) < 0.6).ToArray();
            if (byWidth.Length > 0)
            {
                var closest = byWidth.Min(item => Math.Abs(item.Filter.BandwidthNm!.Value - width));
                narrowed = byWidth.Where(item => Math.Abs(item.Filter.BandwidthNm!.Value - width) - closest < 0.05).ToArray();
            }
            // "Baader S-II 8nm" is not the 6.5 nm model of the set: better a generic SII 8 nm than the wrong product.
            else if (narrowed.All(item => item.Filter.Series is not null)) return null;
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
            // The longest full model name spelled out wins: "NBZ" inside "Nebula Booster NBZ" beats "Nebula Booster NB1".
            var best = remaining.Max(item => item.Specificity);
            var mostSpecific = remaining.Where(item => item.Specificity == best).ToArray();
            if (best > 0 && mostSpecific.Length == 1) remaining = mostSpecific;
        }
        var confidence = 0.0;
        if (remaining.Length > 1 && channels.Count == 0)
        {
            // Without a line in the name, a family's base model is meant ("ALP-T 5nm" is the Hα+OIII one, not SII+Hβ).
            var baseModels = remaining.Where(item => Channels(Normalize(item.Filter.Name)).Count == 0).ToArray();
            if (baseModels.Length > 0 && baseModels.Length < remaining.Length) { remaining = baseModels; confidence = 0.8; }
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
        if (confidence == 0) confidence = match.BrandMatched ? 0.97 : 0.9;
        return FromProduct("", match.Filter, confidence, FilterMatchSource.CatalogProduct);
    }

    private static FilterIdentity? Generic(string raw, string normalized, double? bandwidth, IReadOnlyList<EmissionLine> channels)
    {
        var tokens = Tokens(normalized);
        var compact = Compact(normalized);
        var residual = Residual(normalized);
        var residualText = string.Join(' ', residual);
        if (compact is "uvir" or "uvircut" or "ircut" or "uvirblock" or "ir" or "iruv" || normalized.Contains("uv ir") || normalized.Contains("ir uv")
            || normalized.Contains("ir cut") || normalized.Contains("ir block") || residualText is "l2" or "l3" or "heuib" or "heuib ii")
            return new(raw, FilterKind.Broadband, [], null, null, 0.85, FilterMatchSource.GenericName, "UV/IR cut");

        // Mono sets are sold as "Chroma Blue 50 mm", "Astrodon Gen2 E-Series Tru-Balance Red 31mm", "Baader R-CCD".
        if (channels.Count == 0 && residual.Count > 0 && residual.All(BroadbandWords.ContainsKey)
            && residual.Select(token => BroadbandWords[token]).Distinct().Count() == 1 && BroadbandWords[residual[0]] is var colour)
            return new(raw, FilterKind.Broadband, [], null, null, 0.85, FilterMatchSource.GenericName, colour);

        if (tokens.Intersect(["lp", "cls", "lps", "skyglow", "antinquinamento", "uhc", "ngs1", "ngs", "gnb"]).Any()
            || normalized.Contains("light pollution") || normalized.Contains("night glow") || normalized.Contains("natural night")
            || (channels.Count == 0 && residualText is "deep sky" or "nebula" or "moon"))
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

        if (compact.Contains("rgb", StringComparison.Ordinal) && (compact.Contains("triband") || compact.Contains("tricolor")))
            return new(raw, FilterKind.LightPollution, [], null, null, 0.6, FilterMatchSource.GenericName, "Tribanda RGB");
        if (MultibandRegex().IsMatch(compact))
        {
            // "Dual band" without lines is Hα+OIII in practice; three or four bands stay open until confirmed.
            var dual = compact.Contains("dual") || compact.Contains("duo");
            return new(raw, FilterKind.Multiband, dual ? [EmissionLines.Ha, EmissionLines.Oiii] : [], null, bandwidth, 0.6, FilterMatchSource.GenericName,
                dual ? "Multibanda Hα + OIII" : "Multibanda");
        }

        var broadband = compact switch
        {
            "l" or "lum" or "luminance" or "luminanza" or "lrgbl" => "L",
            "r" or "red" or "rosso" => "R",
            "g" or "green" or "verde" => "G",
            "b" or "blue" or "blu" => "B",
            _ => null
        };
        if (broadband is not null) return new(raw, FilterKind.Broadband, [], null, null, 0.9, FilterMatchSource.GenericName, broadband);

        if (residualText is "lrgb" or "rgb" or "lrvb" or "lrgb set" or "rgb set")
            return new(raw, FilterKind.Broadband, [], null, null, 0.6, FilterMatchSource.GenericName, residualText.ToUpperInvariant().Replace(" SET", "") + " (set)");
        if (residualText is "clear" or "clearglass" or "mc clear" or "clear focusing")
            return new(raw, FilterKind.Broadband, [], null, null, 0.75, FilterMatchSource.GenericName, "Clear");
        if (compact.Contains("irpass") || IrPassRegex().IsMatch(normalized))
            return new(raw, FilterKind.Broadband, [], null, null, 0.8, FilterMatchSource.GenericName, "IR-pass");
        return null;
    }

    private static readonly Dictionary<string, string> BroadbandWords = new(StringComparer.Ordinal)
    {
        ["l"] = "L", ["lum"] = "L", ["luminance"] = "L", ["luminanza"] = "L", ["luminanz"] = "L",
        ["r"] = "R", ["red"] = "R", ["rosso"] = "R", ["rot"] = "R", ["rood"] = "R", ["rouge"] = "R", ["rojo"] = "R",
        ["g"] = "G", ["green"] = "G", ["verde"] = "G", ["grun"] = "G", ["groen"] = "G", ["vert"] = "G",
        ["b"] = "B", ["blue"] = "B", ["blu"] = "B", ["blau"] = "B", ["blauw"] = "B", ["bleu"] = "B", ["azul"] = "B"
    };

    private static readonly HashSet<string> Brands = new(StringComparer.Ordinal)
    {
        "altair", "antlia", "askar", "astrodon", "astronomik", "astronimik", "baader", "planetarium", "planetariun", "celestron", "chroma", "dwarflab",
        "explore", "scientific", "fli", "idas", "lumicon", "moravian", "nisi", "omegon", "optolong", "orion", "player", "one", "qhy", "qhyccd",
        "radian", "telescopes", "sharpstar", "skywatcher", "starlight", "xpress", "stc", "svbony", "ts", "optics", "touptek", "zwo", "atik"
    };

    private static readonly HashSet<string> NoiseWords = new(StringComparer.Ordinal)
    {
        "filter", "filters", "filtro", "filtri", "set", "filterset", "mounted", "unmounted", "cell", "gen", "ii", "e", "i", "series", "eseries",
        "iseries", "tru", "balance", "trubalance", "ccd", "cmos", "optimized", "optimised", "for", "type", "iic", "inch", "in", "mm", "nm",
        "v", "pro", "new", "version", "deep", "sky", "and", "typ", "2c", "square", "round", "gen1", "gen2", "gen3", "gen2e", "g2e", "generation", "serie", "true", "dark", "ximei", "astronomiks", "astromania", "minicam8m"
    };

    /// <summary>The one L, R, G or B colour a broadband name spells out ("Baader Red (CMOS-Optimized)" → "R").</summary>
    private static string? BroadbandColour(string normalized)
    {
        // Only colour words and the UV/IR cut of a luminance may be left: "UHC-L Booster" is not an L filter.
        var residual = Residual(normalized).Where(token => token is not ("uv" or "ir" or "cut" or "block")).ToArray();
        if (residual.Length == 0 || !residual.All(BroadbandWords.ContainsKey)) return null;
        var colours = residual.Select(token => BroadbandWords[token]).Distinct().ToArray();
        return colours.Length == 1 ? colours[0] : null;
    }

    /// <summary>What is left of a name once brand, sizes and marketing words are gone ("Chroma Blue 50 mm" → "blue").</summary>
    private static List<string> Residual(string normalized)
    {
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        // "Deep Sky" is noise inside "Astronomik Deep-Sky Red" but the whole point of "Lumicon Deep Sky".
        var result = tokens.Where(token => !Brands.Contains(token) && !NoiseWords.Contains(token) && !SizeRegex().IsMatch(token)).ToList();
        if (result.Count == 0 && normalized.Contains("deep sky")) return ["deep", "sky"];
        return result;
    }

    private static (string[] Stems, string[] Aliases, string[] Full) Keys(CatalogFilter filter) => KeyCache.GetOrAdd(filter, item =>
    {
        var aliases = item.Aliases.Select(alias => Compact(Normalize(alias))).Where(alias => alias.Length > 0).ToArray();
        var full = new[] { Compact(Normalize(item.Name.Split('(')[0])), Compact(Normalize(item.Name)) }.Concat(aliases).Distinct().ToArray();
        return ([Stem(item.Name)], item.Aliases.Select(Normalize).Where(alias => alias.Length > 0).ToArray(), full);
    });
    // Short aliases ("NB3") must stand as whole words, or they turn up inside "Astrodon B 31mm".
    private static bool ContainsAlias(string normalized, string compact, string alias) =>
        Compact(alias) is { Length: >= 6 } spelled ? compact.Contains(spelled, StringComparison.Ordinal) : $" {normalized} ".Contains($" {alias} ", StringComparison.Ordinal);

    private static readonly ConcurrentDictionary<CatalogFilter, (string[] Stems, string[] Aliases, string[] Full)> KeyCache = new(ReferenceEqualityComparer.Instance);

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
        // Values above 40 nm are a cut-on wavelength ("IR 742nm"), not a bandwidth.
        return match.Success && double.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value < 40 ? value : null;
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

    [GeneratedRegex(@"[\s_\-./\\+:;,()\[\]""'&]+")]
    private static partial Regex SeparatorRegex();
    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*nm")]
    private static partial Regex BandwidthRegex();
    [GeneratedRegex(@"^(\d+(?:[.,]\d+)?(mm|nm|in|inch)?|\d+x\d+(mm)?|asi\d+\w*|aps|apsc|c)$")]
    private static partial Regex SizeRegex();
    [GeneratedRegex(@"dualband|duoband|duonarrow|dualnarrow|triband|tribanda|quadband|multiband|multibanda")]
    private static partial Regex MultibandRegex();
    [GeneratedRegex(@"(?<![a-z])ir ?(6[5-9]\d|[7-9]\d\d)(?![0-9])|(?<![0-9])(6[5-9]\d|[7-9]\d\d) ?nm ?(ir|longpass)|ir (6[5-9]\d|[7-9]\d\d) ?nm|ir pass")]
    private static partial Regex IrPassRegex();
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
