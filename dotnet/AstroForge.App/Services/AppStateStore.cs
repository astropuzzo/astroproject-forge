using System.Text.Json;
using System.IO;
using System.Globalization;
using AstroForge.Core.Models;
using AstroForge.Core.Persistence;
using AstroForge.Core.IO;

namespace AstroForge.App.Services;

public sealed class AppState
{
    public int SchemaVersion { get; set; } = 2;
    public List<string> SourcePaths { get; set; } = [];
    public string LibraryPath { get; set; } = "";
    public List<MasterLibraryDefinition> MasterLibraries { get; set; } = [];
    public string DestinationPath { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public int SessionBoundaryHour { get; set; } = 12;
    public double? ProjectDefaultGain { get; set; } = 100;
    public double? ProjectDefaultOffset { get; set; } = 51;
    public double? ProjectDefaultTemperatureC { get; set; }
    public string LastProjectFile { get; set; } = "";
    public string UiDensity { get; set; } = "Comoda";
    public string UiLanguage { get; set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "it" ? UiLocalization.Italian : UiLocalization.English;
    public bool ReducedMotion { get; set; }
    public bool CheckForUpdates { get; set; }
    public string UpdateChannel { get; set; } = "Beta";
    public double ExportMarginPercent { get; set; } = 10;
    public double ExportMinimumReserveGiB { get; set; } = 1;
    public double ExportEstimatedThroughputMiBps { get; set; } = 100;
    public List<string> ExcludedQualityPaths { get; set; } = [];
    public double QualitySigmaThreshold { get; set; } = 3.5;
    public double QualityStretchStrength { get; set; } = 6;
    public bool QualityDebayerPreview { get; set; }
    public double SourcePanelWidth { get; set; } = 260;
    public double InspectorPanelWidth { get; set; } = 390;
    public bool HasCompletedOnboarding { get; set; }
    public Dictionary<string, FrameOverrides> Overrides { get; set; } = new(PathIdentity.Comparer);
}

public sealed class MasterLibraryDefinition
{
    public string Name { get; set; } = "Libreria Master";
    public string Path { get; set; } = "";
    public int Priority { get; set; } = 1;
    public bool Enabled { get; set; } = true;
}

public sealed class FrameOverrides
{
    public bool HasGain { get; set; }
    public double? Gain { get; set; }
    public bool HasOffset { get; set; }
    public double? Offset { get; set; }
    public bool HasTemperature { get; set; }
    public double? Temperature { get; set; }
    public bool HasFilter { get; set; }
    public string? Filter { get; set; }
    public bool HasFlatSet { get; set; }
    public string? FlatSet { get; set; }
    public bool HasSession { get; set; }
    public string? Session { get; set; }
    public bool HasManualDark { get; set; }
    public string? ManualDarkPath { get; set; }
    public bool HasManualBias { get; set; }
    public string? ManualBiasPath { get; set; }
    public FrameKind? Kind { get; set; }
}

public static class AppStateStore
{
    private static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AstroProjectForge");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "state.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppState Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var state = JsonSerializer.Deserialize<AppState>(SettingsMigration.Migrate(File.ReadAllText(FilePath)), Options) ?? new();
            state.Overrides = new(state.Overrides, PathIdentity.Comparer);
            if (state.UpdateChannel is not ("Stable" or "Beta")) state.UpdateChannel = "Beta";
            return state;
        }
        catch { return new(); }
    }

    public static void Save(AppState state)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, Options));
        File.Move(temporary, FilePath, true);
    }

    public static void Apply(FrameMetadata frame, FrameOverrides value)
    {
        if (value.HasGain) frame.Gain.SetOverride(value.Gain);
        if (value.HasOffset) frame.Offset.SetOverride(value.Offset);
        if (value.HasTemperature) frame.SetTemperatureC.SetOverride(value.Temperature);
        if (value.HasFilter) frame.FilterName.SetOverride(value.Filter);
        if (value.HasFlatSet) frame.FlatSetId.SetOverride(value.FlatSet);
        if (value.HasSession) frame.SessionId.SetOverride(value.Session);
        if (value.HasManualDark) frame.ManualDarkPath.SetOverride(value.ManualDarkPath);
        if (value.HasManualBias) frame.ManualBiasPath.SetOverride(value.ManualBiasPath);
        if (value.Kind is { } kind) frame.Kind = kind;
    }

    public static FrameOverrides Snapshot(FrameMetadata frame) => new()
    {
        HasGain = frame.Gain.HasOverride, Gain = frame.Gain.OverrideValue,
        HasOffset = frame.Offset.HasOverride, Offset = frame.Offset.OverrideValue,
        HasTemperature = frame.SetTemperatureC.HasOverride, Temperature = frame.SetTemperatureC.OverrideValue,
        HasFilter = frame.FilterName.HasOverride, Filter = frame.FilterName.OverrideValue,
        HasFlatSet = frame.FlatSetId.HasOverride, FlatSet = frame.FlatSetId.OverrideValue,
        HasSession = frame.SessionId.HasOverride, Session = frame.SessionId.OverrideValue,
        HasManualDark = frame.ManualDarkPath.HasOverride, ManualDarkPath = frame.ManualDarkPath.OverrideValue,
        HasManualBias = frame.ManualBiasPath.HasOverride, ManualBiasPath = frame.ManualBiasPath.OverrideValue,
        Kind = frame.Kind
    };
}
