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
        var imageType = Text(headers, "IMAGETYP", "FRAMETYP") ?? "";
        frame.IsMaster = imageType.Contains("master", StringComparison.OrdinalIgnoreCase) || MasterNameRegex().IsMatch(System.IO.Path.GetFileNameWithoutExtension(path));
        Set(frame.ObjectName, Text(headers, "OBJECT"));
        Set(frame.FilterName, Text(headers, "FILTER"));
        Set(frame.ExposureSeconds, Number(headers, "EXPTIME", "EXPOSURE"));
        Set(frame.Gain, Number(headers, "GAIN"));
        Set(frame.ElectronGain, Number(headers, "EGAIN"));
        Set(frame.Offset, Number(headers, "OFFSET"));
        Set(frame.SetTemperatureC, Number(headers, "SET-TEMP", "SETTEMP"));
        Set(frame.SensorTemperatureC, Number(headers, "CCD-TEMP", "CCDTEMP"));
        Set(frame.XBin, Integer(headers, "XBINNING", "XBIN"));
        Set(frame.YBin, Integer(headers, "YBINNING", "YBIN"));
        Set(frame.Width, Integer(headers, "NAXIS1"));
        Set(frame.Height, Integer(headers, "NAXIS2"));
        Set(frame.Camera, Text(headers, "INSTRUME", "CAMERA"));
        Set(frame.ReadoutMode, Text(headers, "READOUTM", "READOUT"));
        Set(frame.BayerPattern, Text(headers, "BAYERPAT"));
        Set(frame.FocalLengthMm, Number(headers, "FOCALLEN"));
        Set(frame.RotatorAngleDeg, Number(headers, "ROTATANG", "ROTATOR"));
        Set(frame.CapturedAt, Timestamp(headers, "DATE-LOC", "DATE-OBS", "DATE-UTC"));
        AstronomicalSessionResolver.Apply(frame, sessionSettings);
        FrameValidator.Revalidate(frame);
        return frame;
    }

    private static FrameKind ParseKind(Dictionary<string, object?> headers, string path)
    {
        var text = $"{Text(headers, "IMAGETYP", "FRAMETYP")} {System.IO.Path.GetFileName(path)}".ToLowerInvariant();
        if (text.Contains("dark flat") || text.Contains("flat dark") || text.Contains("darkflat")) return FrameKind.DarkFlat;
        if (text.Contains("light")) return FrameKind.Light;
        if (text.Contains("flat")) return FrameKind.Flat;
        if (text.Contains("dark")) return FrameKind.Dark;
        if (text.Contains("bias") || text.Contains("offset frame")) return FrameKind.Bias;
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
