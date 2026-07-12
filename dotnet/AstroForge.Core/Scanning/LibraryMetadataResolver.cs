using System.Globalization;
using System.Text.RegularExpressions;
using AstroForge.Core.Models;
using AstroForge.Core.Validation;

namespace AstroForge.Core.Scanning;

public static partial class LibraryMetadataResolver
{
    public static Dictionary<string, List<string>> Apply(IEnumerable<FrameMetadata> frames, string libraryRoot)
    {
        var root = System.IO.Path.GetFullPath(libraryRoot).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        var applied = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var frame in frames.Where(frame => frame.IsMaster && frame.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
        {
            var relative = System.IO.Path.GetRelativePath(libraryRoot, frame.Path);
            var parts = relative.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            var changes = new List<string>();
            var gain = Labeled(parts, GainRegex());
            var temperature = Labeled(parts, TemperatureRegex());
            var darkTree = parts.Take(parts.Length - 1).Any(part => part.Contains("dark", StringComparison.OrdinalIgnoreCase));
            var biasTree = parts.Take(parts.Length - 1).Any(part => part.Contains("bias", StringComparison.OrdinalIgnoreCase));
            if (temperature is null && darkTree && gain is not null && parts.Length >= 2)
                temperature = Plain(parts[^2]);
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
    [GeneratedRegex(@"^(?:temp|temperature)[_ -]?(-?\d+(?:[.,]\d+)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex TemperatureRegex();
    [GeneratedRegex(@"(?:gain|bias)[_ -]?(-?\d+(?:[.,]\d+)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex BiasGainRegex();
}
