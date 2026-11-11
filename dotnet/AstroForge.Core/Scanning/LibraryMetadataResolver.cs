using System.Globalization;
using System.Text.RegularExpressions;
using AstroForge.Core.Models;
using AstroForge.Core.Validation;

namespace AstroForge.Core.Scanning;

public static partial class LibraryMetadataResolver
{
    public static Dictionary<string, List<string>> Apply(IEnumerable<FrameMetadata> frames, string libraryRoot, int priority = 1)
    {
        var root = System.IO.Path.GetFullPath(libraryRoot).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        var applied = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var frame in frames.Where(frame => frame.IsMaster && frame.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
        {
            frame.ConfiguredLibraryPriority = priority;
            var relative = System.IO.Path.GetRelativePath(libraryRoot, frame.Path);
            var parts = relative.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            var changes = new List<string>();
            var gain = Labeled(parts, GainRegex());
            var temperature = Labeled(parts, TemperatureRegex());
            // The frame type is known from its header or name: the folders need not say "dark" again (GAIN_100\-10\MasterDark.xisf).
            var darkTree = frame.Kind == FrameKind.Dark || parts.Take(parts.Length - 1).Any(part => part.Contains("dark", StringComparison.OrdinalIgnoreCase));
            var biasTree = frame.Kind == FrameKind.Bias || parts.Take(parts.Length - 1).Any(part => part.Contains("bias", StringComparison.OrdinalIgnoreCase));
            if (temperature is null && darkTree && gain is not null && parts.Length >= 2 && Plain(parts[^2]) is >= -60 and <= 45 and var folderTemperature)
                temperature = folderTemperature;
            if (gain is null && biasTree)
                gain = BiasGain(System.IO.Path.GetFileNameWithoutExtension(frame.Path));
            Apply(frame.Gain, gain, "gain", changes);
            Apply(frame.SetTemperatureC, temperature, "temperatura", changes);
            if (changes.Count > 0)
            {
                applied[frame.Path] = changes;
                FrameValidator.Revalidate(frame);
            }
        }
        return applied;
    }

    private static void Apply(MetadataField<double?> field, double? value, string label, List<string> changes)
    {
        if (value is null || field.Value is not null) return;
        field.SetOriginal(value, MetadataSource.LibraryPath);
        changes.Add($"{label}={value.Value.ToString("g", CultureInfo.InvariantCulture)}");
    }

    private static double? Labeled(IEnumerable<string> parts, Regex regex)
    {
        var values = parts.Select(part => regex.Match(part)).Where(match => match.Success)
            .Select(match => double.Parse(match.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture)).Distinct().ToArray();
        return values.Length == 1 ? values[0] : null;
    }

    private static double? Plain(string value) => double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
    private static double? BiasGain(string stem)
    {
        var match = BiasGainRegex().Match(stem);
        return match.Success ? double.Parse(match.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture) : Plain(stem);
    }

    [GeneratedRegex(@"^gain[_ -]?(-?\d+(?:[.,]\d+)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex GainRegex();
    // The separator is lazy so that "Temp-10" reads as -10 °C, the way people name cooled Darks.
    [GeneratedRegex(@"^(?:temp|temperature)[_ -]??(-?\d+(?:[.,]\d+)?)\s*(?:°?c)?$", RegexOptions.IgnoreCase)]
    private static partial Regex TemperatureRegex();
    [GeneratedRegex(@"(?:gain|bias)[_ -]?(-?\d+(?:[.,]\d+)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex BiasGainRegex();
}
