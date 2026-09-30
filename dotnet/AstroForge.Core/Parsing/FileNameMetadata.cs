using System.Globalization;
using System.Text.RegularExpressions;
using AstroForge.Core.Filters;
using AstroForge.Core.Models;

namespace AstroForge.Core.Parsing;

/// <summary>Acquisition values that capture software writes into the file name.</summary>
public sealed partial record FileNameMetadata(
    FrameKind? Kind,
    string? Filter,
    double? ExposureSeconds,
    int? Binning,
    double? Gain,
    double? SensorTemperatureC)
{
    public static readonly FileNameMetadata Empty = new(null, null, null, null, null, null);

    /// <summary>
    /// Reads the common naming schemes: ZWO ASIAIR (<c>Light_M31_300.0s_Bin1_Ha_gain100_20231015-231512_-10.0C_0001</c>)
    /// and the N.I.N.A. default (<c>2026-06-15_00-21-26_Ha_-10.00_300.00s_0001</c>). Headers always win; these values
    /// only fill what the header leaves empty and are marked as coming from the file name.
    /// </summary>
    public static FileNameMetadata Parse(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        var tokens = stem.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2) return Empty;

        FrameKind? kind = null;
        string? filter = null;
        double? exposure = null, gain = null, temperature = null;
        int? binning = null;
        var ninaLayout = tokens.Length >= 3 && DateRegex().IsMatch(tokens[0]) && TimeRegex().IsMatch(tokens[1]);
        var candidates = new List<(int Index, string Token)>();

        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (index == 0 && KindFrom(token) is { } parsedKind) { kind = parsedKind; continue; }
            if (ExposureRegex().Match(token) is { Success: true } exposureMatch)
            {
                var value = Number(exposureMatch.Groups[1].Value);
                exposure ??= exposureMatch.Groups[2].Value.Equals("ms", StringComparison.OrdinalIgnoreCase) ? value / 1000 : value;
                continue;
            }
            if (BinRegex().Match(token) is { Success: true } binMatch) { binning ??= int.Parse(binMatch.Groups[1].Value, CultureInfo.InvariantCulture); continue; }
            if (GainRegex().Match(token) is { Success: true } gainMatch) { gain ??= Number(gainMatch.Groups[1].Value); continue; }
            if (TemperatureRegex().Match(token) is { Success: true } temperatureMatch) { temperature ??= Number(temperatureMatch.Groups[1].Value); continue; }
            if (ninaLayout && index > 2 && SignedDecimalRegex().IsMatch(token)) { temperature ??= Number(token); continue; }
            if (DateRegex().IsMatch(token) || TimeRegex().IsMatch(token) || StampRegex().IsMatch(token) || FrameNumberRegex().IsMatch(token)) continue;
            candidates.Add((index, token));
        }

        // ASIAIR writes the filter right after the binning token; N.I.N.A. right after date and time.
        var binIndex = Array.FindIndex(tokens, token => BinRegex().IsMatch(token));
        var positional = candidates.FirstOrDefault(item => (binIndex >= 0 && item.Index == binIndex + 1) || (ninaLayout && item.Index == 2));
        if (positional.Token is not null) filter = positional.Token;
        else filter = candidates.Select(item => item.Token)
            .FirstOrDefault(token => FilterRecognizer.Recognize(token) is { IsRecognized: true, Kind: not FilterKind.None });

        return new(kind, filter, exposure, binning, gain, temperature);
    }

    private static FrameKind? KindFrom(string token) => token.ToLowerInvariant() switch
    {
        "light" or "lights" => FrameKind.Light,
        "flat" or "flats" => FrameKind.Flat,
        "dark" or "darks" => FrameKind.Dark,
        "bias" or "offset" => FrameKind.Bias,
        "darkflat" or "flatdark" => FrameKind.DarkFlat,
        _ => null
    };

    private static double Number(string value) => double.Parse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(\d+(?:[.,]\d+)?)(s|sec|ms)$", RegexOptions.IgnoreCase)]
    private static partial Regex ExposureRegex();
    [GeneratedRegex(@"^bin(\d)(?:x\d)?$", RegexOptions.IgnoreCase)]
    private static partial Regex BinRegex();
    [GeneratedRegex(@"^gain(\d+(?:[.,]\d+)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex GainRegex();
    [GeneratedRegex(@"^(-?\d+(?:[.,]\d+)?)c$", RegexOptions.IgnoreCase)]
    private static partial Regex TemperatureRegex();
    [GeneratedRegex(@"^-?\d+\.\d+$")]
    private static partial Regex SignedDecimalRegex();
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex DateRegex();
    [GeneratedRegex(@"^\d{2}-\d{2}-\d{2}$")]
    private static partial Regex TimeRegex();
    [GeneratedRegex(@"^\d{8}-\d{6}$")]
    private static partial Regex StampRegex();
    [GeneratedRegex(@"^\d{3,5}$")]
    private static partial Regex FrameNumberRegex();
}
