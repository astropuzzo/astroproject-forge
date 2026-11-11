using AstroForge.Core.Models;

namespace AstroForge.Core.Sessions;

public sealed record SessionSettings(TimeZoneInfo TimeZone, TimeOnly DayBoundary)
{
    public static SessionSettings DefaultForLocalMachine() => new(TimeZoneInfo.Local, new TimeOnly(12, 0));
}

public static class AstronomicalSessionResolver
{
    public static string Resolve(DateTimeOffset timestamp, SessionSettings settings, TimeSpan? siteOffset = null)
    {
        var local = LocalTime(timestamp, settings.TimeZone, siteOffset);
        var date = local.TimeOfDay < settings.DayBoundary.ToTimeSpan()
            ? local.Date.AddDays(-1)
            : local.Date;
        return date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The wall clock where the frame was taken: the site's own offset when the capture software recorded it,
    /// otherwise this machine's zone. A remote observatory keeps its own noon, so its nights are not cut in two.
    /// </summary>
    public static DateTime LocalTime(DateTimeOffset timestamp, TimeZoneInfo zone, TimeSpan? siteOffset = null) =>
        siteOffset is { } offset ? timestamp.ToOffset(offset).DateTime : TimeZoneInfo.ConvertTime(timestamp, zone).DateTime;

    public static DateTime? LocalTime(FrameMetadata frame, TimeZoneInfo zone) =>
        frame.CapturedAt.Value is { } timestamp ? LocalTime(timestamp, zone, frame.SiteUtcOffset) : null;

    public static void Apply(FrameMetadata frame, SessionSettings settings)
    {
        if (frame.CapturedAt.Value is { } timestamp)
            frame.SessionId.SetOriginal(Resolve(timestamp, settings, frame.SiteUtcOffset), MetadataSource.Inferred);
    }
}

