using AstroForge.Core.Models;

namespace AstroForge.Core.Validation;

public static class FrameValidator
{
    private static readonly HashSet<string> ManagedCodes =
    [
        "frame.kind_missing",
        "binning.mismatch",
        "temperature.unstable",
        "metadata.filter_missing",
        "master.gain_missing",
        "master.temperature_missing"
    ];

    public static void Revalidate(FrameMetadata frame)
    {
        frame.Issues.RemoveAll(issue => ManagedCodes.Contains(issue.Code));
        if (frame.Kind == FrameKind.Unknown)
            frame.Issues.Add(new("frame.kind_missing", IssueSeverity.Error, "Tipo frame non riconosciuto.", "Kind"));
        if (frame.XBin.Value is { } x && frame.YBin.Value is { } y && x != y)
            frame.Issues.Add(new("binning.mismatch", IssueSeverity.Error, "XBINNING e YBINNING sono diversi.", "Binning"));
        if (frame.SetTemperatureC.Value is { } set && frame.SensorTemperatureC.Value is { } actual && Math.Abs(set - actual) > 2)
            frame.Issues.Add(new("temperature.unstable", IssueSeverity.Warning, $"Sensore distante {Math.Abs(set - actual):0.0} °C dal setpoint.", "SensorTemperatureC"));
        if (frame.Kind == FrameKind.Light && frame.FilterName.Value is null)
            frame.Issues.Add(new("metadata.filter_missing", IssueSeverity.Warning, "Filtro mancante.", "FilterName"));
        if (frame.IsMaster && frame.Kind is FrameKind.Dark or FrameKind.Bias && frame.Gain.Value is null)
            frame.Issues.Add(new("master.gain_missing", IssueSeverity.Warning, "Gain di acquisizione assente; EGAIN non è equivalente.", "Gain"));
        if (frame.IsMaster && frame.Kind == FrameKind.Dark && frame.EffectiveTemperatureC is null)
            frame.Issues.Add(new("master.temperature_missing", IssueSeverity.Warning, "Temperatura assente dal Master Dark.", "SetTemperatureC"));
    }
}
