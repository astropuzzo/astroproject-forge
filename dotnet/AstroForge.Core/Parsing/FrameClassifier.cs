using System.Globalization;
using System.Text.RegularExpressions;
using AstroForge.Core.Models;
using AstroForge.Core.Sessions;
using AstroForge.Core.Validation;

namespace AstroForge.Core.Parsing;

public static partial class FrameClassifier
{
    public static FrameMetadata Classify(string path, Dictionary<string, object?> headers, SessionSettings sessionSettings)
    {
        var frame = new FrameMetadata { Path = path, Kind = ParseKind(headers, path) };
        frame.Headers.EnsureCapacity(headers.Count);
        foreach (var pair in headers) frame.Headers[pair.Key] = pair.Value;
        var imageType = Text(headers, "IMAGETYP", "FRAMETYP", "OBSTYPE", "FRAME", "PICTTYPE", "IMAGE-TYP") ?? "";
        frame.IsMaster = imageType.Contains("master", StringComparison.OrdinalIgnoreCase) || Boolean(headers, "MASTER", "ISMASTER") == true || MasterNameRegex().IsMatch(System.IO.Path.GetFileNameWithoutExtension(path));
        Set(frame.ObjectName, Text(headers, "OBJECT"));
        Set(frame.FilterName, Text(headers, "FILTER", "FILTERID", "FILTNAME"));
        Set(frame.ExposureSeconds, Number(headers, "EXPTIME", "EXPOSURE", "EXPOSURETIME"));
        Set(frame.Gain, Number(headers, "GAIN", "CAM-GAIN", "CCDGAIN"));
        Set(frame.ElectronGain, Number(headers, "EGAIN"));
        Set(frame.Offset, Number(headers, "OFFSET", "BLKLEVEL", "BLACKLVL"));
        Set(frame.SetTemperatureC, Number(headers, "SET-TEMP", "SETTEMP", "CCD-SETT", "COOLER-T"));
        Set(frame.SensorTemperatureC, Number(headers, "CCD-TEMP", "CCDTEMP", "SENSOR-T"));
        Set(frame.XBin, Integer(headers, "XBINNING", "XBIN", "CCDXBIN"));
        Set(frame.YBin, Integer(headers, "YBINNING", "YBIN", "CCDYBIN"));
        Set(frame.Width, Integer(headers, "NAXIS1"));
        Set(frame.Height, Integer(headers, "NAXIS2"));
        Set(frame.Camera, Text(headers, "INSTRUME", "CAMERA", "DETECTOR", "SENSOR"));
        Set(frame.ReadoutMode, Text(headers, "READOUTM", "READOUT", "READMODE", "READ-MOD"));
        Set(frame.BayerPattern, Text(headers, "BAYERPAT", "BAYERPATTERN", "COLORTYP"));
        Set(frame.FocalLengthMm, Number(headers, "FOCALLEN", "FOCALLENGTH"));
        Set(frame.RotatorAngleDeg, Number(headers, "ROTATANG", "ROTATOR", "ROTANGLE"));
        Set(frame.CapturedAt, CaptureTime(headers, out var siteOffset));
        frame.SiteUtcOffset = siteOffset;
        ApplyFileName(frame, FileNameMetadata.Parse(path), Text(headers, "SWCREATE"));
        AstronomicalSessionResolver.Apply(frame, sessionSettings);
        FrameValidator.Revalidate(frame);
        return frame;
    }

    private static void ApplyFileName(FrameMetadata frame, FileNameMetadata name, string? software)
    {
        // N.I.N.A. fills the name and the FILTER header from the same wheel: without the header, the token in the
        // name is the target or "FlatWizard", and taking it for a filter would keep Lights and Flats apart.
        var nameHasNoFilter = software?.StartsWith("N.I.N.A", StringComparison.OrdinalIgnoreCase) == true;
        if (frame.FilterName.Value is null && name.Filter is { Length: > 0 } && !nameHasNoFilter) frame.FilterName.SetOriginal(name.Filter, MetadataSource.Filename);
        if (frame.ExposureSeconds.Value is null && name.ExposureSeconds is { } exposure) frame.ExposureSeconds.SetOriginal(exposure, MetadataSource.Filename);
        if (frame.Gain.Value is null && name.Gain is { } gain) frame.Gain.SetOriginal(gain, MetadataSource.Filename);
        if (frame.SensorTemperatureC.Value is null && name.SensorTemperatureC is { } temperature) frame.SensorTemperatureC.SetOriginal(temperature, MetadataSource.Filename);
        if (frame.XBin.Value is null && name.Binning is { } xbin) frame.XBin.SetOriginal(xbin, MetadataSource.Filename);
        if (frame.YBin.Value is null && name.Binning is { } ybin) frame.YBin.SetOriginal(ybin, MetadataSource.Filename);
    }

    private static FrameKind ParseKind(Dictionary<string, object?> headers, string path)
    {
        var headerKind = KindFrom(Text(headers, "IMAGETYP", "FRAMETYP", "OBSTYPE", "FRAME", "PICTTYPE", "IMAGE-TYP"));
        return headerKind == FrameKind.Unknown ? KindFrom(System.IO.Path.GetFileName(path)) : headerKind;
    }

    private static FrameKind KindFrom(string? value)
    {
        // "Dark_Flat", "flat-dark" and "FLATDARK" are the same frame type written with other separators.
        var text = SeparatorRegex().Replace((value ?? "").ToLowerInvariant(), " ");
        if (text.Contains("dark flat") || text.Contains("flat dark") || text.Contains("darkflat") || text.Contains("flatdark")) return FrameKind.DarkFlat;
        if (text.Contains("light") || text.Contains("object") || text.Contains("science")) return FrameKind.Light;
        if (text.Contains("flat")) return FrameKind.Flat;
        if (text.Contains("dark")) return FrameKind.Dark;
        if (text.Contains("bias") || text.Contains("offset frame") || text.Contains("zero")) return FrameKind.Bias;
        return FrameKind.Unknown;
    }

    private static string? Text(Dictionary<string, object?> headers, params string[] keys) => keys.Select(key => Raw(headers.GetValueOrDefault(key))?.Trim()).FirstOrDefault(value => !string.IsNullOrEmpty(value));
    private static double? Number(Dictionary<string, object?> headers, params string[] keys)
    {
        foreach (var key in keys)
            if (headers.TryGetValue(key, out var value) && double.TryParse(Raw(value), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return number;
        return null;
    }
    private static int? Integer(Dictionary<string, object?> headers, params string[] keys) => Number(headers, keys) is { } value && Math.Abs(value % 1) < 1e-9 ? (int)value : null;
    private static bool? Boolean(Dictionary<string, object?> headers, params string[] keys)
    {
        var value = Text(headers, keys);
        if (value is null) return null;
        if (bool.TryParse(value, out var boolean)) return boolean;
        return value.Trim().ToUpperInvariant() switch { "T" or "YES" or "1" => true, "F" or "NO" or "0" => false, _ => null };
    }
    /// <summary>
    /// When the exposure started. DATE-OBS is UTC by the FITS standard; N.I.N.A., SGP and APT add DATE-LOC, the same
    /// instant on the site's wall clock, so the difference between the two is the site's UTC offset.
    /// </summary>
    private static DateTimeOffset? CaptureTime(Dictionary<string, object?> headers, out TimeSpan? siteOffset)
    {
        siteOffset = null;
        var utc = Utc(headers, "DATE-OBS", "DATE-UTC");
        var wallClock = headers.TryGetValue("DATE-LOC", out var value)
            && DateTime.TryParse(Raw(value), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : (DateTime?)null;
        if (utc is { } instant && wallClock is { Kind: DateTimeKind.Unspecified } local)
        {
            var offset = TimeSpan.FromMinutes(Math.Round((local - instant.UtcDateTime).TotalMinutes / 15) * 15);
            // Offsets on Earth run from -12 h to +14 h in quarter hours: anything else is two clocks that disagree.
            if (offset >= TimeSpan.FromHours(-12) && offset <= TimeSpan.FromHours(14) && (local - instant.UtcDateTime - offset).Duration() < TimeSpan.FromMinutes(5))
            {
                siteOffset = offset;
                return instant.ToOffset(offset);
            }
        }
        if (utc is not null) return utc;
        if (wallClock is { } alone) return alone.Kind == DateTimeKind.Unspecified ? new DateTimeOffset(alone, TimeZoneInfo.Local.GetUtcOffset(alone)) : new DateTimeOffset(alone);
        return Utc(headers, "DATE");
    }

    private static DateTimeOffset? Utc(Dictionary<string, object?> headers, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!headers.TryGetValue(key, out var value) || Raw(value) is not { Length: > 0 } text) continue;
            // Older software splits the instant in two cards: the date in DATE-OBS, the time in TIME-OBS.
            if (key == "DATE-OBS" && !text.Contains('T') && headers.TryGetValue("TIME-OBS", out var time) && Raw(time) is { Length: > 0 } clock) text = $"{text.Trim()}T{clock.Trim()}";
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp)) return timestamp;
        }
        return null;
    }
    // Header values are already typed (-9.9 is a double): format them invariantly, or on an Italian system
    // "-9,9" no longer parses and every fractional value (CCD-TEMP, EXPTIME of Flats) is silently lost.
    private static string? Raw(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture);
    private static void Set<T>(MetadataField<T> field, T value) => field.SetOriginal(value, MetadataSource.Header);

    // "masterDark_300s" and "MasterFlat" count; "Remastered" does not.
    [GeneratedRegex(@"(?<![a-z])masters?(?=$|[^a-z]|bias|dark|flat|light)", RegexOptions.IgnoreCase)]
    private static partial Regex MasterNameRegex();
    [GeneratedRegex(@"[\s_\-.]+")]
    private static partial Regex SeparatorRegex();
}
