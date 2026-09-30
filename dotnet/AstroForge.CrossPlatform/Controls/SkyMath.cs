using System.Globalization;
using AstroForge.Core.Models;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>Low-precision Sun and Moon, good to a fraction of a degree: enough to shade twilight and draw the Moon's phase.</summary>
public static class SkyMath
{
    private const double SynodicMonth = 29.530588853;
    // New Moon of 6 January 2000, 18:14 UTC.
    private static readonly DateTime NewMoonEpoch = new(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);

    /// <summary>Days since the last new Moon, 0 to 29.53.</summary>
    public static double MoonAge(DateTime utc)
    {
        var days = (utc - NewMoonEpoch).TotalDays % SynodicMonth;
        return days < 0 ? days + SynodicMonth : days;
    }

    /// <summary>Illuminated fraction of the Moon's disc, 0 (new) to 1 (full).</summary>
    public static double MoonIllumination(DateTime utc) => (1 - Math.Cos(2 * Math.PI * MoonAge(utc) / SynodicMonth)) / 2;

    /// <summary>Altitude of the Sun in degrees for an observer at <paramref name="latitude"/>, <paramref name="longitude"/> (east positive).</summary>
    public static double SunAltitude(DateTime utc, double latitude, double longitude)
    {
        var d = (utc - new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc)).TotalDays;
        var g = Radians(357.529 + 0.98560028 * d);
        var q = 280.459 + 0.98564736 * d;
        var l = Radians(q + 1.915 * Math.Sin(g) + 0.020 * Math.Sin(2 * g));
        var e = Radians(23.439 - 0.00000036 * d);
        var ra = Math.Atan2(Math.Cos(e) * Math.Sin(l), Math.Cos(l));
        var dec = Math.Asin(Math.Sin(e) * Math.Sin(l));
        var gmst = (18.697374558 + 24.06570982441908 * d) % 24;
        var hourAngle = Radians(gmst * 15 + longitude) - ra;
        var lat = Radians(latitude);
        return Math.Asin(Math.Sin(lat) * Math.Sin(dec) + Math.Cos(lat) * Math.Cos(dec) * Math.Cos(hourAngle)) * 180 / Math.PI;
    }

    /// <summary>Observer site from SITELAT/SITELONG (decimal or sexagesimal), when the capture software wrote it.</summary>
    public static (double Latitude, double Longitude)? SiteOf(IEnumerable<FrameMetadata> frames)
    {
        foreach (var frame in frames)
            if (Angle(frame.Headers.GetValueOrDefault("SITELAT")) is { } lat && Angle(frame.Headers.GetValueOrDefault("SITELONG")) is { } lon
                && Math.Abs(lat) <= 90 && Math.Abs(lon) <= 360 && (lat != 0 || lon != 0))
                return (lat, lon > 180 ? lon - 360 : lon);
        return null;
    }

    private static double? Angle(object? value)
    {
        switch (value)
        {
            case double number: return number;
            case float number: return number;
            case int number: return number;
            case long number: return number;
            case string text:
                text = text.Trim().Trim('\'').Trim();
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return parsed;
                var parts = text.Replace(':', ' ').Replace('°', ' ').Replace('\'', ' ').Replace('"', ' ')
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length is < 2 or > 3) return null;
                var values = new double[3];
                for (var index = 0; index < parts.Length; index++)
                    if (!double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out values[index])) return null;
                var sign = parts[0].StartsWith('-') ? -1 : 1;
                return sign * (Math.Abs(values[0]) + values[1] / 60 + values[2] / 3600);
            default: return null;
        }
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;
}
