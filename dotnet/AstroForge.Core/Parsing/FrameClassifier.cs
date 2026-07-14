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
        Set(frame.CapturedAt, Timestamp(headers, "DATE-LOC", "DATE-OBS", "DATE-UTC", "DATE"));
        AstronomicalSessionResolver.Apply(frame, sessionSettings);
        FrameValidator.Revalidate(frame);
        return frame;
    }

    private static FrameKind ParseKind(Dictionary<string, object?> headers, string path)
    {
        var headerKind = KindFrom(Text(headers, "IMAGETYP", "FRAMETYP", "OBSTYPE", "FRAME", "PICTTYPE", "IMAGE-TYP"));
        return headerKind == FrameKind.Unknown ? KindFrom(System.IO.Path.GetFileName(path)) : headerKind;
    }

    private static FrameKind KindFrom(string? value)
    {
        var text = (value ?? "").ToLowerInvariant();
        if (text.Contains("dark flat") || text.Contains("flat dark") || text.Contains("darkflat")) return FrameKind.DarkFlat;
        if (text.Contains("light") || text.Contains("object") || text.Contains("science")) return FrameKind.Light;
        if (text.Contains("flat")) return FrameKind.Flat;
        if (text.Contains("dark")) return FrameKind.Dark;
        if (text.Contains("bias") || text.Contains("offset frame") || text.Contains("zero")) return FrameKind.Bias;
        return FrameKind.Unknown;
    }

    private static string? Text(Dictionary<string, object?> headers, params string[] keys) => keys.Select(key => headers.GetValueOrDefault(key)?.ToString()?.Trim()).FirstOrDefault(value => !string.IsNullOrEmpty(value));
    private static double? Number(Dictionary<string, object?> headers, params string[] keys)
    {
        foreach (var key in keys)
            if (headers.TryGetValue(key, out var value) && double.TryParse(value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return number;
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
    private static DateTimeOffset? Timestamp(Dictionary<string, object?> headers, params string[] keys)
    {
        foreach (var key in keys)
            if (headers.TryGetValue(key, out var value) && DateTimeOffset.TryParse(value?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var timestamp)) return timestamp;
        return null;
    }
    private static void Set<T>(MetadataField<T> field, T value) => field.SetOriginal(value, MetadataSource.Header);

    [GeneratedRegex(@"(^|[^a-z])master([^a-z]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex MasterNameRegex();
}
