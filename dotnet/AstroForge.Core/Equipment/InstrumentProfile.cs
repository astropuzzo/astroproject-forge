using System.Globalization;
using AstroForge.Core.Filters;
using AstroForge.Core.Models;

namespace AstroForge.Core.Equipment;

/// <summary>One filter as the project uses it: the name the capture software wrote, what Forge thinks it is, and how much data it carries.</summary>
public sealed record InstrumentFilter(string RawName, FilterIdentity Identity, int Lights, int Flats, double IntegrationSeconds)
{
    public bool NeedsConfirmation => Identity.NeedsConfirmation;
}

/// <summary>
/// The imaging train reconstructed from the project's headers: camera, optics, sensor geometry and
/// the filters on the wheel. Built once per analysis; confirmations live in the per-camera wheel profile.
/// </summary>
public sealed record InstrumentProfile(
    string CameraKey,
    CameraIdentity Camera,
    TelescopeIdentity Telescope,
    double? PixelUm,
    int? WidthPx,
    int? HeightPx,
    int Binning,
    IReadOnlyList<InstrumentFilter> Filters)
{
    public double? FocalMm => Telescope.FocalMm;
    public double? SensorWidthMm => PixelUm is { } pixel && WidthPx is { } width ? pixel * width * Binning / 1000 : null;
    public double? SensorHeightMm => PixelUm is { } pixel && HeightPx is { } height ? pixel * height * Binning / 1000 : null;
    public double? ImageScale => PixelUm is { } pixel && FocalMm is > 0 ? EquipmentRecognizer.ImageScale(pixel, FocalMm.Value, Binning) : null;

    /// <summary>Field of view in degrees (width, height).</summary>
    public (double Width, double Height)? FieldOfView => SensorWidthMm is { } w && SensorHeightMm is { } h && FocalMm is > 0
        ? (2 * Math.Atan(w / (2 * FocalMm.Value)) * 180 / Math.PI, 2 * Math.Atan(h / (2 * FocalMm.Value)) * 180 / Math.PI)
        : null;

    public int PendingConfirmations => Filters.Count(filter => filter.NeedsConfirmation);

    public static InstrumentProfile? Build(IEnumerable<FrameMetadata> frames, Func<string, IReadOnlyDictionary<string, string>?>? wheelProfiles = null)
    {
        var lights = frames.Where(frame => !frame.IsMaster && frame.Kind is FrameKind.Light or FrameKind.Flat).ToList();
        if (lights.Count == 0) return null;

        // The camera that shot most Lights defines the profile; mixed rigs show their main train.
        var cameraName = Mode(lights.Where(frame => frame.Kind == FrameKind.Light).Select(frame => frame.Camera.Value)) ?? Mode(lights.Select(frame => frame.Camera.Value)) ?? "";
        var ownFrames = lights.Where(frame => string.Equals(frame.Camera.Value ?? "", cameraName, StringComparison.OrdinalIgnoreCase)).ToList();
        var camera = EquipmentRecognizer.Camera(cameraName);
        var key = CameraKeyFor(camera);
        var profile = wheelProfiles?.Invoke(key);

        var telescopeName = Mode(ownFrames.Select(frame => Text(frame, "TELESCOP")));
        var focal = Mode(ownFrames.Select(frame => frame.FocalLengthMm.Value));
        var telescope = EquipmentRecognizer.Telescope(telescopeName, focal);
        var binning = Mode(ownFrames.Select(frame => frame.XBin.Value)) ?? 1;
        var headerPixel = Mode(ownFrames.Select(frame => Number(frame, "XPIXSZ")));
        // XPIXSZ is usually already binned; the catalogue gives the native pitch.
        var pixel = headerPixel is { } binned ? binned / Math.Max(1, binning) : camera.Camera?.PixelUm;
        var width = Mode(ownFrames.Select(frame => frame.Width.Value)) ?? camera.Camera?.Width;
        var height = Mode(ownFrames.Select(frame => frame.Height.Value)) ?? camera.Camera?.Height;

        var filters = ownFrames
            .GroupBy(frame => (frame.FilterName.Value ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0 || camera.IsColor)
            .Select(group => new InstrumentFilter(
                group.Key,
                FilterRecognizer.Recognize(group.Key.Length == 0 ? null : group.Key, camera.IsColor, profile),
                group.Count(frame => frame.Kind == FrameKind.Light),
                group.Count(frame => frame.Kind == FrameKind.Flat),
                group.Where(frame => frame.Kind == FrameKind.Light).Sum(frame => frame.ExposureSeconds.Value ?? 0)))
            .OrderByDescending(filter => filter.IntegrationSeconds)
            .ThenBy(filter => filter.RawName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new InstrumentProfile(key, camera, telescope, pixel, width, height, binning, filters);
    }

    /// <summary>Stable key for a camera's wheel profile: the catalogue id when known, the header name otherwise.</summary>
    public static string CameraKeyFor(CameraIdentity camera) =>
        camera.Camera?.Id ?? (camera.RawName.Length == 0 ? "unknown" : FilterRecognizer.Normalize(camera.RawName));

    private static T? Mode<T>(IEnumerable<T?> values) where T : struct =>
        values.Where(value => value.HasValue).GroupBy(value => value!.Value).OrderByDescending(group => group.Count()).Select(group => (T?)group.Key).FirstOrDefault();

    private static string? Mode(IEnumerable<string?> values) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value)).GroupBy(value => value!.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count()).Select(group => group.Key).FirstOrDefault();

    private static string? Text(FrameMetadata frame, string key) =>
        frame.Headers.TryGetValue(key, out var value) ? value?.ToString()?.Trim() : null;

    private static double? Number(FrameMetadata frame, string key) => frame.Headers.TryGetValue(key, out var value) ? value switch
    {
        double number => number,
        float number => number,
        long number => number,
        int number => number,
        string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null
    } : null;
}
