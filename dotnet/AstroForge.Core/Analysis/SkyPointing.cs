using System.Globalization;
using System.Text.RegularExpressions;
using AstroForge.Core.Models;

namespace AstroForge.Core.Analysis;

/// <summary>Where a frame was pointing: the centre of its field on the sky (ICRS/J2000, degrees) and, when the file says so, how the sensor was turned.</summary>
/// <param name="PositionAngleDeg">Position angle of the sensor's "up" direction, degrees from north through east; null when the file does not say.</param>
/// <param name="Source">Where the position came from: the solved WCS, the telescope's RA/DEC or the object's coordinates.</param>
public sealed record SkyPointing(double RaDeg, double DecDeg, double? PositionAngleDeg, string Source);

/// <summary>One panel of the framing: the Lights that point at the same place, where that is and what it holds.</summary>
public sealed record SkyPanel(double RaDeg, double DecDeg, double? PositionAngleDeg, int Lights, double IntegrationSeconds, int Nights, string Label);

/// <summary>Reads where the frames pointed and lays the sky out flat around a point (gnomonic projection, the one astronomical images use).</summary>
public static partial class SkyPointings
{
    /// <summary>
    /// The centre of the field, in order of trust: the solved WCS (CRVAL), the mount's RA/DEC, the target's OBJCTRA/OBJCTDEC.
    /// Degrees or sexagesimal text both work; (0, 0) is how some software says "unknown" and is not taken as a place.
    /// </summary>
    public static SkyPointing? FromHeaders(IReadOnlyDictionary<string, object?> headers)
    {
        (double Ra, double Dec, string Source)? centre = null;
        if (Text(headers, "CTYPE1") is { } ctype && ctype.Contains("RA", StringComparison.OrdinalIgnoreCase)
            && Degrees(headers, "CRVAL1", hours: false) is { } wcsRa && Degrees(headers, "CRVAL2", hours: false) is { } wcsDec)
            centre = (wcsRa, wcsDec, "wcs");
        if (centre is null && Degrees(headers, "RA", hours: false) is { } ra && Degrees(headers, "DEC", hours: false) is { } dec)
            centre = (ra, dec, "mount");
        if (centre is null && Degrees(headers, "OBJCTRA", hours: true) is { } objectRa && Degrees(headers, "OBJCTDEC", hours: false) is { } objectDec)
            centre = (objectRa, objectDec, "object");
        if (centre is not { } found) return null;

        var rightAscension = ((found.Ra % 360) + 360) % 360;
        if (found.Dec is < -90 or > 90 || (Math.Abs(rightAscension) < 1e-9 && Math.Abs(found.Dec) < 1e-9)) return null;
        return new SkyPointing(rightAscension, found.Dec, PositionAngle(headers), found.Source);
    }

    /// <summary>
    /// Position angle of the sensor's up direction (degrees east of north). From the WCS matrix when there is one:
    /// the image's +y axis maps to (CD1_2, CD2_2) on (east, north). Otherwise the rotation the software declares.
    /// </summary>
    public static double? PositionAngle(IReadOnlyDictionary<string, object?> headers)
    {
        if (Number(headers, "CD1_2") is { } cd12 && Number(headers, "CD2_2") is { } cd22 && (cd12 != 0 || cd22 != 0))
            return Normalise(Math.Atan2(cd12, cd22) * 180 / Math.PI);
        if (Number(headers, "CROTA2") is { } crota) return Normalise(-crota);
        foreach (var key in new[] { "OBJCTROT", "POSANGLE", "PA" })
            if (Number(headers, key) is { } angle) return Normalise(angle);
        return null;
    }

    /// <summary>
    /// Groups Lights by where they point. Two Lights are the same panel when they are closer than <paramref name="toleranceDeg"/>
    /// (dithering and a night's re-centring stay inside a panel; a mosaic's panels do not). Panels read from east to west, north first.
    /// </summary>
    public static IReadOnlyList<SkyPanel> Panels(IEnumerable<FrameMetadata> lights, double toleranceDeg)
    {
        var groups = new List<Group>();
        foreach (var light in lights)
        {
            if (FromHeaders(light.Headers) is not { } pointing) continue;
            var group = groups.FirstOrDefault(candidate => Separation(candidate.Ra, candidate.Dec, pointing.RaDeg, pointing.DecDeg) <= toleranceDeg);
            if (group is null) groups.Add(group = new Group());
            group.Add(pointing, light.ExposureSeconds.Value ?? 0, light.SessionId.Value ?? light.CapturedAt.Value?.Date.ToString("yyyy-MM-dd") ?? "");
        }

        var ordered = groups.OrderByDescending(group => Math.Round(group.Dec / Math.Max(toleranceDeg, 0.01))).ThenByDescending(group => group.Ra).ToList();
        return ordered.Select((group, index) => new SkyPanel(group.Ra, group.Dec, group.PositionAngle, group.Lights, group.Seconds, group.Nights.Count,
            ordered.Count == 1 ? "" : $"P{index + 1}")).ToList();
    }

    /// <summary>
    /// The panels of the framing that is being shown: the one with most Lights and those within <paramref name="reachDeg"/> of it (the panels of a mosaic are a field or two apart).
    /// A panel farther away is another target in the same project and is left out; the main panel comes first, then the others by Lights.
    /// </summary>
    public static IReadOnlyList<SkyPanel> Mosaic(IEnumerable<SkyPanel> panels, double reachDeg, int maxPanels = 12)
    {
        var ordered = panels.OrderByDescending(panel => panel.Lights).ToList();
        if (ordered.Count == 0) return ordered;
        var main = ordered[0];
        return ordered.Where(panel => Separation(main.RaDeg, main.DecDeg, panel.RaDeg, panel.DecDeg) <= reachDeg).Take(maxPanels).ToList();
    }

    /// <summary>Angular distance between two points of the sky, degrees (haversine).</summary>
    public static double Separation(double ra1, double dec1, double ra2, double dec2)
    {
        var (a1, d1, a2, d2) = (ra1 * Math.PI / 180, dec1 * Math.PI / 180, ra2 * Math.PI / 180, dec2 * Math.PI / 180);
        var h = Math.Pow(Math.Sin((d2 - d1) / 2), 2) + Math.Cos(d1) * Math.Cos(d2) * Math.Pow(Math.Sin((a2 - a1) / 2), 2);
        return 2 * Math.Asin(Math.Min(1, Math.Sqrt(h))) * 180 / Math.PI;
    }

    /// <summary>
    /// Where a point of the sky falls on the flat picture around (ra0, dec0): degrees towards the east (xi) and the north (eta).
    /// A DSS or HiPS image centred there has the same projection, so positions computed this way land on its stars.
    /// </summary>
    public static (double Xi, double Eta) Gnomonic(double ra, double dec, double ra0, double dec0)
    {
        var (a, d, a0, d0) = (ra * Math.PI / 180, dec * Math.PI / 180, ra0 * Math.PI / 180, dec0 * Math.PI / 180);
        var denominator = Math.Sin(d) * Math.Sin(d0) + Math.Cos(d) * Math.Cos(d0) * Math.Cos(a - a0);
        var xi = Math.Cos(d) * Math.Sin(a - a0) / denominator;
        var eta = (Math.Sin(d) * Math.Cos(d0) - Math.Cos(d) * Math.Sin(d0) * Math.Cos(a - a0)) / denominator;
        return (xi * 180 / Math.PI, eta * 180 / Math.PI);
    }

    /// <summary>The point of the sky that falls at (xi, eta) degrees east and north of (ra0, dec0): the inverse of <see cref="Gnomonic"/>.</summary>
    public static (double Ra, double Dec) FromGnomonic(double xi, double eta, double ra0, double dec0)
    {
        var (x, y, a0, d0) = (xi * Math.PI / 180, eta * Math.PI / 180, ra0 * Math.PI / 180, dec0 * Math.PI / 180);
        var rho = Math.Sqrt(x * x + y * y);
        if (rho < 1e-12) return (ra0, dec0);
        var c = Math.Atan(rho);
        var dec = Math.Asin(Math.Cos(c) * Math.Sin(d0) + y * Math.Sin(c) * Math.Cos(d0) / rho);
        var ra = a0 + Math.Atan2(x * Math.Sin(c), rho * Math.Cos(d0) * Math.Cos(c) - y * Math.Sin(d0) * Math.Sin(c));
        return (((ra * 180 / Math.PI) % 360 + 360) % 360, dec * 180 / Math.PI);
    }

    /// <summary>The middle of several points of the sky (the mean of their directions, right even across 0h).</summary>
    public static (double Ra, double Dec) Centre(IEnumerable<(double Ra, double Dec)> points)
    {
        double x = 0, y = 0, z = 0;
        foreach (var (ra, dec) in points)
        {
            var (a, d) = (ra * Math.PI / 180, dec * Math.PI / 180);
            x += Math.Cos(d) * Math.Cos(a); y += Math.Cos(d) * Math.Sin(a); z += Math.Sin(d);
        }
        return (((Math.Atan2(y, x) * 180 / Math.PI) + 360) % 360, Math.Atan2(z, Math.Sqrt(x * x + y * y)) * 180 / Math.PI);
    }

    /// <summary>
    /// The four corners of a sensor footprint on the flat picture (degrees east and north of its origin), going round from the top-left of the sensor:
    /// centred on (xi, eta), width x height degrees, turned so that its up side points at <paramref name="positionAngleDeg"/> east of north.
    /// </summary>
    public static (double Xi, double Eta)[] Corners(double xi, double eta, double widthDeg, double heightDeg, double positionAngleDeg)
    {
        var angle = positionAngleDeg * Math.PI / 180;
        var (upX, upY) = (Math.Sin(angle), Math.Cos(angle));
        var (rightX, rightY) = (-Math.Cos(angle), Math.Sin(angle));
        (double, double) At(int sx, int sy) => (xi + sx * widthDeg / 2 * rightX + sy * heightDeg / 2 * upX, eta + sx * widthDeg / 2 * rightY + sy * heightDeg / 2 * upY);
        return [At(-1, 1), At(1, 1), At(1, -1), At(-1, -1)];
    }

    public static string FormatRa(double raDeg)
    {
        var hours = raDeg / 15;
        var h = (int)Math.Floor(hours);
        var minutes = (hours - h) * 60;
        return string.Create(CultureInfo.InvariantCulture, $"{h}h {minutes:00.0}m");
    }

    public static string FormatDec(double decDeg)
    {
        var sign = decDeg < 0 ? "−" : "+";
        var absolute = Math.Abs(decDeg);
        var d = (int)Math.Floor(absolute);
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{d}° {(absolute - d) * 60:00}′");
    }

    // ---- header values ----

    private static double Normalise(double angle)
    {
        angle %= 360;
        return angle > 180 ? angle - 360 : angle <= -180 ? angle + 360 : angle;
    }

    private static string? Text(IReadOnlyDictionary<string, object?> headers, string key) =>
        headers.TryGetValue(key, out var value) && value is not null ? Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim().Trim('\'').Trim() : null;

    private static double? Number(IReadOnlyDictionary<string, object?> headers, string key) =>
        headers.TryGetValue(key, out var value) ? value switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            string s when double.TryParse(s.Trim().Trim('\''), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        } : null;

    /// <summary>A coordinate as degrees: a number is degrees already; text such as "20 56 24.5" or "+31:43:00" is hours (for a right ascension) or degrees.</summary>
    private static double? Degrees(IReadOnlyDictionary<string, object?> headers, string key, bool hours)
    {
        if (!headers.TryGetValue(key, out var value) || value is null) return null;
        if (value is double or float or int or long) return Convert.ToDouble(value, CultureInfo.InvariantCulture) is var number && double.IsFinite(number) ? number : null;
        var text = (Convert.ToString(value, CultureInfo.InvariantCulture) ?? "").Trim().Trim('\'').Trim();
        var parts = NumberPattern().Matches(text).Select(match => double.Parse(match.Value, CultureInfo.InvariantCulture)).ToArray();
        if (parts.Length == 0) return null;
        // One number is a plain angle (even for a right ascension: degrees, as N.I.N.A. and ASIAIR write RA); several are hours/degrees, minutes, seconds.
        if (parts.Length == 1 && !text.Contains('h', StringComparison.OrdinalIgnoreCase)) return parts[0];
        var negative = text.TrimStart().StartsWith('-') || parts[0] < 0;
        var magnitude = Math.Abs(parts[0]) + (parts.Length > 1 ? Math.Abs(parts[1]) / 60 : 0) + (parts.Length > 2 ? Math.Abs(parts[2]) / 3600 : 0);
        return (negative ? -magnitude : magnitude) * (hours ? 15 : 1);
    }

    [GeneratedRegex(@"[-+]?\d+(?:\.\d+)?")]
    private static partial Regex NumberPattern();

    private sealed class Group
    {
        private double _x, _y, _z, _sin, _cos;
        public int Lights { get; private set; }
        public double Seconds { get; private set; }
        public HashSet<string> Nights { get; } = [];
        public double Ra => ((Math.Atan2(_y, _x) * 180 / Math.PI) + 360) % 360;
        public double Dec => Math.Atan2(_z, Math.Sqrt(_x * _x + _y * _y)) * 180 / Math.PI;
        public double? PositionAngle { get; private set; }
        private int _angles;

        public void Add(SkyPointing pointing, double seconds, string night)
        {
            var (a, d) = (pointing.RaDeg * Math.PI / 180, pointing.DecDeg * Math.PI / 180);
            _x += Math.Cos(d) * Math.Cos(a); _y += Math.Cos(d) * Math.Sin(a); _z += Math.Sin(d);
            if (pointing.PositionAngleDeg is { } angle)
            {
                _sin += Math.Sin(angle * Math.PI / 180); _cos += Math.Cos(angle * Math.PI / 180); _angles++;
                PositionAngle = Normalise(Math.Atan2(_sin, _cos) * 180 / Math.PI);
            }
            Lights++;
            Seconds += seconds;
            Nights.Add(night);
        }
    }
}
