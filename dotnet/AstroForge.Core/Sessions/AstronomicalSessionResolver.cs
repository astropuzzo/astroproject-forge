using AstroForge.Core.Models;

namespace AstroForge.Core.Sessions;

public sealed record SessionSettings(TimeZoneInfo TimeZone, TimeOnly DayBoundary)
{
    public static SessionSettings DefaultForLocalMachine() => new(TimeZoneInfo.Local, new TimeOnly(12, 0));
}

public static class AstronomicalSessionResolver
{
    public static string Resolve(DateTimeOffset timestamp, SessionSettings settings)
    {
        var local = TimeZoneInfo.ConvertTime(timestamp, settings.TimeZone);
        var date = local.TimeOfDay < settings.DayBoundary.ToTimeSpan()
            ? local.Date.AddDays(-1)
            : local.Date;
        return date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static void Apply(FrameMetadata frame, SessionSettings settings)
    {
        if (frame.CapturedAt.Value is { } timestamp)
            frame.SessionId.SetOriginal(Resolve(timestamp, settings), MetadataSource.Inferred);
    }
}

