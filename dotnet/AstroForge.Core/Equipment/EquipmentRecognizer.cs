using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace AstroForge.Core.Equipment;

/// <summary>What Forge believes the camera named in a header (INSTRUME) is.</summary>
public sealed record CameraIdentity(string RawName, CatalogCamera? Camera, CameraSensorType Type, double Confidence)
{
    public bool IsRecognized => Camera is not null || Type != CameraSensorType.Unknown;
    public bool IsColor => Type is CameraSensorType.Color or CameraSensorType.Dslr or CameraSensorType.DslrModified;
    public string DisplayName => Camera?.Name ?? (RawName.Length == 0 ? "Camera sconosciuta" : RawName);
}

/// <summary>What Forge believes the optics named in a header (TELESCOP) are, and whether a reducer explains FOCALLEN.</summary>
public sealed record TelescopeIdentity(string RawName, CatalogTelescope? Telescope, double? FocalMm, TelescopeReducer? Reducer, double Confidence)
{
    public bool IsRecognized => Telescope is not null;
    public string DisplayName => Telescope is null ? RawName : Reducer is null ? Telescope.Name : $"{Telescope.Name} + {Reducer.Name}";
}

/// <summary>
/// Maps the free-form camera and telescope names that capture software writes (N.I.N.A. profile names, ASIAIR,
/// ASCOM driver names) to the equipment catalogue. Names it cannot place stay unrecognized so the instrument
/// profile can ask once.
/// </summary>
public static partial class EquipmentRecognizer
{
    private static readonly ConcurrentDictionary<(string, EquipmentCatalog), CameraIdentity> CameraCache = new();
    private static readonly ConditionalWeakTable<object, string[]> KeyCache = new();

    private static readonly string[] CameraBrands =
    [
        "zwo", "qhyccd", "player one", "playerone", "touptek", "touptek astro", "risingcam", "svbony", "altair", "atik", "moravian",
        "moravian instruments", "omegon", "ts optics", "ts-optics", "starlight xpress", "fli", "sbig", "orion", "celestron", "pegasus astro"
    ];
    private static readonly string[] TelescopeBrands =
    [
        "sky-watcher", "skywatcher", "sky watcher", "william optics", "wo", "celestron", "askar", "takahashi", "ts-optics", "ts optics", "tsoptics",
        "explore scientific", "es", "stellarvue", "tele vue", "televue", "sharpstar", "sharp star", "radian", "svbony", "zwo", "orion", "meade",
        "lacerta", "gso", "planewave", "officina stellare", "tecnosky", "apm", "altair", "vixen", "borg", "samyang", "rokinon", "sigma", "canon", "nikon"
    ];

    public static CameraIdentity Camera(string? rawName, EquipmentCatalog? catalog = null)
    {
        catalog ??= EquipmentCatalog.Default;
        var raw = (rawName ?? "").Trim();
        return CameraCache.GetOrAdd((raw, catalog), key => RecognizeCamera(key.Item1, key.Item2));
    }

    public static TelescopeIdentity Telescope(string? rawName, double? focalMm = null, EquipmentCatalog? catalog = null)
    {
        catalog ??= EquipmentCatalog.Default;
        var raw = (rawName ?? "").Trim();
        var telescope = Match(raw, catalog.Telescopes, item => item.Name, item => item.Aliases, item => item.Uses, TelescopeBrands, out var confidence,
            (left, right) => left.ApertureMm == right.ApertureMm && left.FocalMm == right.FocalMm);
        if (telescope is null) return new(raw, null, focalMm, null, 0);
        if (focalMm is not { } focal || focal <= 0 || Near(focal, telescope.FocalMm)) return new(raw, telescope, focalMm ?? telescope.FocalMm, null, confidence);
        // FOCALLEN that matches a known reducer or flattener confirms the match; one far from every option weakens it.
        // The same optics may be listed twice (an AstroBin spelling without reducers): borrow the reducers of its twin.
        var reducer = catalog.Telescopes.Where(item => item.ApertureMm == telescope.ApertureMm && item.FocalMm == telescope.FocalMm)
            .SelectMany(item => item.Reducers).FirstOrDefault(item => Near(focal, telescope.FocalMm * item.Factor));
        return new(raw, telescope, focal, reducer, reducer is null ? Math.Min(confidence, 0.6) : confidence);
    }

    /// <summary>Sampling in arcseconds per pixel.</summary>
    public static double ImageScale(double pixelUm, double focalMm, int binning = 1) => 206.265 * pixelUm * Math.Max(1, binning) / focalMm;

    private static CameraIdentity RecognizeCamera(string raw, EquipmentCatalog catalog)
    {
        if (raw.Length == 0) return new(raw, null, CameraSensorType.Unknown, 0);
        var camera = Match(raw, catalog.Cameras, item => item.Name, item => item.Aliases, item => item.Uses, CameraBrands, out var confidence,
            (left, right) => left.Type == right.Type && left.PixelUm == right.PixelUm && left.Width == right.Width);
        if (camera is not null) return new(raw, camera, camera.Type, confidence);
        // Unknown model: the name still says mono or colour for most astro cameras ("ASI1600MM", "QHY268C", "Canon EOS").
        var text = raw.ToLowerInvariant();
        var type = DslrRegex().IsMatch(text) ? (ModifiedRegex().IsMatch(text) ? CameraSensorType.DslrModified : CameraSensorType.Dslr)
            : MonoRegex().IsMatch(text) ? CameraSensorType.Mono
            : ColorRegex().IsMatch(text) ? CameraSensorType.Color
            : CameraSensorType.Unknown;
        return new(raw, null, type, type == CameraSensorType.Unknown ? 0 : 0.6);
    }

    private static T? Match<T>(string raw, IReadOnlyList<T> items, Func<T, string> name, Func<T, IReadOnlyList<string>> aliases, Func<T, int> uses,
        string[] brands, out double confidence, Func<T, T, bool> equivalent) where T : class
    {
        confidence = 0;
        var input = Key(raw, brands);
        if (input.Length < 3 || !input.Any(char.IsDigit) && input.Length < 6) return null;

        var scored = new List<(T Item, int Score, int Length)>();
        foreach (var item in items)
        {
            foreach (var key in KeyCache.GetValue(item, _ => [Key(name(item), brands), .. aliases(item).Select(alias => Key(alias, brands))]))
            {
                if (key.Length < 3) continue;
                // 3 = same model name, 2 = the header names the catalogue model plus extras, 1 = the header is a shorter form.
                var score = key == input ? 3
                    : key.Length >= 5 && input.Contains(key, StringComparison.Ordinal) && Boundary(input, key) ? 2
                    : input.Length >= 5 && key.StartsWith(input, StringComparison.Ordinal) && !char.IsDigit(key[input.Length]) ? 1
                    : 0;
                if (score > 0) scored.Add((item, score, key.Length));
            }
        }
        if (scored.Count == 0) return null;
        var bestScore = scored.Max(item => item.Score);
        var best = scored.Where(item => item.Score == bestScore).ToArray();
        if (bestScore == 2)
        {
            var longest = best.Max(item => item.Length);
            best = best.Where(item => item.Length == longest).ToArray();
        }
        var winner = best.OrderByDescending(item => uses(item.Item)).First().Item;
        // A shorter form ("ASI2600MC") that fits several variants (Pro, Air, Duo) is safe when they share the sensor.
        confidence = bestScore switch { 3 => 0.97, 2 => 0.9, _ => best.All(item => equivalent(item.Item, winner)) ? 0.85 : 0.7 };
        return winner;
    }

    // "asi2600mm" inside "asi2600mmpro" is fine, but "asi294mm" must not match inside "asi2940mm".
    private static bool Boundary(string input, string key)
    {
        var index = input.IndexOf(key, StringComparison.Ordinal);
        var end = index + key.Length;
        return end >= input.Length || !(char.IsDigit(key[^1]) && char.IsDigit(input[end]));
    }

    private static string Key(string name, string[] brands)
    {
        var text = RemoveDiacritics(name.ToLowerInvariant()).Replace("α", "a");
        text = " " + SeparatorRegex().Replace(text, " ") + " ";
        foreach (var brand in brands.OrderByDescending(brand => brand.Length))
            text = text.Replace(" " + SeparatorRegex().Replace(brand, " ") + " ", " ", StringComparison.Ordinal);
        return NonAlphanumericRegex().Replace(text, "");
    }

    private static bool Near(double value, double expected) => Math.Abs(value - expected) <= Math.Max(5, expected * 0.03);

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
    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlphanumericRegex();
    [GeneratedRegex(@"\b(canon|nikon|sony|pentax|fuji|fujifilm|olympus|panasonic|lumix)\b|\beos\b")]
    private static partial Regex DslrRegex();
    [GeneratedRegex(@"modif|full spectrum|\bmod\b|60da\b|d810a\b|\bra\b")]
    private static partial Regex ModifiedRegex();
    [GeneratedRegex(@"\d{3,4} ?mm\b|\bmono\b|\d{3,4}m\b|\d{3,4}m (pro|mini|cool)|-m\b|\bm pro\b")]
    private static partial Regex MonoRegex();
    [GeneratedRegex(@"\d{3,4} ?mc\b|\bcolou?r\b|\bosc\b|\d{3,4}c\b|\d{3,4}c (pro|mini|cool)|-c\b|\bc pro\b")]
    private static partial Regex ColorRegex();
}
