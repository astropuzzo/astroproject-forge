using AstroForge.Core.Models;

namespace AstroForge.Core.Analysis;

public sealed record ProjectStatistics(
    int LightCount,
    double ExposureSeconds,
    int FilterCount,
    int ConfigurationSessionCount,
    int NightCount,
    DateTimeOffset? FirstCapture,
    DateTimeOffset? LastCapture,
    IReadOnlyList<FilterStatistics> Filters,
    IReadOnlyList<ConfigurationSessionStatistics> Sessions,
    IReadOnlyList<NightStatistics> Nights)
{
    public double ExposureHours => ExposureSeconds / 3600d;
}

public sealed record FilterStatistics(
    string Filter,
    int LightCount,
    double ExposureSeconds,
    int ConfigurationSessionCount,
    int NightCount,
    int FlatFrameCount,
    int DarkMasterCount,
    int BiasMasterCount,
    int UnresolvedCalibrationCount,
    IReadOnlyList<double> Gains,
    double? MinimumTemperatureC,
    double? MaximumTemperatureC,
    DateTimeOffset? FirstCapture,
    DateTimeOffset? LastCapture)
{
    public double ExposureHours => ExposureSeconds / 3600d;
    public double AverageExposureSeconds => LightCount == 0 ? 0 : ExposureSeconds / LightCount;
    public bool Ready => UnresolvedCalibrationCount == 0;
}

public sealed record ConfigurationSessionStatistics(
    string Filter,
    string Session,
    int LightCount,
    double ExposureSeconds,
    int NightCount,
    int FlatFrameCount,
    string DarkMasters,
    string BiasMasters,
    int UnresolvedCalibrationCount,
    DateTimeOffset? FirstCapture,
    DateTimeOffset? LastCapture)
{
    public double ExposureHours => ExposureSeconds / 3600d;
    public bool Ready => UnresolvedCalibrationCount == 0;
}

public sealed record NightStatistics(
    string Filter,
    string ConfigurationSession,
    string Night,
    int LightCount,
    double ExposureSeconds,
    double AverageExposureSeconds,
    double? MinimumTemperatureC,
    double? MaximumTemperatureC,
    int IssueCount)
{
    public double ExposureHours => ExposureSeconds / 3600d;
}

public static class ProjectStatisticsCalculator
{
    public static ProjectStatistics Calculate(ProjectAnalysis analysis)
    {
        var items = analysis.Lights.ToArray();
        var filters = items.GroupBy(item => Display(item.Light.FilterName.Value)).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => Filter(group.Key, group.ToArray())).ToArray();
        var sessions = items.GroupBy(item => new { Filter = Display(item.Light.FilterName.Value), Session = item.FlatGroup?.Id ?? "IRRISOLTA" })
            .OrderBy(group => group.Key.Filter, StringComparer.OrdinalIgnoreCase).ThenBy(group => First(group.Select(item => item.Light)))
            .Select(group => Session(group.Key.Filter, group.Key.Session, group.ToArray())).ToArray();
        var nights = items.GroupBy(item => new
            {
                Filter = Display(item.Light.FilterName.Value),
                Session = item.FlatGroup?.Id ?? "IRRISOLTA",
                Night = item.Light.SessionId.Value ?? "Notte non definita"
            })
            .OrderBy(group => group.Key.Filter, StringComparer.OrdinalIgnoreCase).ThenBy(group => group.Key.Night, StringComparer.OrdinalIgnoreCase)
            .Select(group => Night(group.Key.Filter, group.Key.Session, group.Key.Night, group.ToArray())).ToArray();
        var captures = items.Select(item => item.Light.CapturedAt.Value).Where(value => value.HasValue).Select(value => value!.Value).OrderBy(value => value).ToArray();
        return new(
            items.Length,
            items.Sum(item => item.Light.ExposureSeconds.Value ?? 0),
            filters.Length,
            sessions.Length,
            nights.Length,
            captures.Length == 0 ? null : captures[0],
            captures.Length == 0 ? null : captures[^1],
            filters,
            sessions,
            nights);
    }

    private static FilterStatistics Filter(string filter, LightCalibrationAnalysis[] items)
    {
        var frames = items.Select(item => item.Light).ToArray();
        var temperatures = frames.Select(frame => frame.EffectiveTemperatureC).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return new(
            filter,
            frames.Length,
            frames.Sum(frame => frame.ExposureSeconds.Value ?? 0),
            items.Select(item => item.FlatGroup?.Id ?? "IRRISOLTA").Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            frames.Select(frame => frame.SessionId.Value ?? "Notte non definita").Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            items.Where(item => item.FlatGroup is not null).SelectMany(item => item.FlatGroup!.Frames).Distinct().Count(),
            items.Select(item => item.Dark.Selected?.Frame).Where(frame => frame is not null).Distinct().Count(),
            items.Select(item => item.Bias.Selected?.Frame).Where(frame => frame is not null).Distinct().Count(),
            items.Sum(Unresolved),
            frames.Select(frame => frame.Gain.Value).Where(value => value.HasValue).Select(value => value!.Value).Distinct().OrderBy(value => value).ToArray(),
            temperatures.Length == 0 ? null : temperatures.Min(),
            temperatures.Length == 0 ? null : temperatures.Max(),
            First(frames), Last(frames));
    }

    private static ConfigurationSessionStatistics Session(string filter, string session, LightCalibrationAnalysis[] items)
    {
        var frames = items.Select(item => item.Light).ToArray();
        return new(
            filter,
            session,
            frames.Length,
            frames.Sum(frame => frame.ExposureSeconds.Value ?? 0),
            frames.Select(frame => frame.SessionId.Value ?? "Notte non definita").Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            items.Where(item => item.FlatGroup is not null).SelectMany(item => item.FlatGroup!.Frames).Distinct().Count(),
            JoinNames(items.Select(item => item.Dark.Selected?.Frame)),
            JoinNames(items.Select(item => item.Bias.Selected?.Frame)),
            items.Sum(Unresolved),
            First(frames), Last(frames));
    }

    private static NightStatistics Night(string filter, string session, string night, LightCalibrationAnalysis[] items)
    {
        var frames = items.Select(item => item.Light).ToArray();
        var exposure = frames.Sum(frame => frame.ExposureSeconds.Value ?? 0);
        var temperatures = frames.Select(frame => frame.EffectiveTemperatureC).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return new(
            filter, session, night, frames.Length, exposure, frames.Length == 0 ? 0 : exposure / frames.Length,
            temperatures.Length == 0 ? null : temperatures.Min(), temperatures.Length == 0 ? null : temperatures.Max(),
            frames.Sum(frame => frame.Issues.Count));
    }

    private static int Unresolved(LightCalibrationAnalysis item) => new[] { item.Flat, item.Dark, item.Bias }.Count(result => !result.IsAccepted);
    private static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "Senza filtro" : value.Trim();
    private static DateTimeOffset? First(IEnumerable<FrameMetadata> frames) { var values = Times(frames); return values.Length == 0 ? null : values[0]; }
    private static DateTimeOffset? Last(IEnumerable<FrameMetadata> frames) { var values = Times(frames); return values.Length == 0 ? null : values[^1]; }
    private static DateTimeOffset[] Times(IEnumerable<FrameMetadata> frames) => frames.Select(frame => frame.CapturedAt.Value).Where(value => value.HasValue).Select(value => value!.Value).OrderBy(value => value).ToArray();
    private static string JoinNames(IEnumerable<FrameMetadata?> frames)
    {
        var names = frames.Where(frame => frame is not null).Select(frame => frame!.FileName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return names.Length == 0 ? "—" : string.Join(", ", names);
    }
}
