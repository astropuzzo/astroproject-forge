namespace AstroForge.Core.Models;

public enum FrameKind
{
    Unknown,
    Light,
    Flat,
    Dark,
    Bias,
    DarkFlat
}

public enum MetadataSource
{
    Missing,
    Header,
    LibraryPath,
    Filename,
    Inferred,
    ProjectDefault,
    UserOverride
}

public enum IssueSeverity
{
    Info,
    Warning,
    Error
}

public sealed record FrameIssue(string Code, IssueSeverity Severity, string Message, string? Field = null);

public sealed class MetadataField<T>
{
    public T? OriginalValue { get; private set; }
    public MetadataSource OriginalSource { get; private set; } = MetadataSource.Missing;
    public T? OverrideValue { get; private set; }
    public bool HasOverride { get; private set; }

    public T? Value => HasOverride ? OverrideValue : OriginalValue;
    public MetadataSource Source => HasOverride ? MetadataSource.UserOverride : OriginalSource;

    public void SetOriginal(T? value, MetadataSource source)
    {
        OriginalValue = value;
        OriginalSource = value is null ? MetadataSource.Missing : source;
    }

    public void SetOverride(T? value)
    {
        OverrideValue = value;
        HasOverride = true;
    }

    public void ClearOverride()
    {
        OverrideValue = default;
        HasOverride = false;
    }
}

public sealed class FrameMetadata
{
    public required string Path { get; init; }
    public FrameKind Kind { get; set; }
    public bool IsMaster { get; set; }
    public int? ConfiguredLibraryPriority { get; set; }
    public MetadataField<string?> ObjectName { get; } = new();
    public MetadataField<string?> FilterName { get; } = new();
    public MetadataField<string?> FlatSetId { get; } = new();
    public MetadataField<double?> ExposureSeconds { get; } = new();
    public MetadataField<double?> Gain { get; } = new();
    public MetadataField<double?> ElectronGain { get; } = new();
    public MetadataField<double?> Offset { get; } = new();
    public MetadataField<double?> SetTemperatureC { get; } = new();
    public MetadataField<double?> SensorTemperatureC { get; } = new();
    public MetadataField<int?> XBin { get; } = new();
    public MetadataField<int?> YBin { get; } = new();
    public MetadataField<int?> Width { get; } = new();
    public MetadataField<int?> Height { get; } = new();
    public MetadataField<string?> Camera { get; } = new();
    public MetadataField<string?> ReadoutMode { get; } = new();
    public MetadataField<string?> BayerPattern { get; } = new();
    public MetadataField<double?> FocalLengthMm { get; } = new();
    public MetadataField<double?> RotatorAngleDeg { get; } = new();
    public MetadataField<DateTimeOffset?> CapturedAt { get; } = new();
    public MetadataField<string?> SessionId { get; } = new();
    public MetadataField<string?> ManualDarkPath { get; } = new();
    public MetadataField<string?> ManualBiasPath { get; } = new();
    public Dictionary<string, object?> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<FrameIssue> Issues { get; } = [];

    public string FileName => System.IO.Path.GetFileName(Path);
    public double? EffectiveTemperatureC => SetTemperatureC.Value ?? SensorTemperatureC.Value;
}
