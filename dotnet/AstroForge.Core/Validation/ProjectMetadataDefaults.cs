using AstroForge.Core.Models;

namespace AstroForge.Core.Validation;

public sealed record ProjectMetadataDefaults(double? Gain = 100, double? Offset = 51, double? TemperatureC = null);

public static class ProjectMetadataDefaultsResolver
{
    public static int Apply(IEnumerable<FrameMetadata> frames, ProjectMetadataDefaults defaults)
    {
        var changedFrames = 0;
        foreach (var frame in frames)
        {
            var changed = false;
            changed |= Apply(frame.Gain, defaults.Gain);
            changed |= Apply(frame.Offset, defaults.Offset);
            if (frame.SetTemperatureC.OriginalSource == MetadataSource.ProjectDefault ||
                (!frame.SetTemperatureC.HasOverride && frame.SetTemperatureC.OriginalValue is null && frame.SensorTemperatureC.Value is null))
                changed |= Apply(frame.SetTemperatureC, defaults.TemperatureC);
            if (!changed) continue;
            changedFrames++;
            FrameValidator.Revalidate(frame);
        }
        return changedFrames;
    }

    private static bool Apply(MetadataField<double?> field, double? value)
    {
        if (field.HasOverride) return false;
        if (field.OriginalSource is not (MetadataSource.Missing or MetadataSource.ProjectDefault)) return false;
        if (Nullable.Equals(field.OriginalValue, value) && field.OriginalSource == (value is null ? MetadataSource.Missing : MetadataSource.ProjectDefault)) return false;
        field.SetOriginal(value, MetadataSource.ProjectDefault);
        return true;
    }
}
