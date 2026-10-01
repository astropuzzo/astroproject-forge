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
/// the filters on the wheel. Built once per analysis; confirmations live in the per-camera wheel profile, and the
/// user's equipment profile (<see cref="EquipmentOverride"/>) wins over headers and catalogue for optics and pixel size.
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
    /// <summary>Effective focal length, reducer included.</summary>
    public double? FocalMm => Telescope.FocalMm;
    /// <summary>Focal length of the bare optics: the user's value, the catalogue's, or FOCALLEN when the optics are unknown.</summary>
    public double? NativeFocalMm { get; init; }
    /// <summary>Reducer or barlow factor in the train; null when none is known.</summary>
    public double? ReducerFactor { get; init; }
    public double? ApertureMm { get; init; }
    public EquipmentSource TelescopeSource { get; init; }
    public EquipmentSource FocalSource { get; init; }
    public EquipmentSource PixelSource { get; init; }
    /// <summary>The user's equipment profile for this camera, when one was applied.</summary>
    public EquipmentOverride? Override { get; init; }
    /// <summary>The camera as the headers name it; <see cref="Camera"/> is the one the user said it really was, when they did.</summary>
    public CameraIdentity? DetectedCamera { get; init; }
    public EquipmentSource CameraSource { get; init; }
    public double? FocalRatio => ApertureMm is > 0 && FocalMm is > 0 ? FocalMm / ApertureMm : null;
    public double? SensorWidthMm => PixelUm is { } pixel && WidthPx is { } width ? pixel * width * Binning / 1000 : null;
    public double? SensorHeightMm => PixelUm is { } pixel && HeightPx is { } height ? pixel * height * Binning / 1000 : null;
    public double? ImageScale => FocalMm is { } focal ? ImageScaleAt(focal) : null;

    /// <summary>Field of view in degrees (width, height).</summary>
    public (double Width, double Height)? FieldOfView => FocalMm is { } focal ? FieldOfViewAt(focal) : null;

    /// <summary>Sampling in arcsec/px at another focal length (e.g. with or without the reducer).</summary>
    public double? ImageScaleAt(double focalMm) => PixelUm is { } pixel && focalMm > 0 ? EquipmentRecognizer.ImageScale(pixel, focalMm, Binning) : null;

    /// <summary>Field of view in degrees (width, height) at another focal length (e.g. with or without the reducer).</summary>
    public (double Width, double Height)? FieldOfViewAt(double focalMm) => SensorWidthMm is { } w && SensorHeightMm is { } h && focalMm > 0
        ? (2 * Math.Atan(w / (2 * focalMm)) * 180 / Math.PI, 2 * Math.Atan(h / (2 * focalMm)) * 180 / Math.PI)
        : null;

    public int PendingConfirmations => Filters.Count(filter => filter.NeedsConfirmation);

    public static InstrumentProfile? Build(IEnumerable<FrameMetadata> frames, Func<string, IReadOnlyDictionary<string, string>?>? wheelProfiles = null,
        Func<string, EquipmentOverride?>? equipmentProfiles = null, EquipmentCatalog? catalog = null)
    {
        catalog ??= EquipmentCatalog.Default;
        var lights = frames.Where(frame => !frame.IsMaster && frame.Kind is FrameKind.Light or FrameKind.Flat).ToList();
        if (lights.Count == 0) return null;

        // The camera that shot most Lights defines the profile; mixed rigs show their main train. The profile is keyed by the camera
        // as the headers name it, so what the user said about it keeps applying; the camera the user named wins for everything shown.
        var cameraName = Mode(lights.Where(frame => frame.Kind == FrameKind.Light).Select(CapturedCamera)) ?? Mode(lights.Select(CapturedCamera)) ?? "";
        var ownFrames = lights.Where(frame => string.Equals(CapturedCamera(frame) ?? "", cameraName, StringComparison.OrdinalIgnoreCase)).ToList();
        var detectedCamera = EquipmentRecognizer.Camera(cameraName, catalog);
        var key = CameraKeyFor(detectedCamera);
        var profile = wheelProfiles?.Invoke(key);
        var user = equipmentProfiles?.Invoke(key) is { IsEmpty: false } found ? found : null;
        var camera = user?.UserCamera(catalog) ?? detectedCamera;

        var telescopeName = Mode(ownFrames.Select(frame => Text(frame, "TELESCOP")));
        var focal = Mode(ownFrames.Select(frame => frame.FocalLengthMm.Value));
        var telescope = EquipmentRecognizer.Telescope(telescopeName, focal, catalog);
        var optics = new Optics(telescope, telescope.Telescope?.FocalMm ?? telescope.FocalMm, telescope.Reducer?.Factor, telescope.Telescope?.ApertureMm,
            telescope.IsRecognized ? EquipmentSource.Catalog : EquipmentSource.Header,
            focal is > 0 || telescope.FocalMm is null ? EquipmentSource.Header : EquipmentSource.Catalog);
        if (user is not null && (user.HasTelescope || user.ReducerFactor is > 0)) optics = ApplyOptics(user, optics, catalog);

        var binning = Mode(ownFrames.Select(frame => frame.XBin.Value)) ?? 1;
        var headerPixel = Mode(ownFrames.Select(frame => Number(frame, "XPIXSZ")));
        // XPIXSZ is usually already binned; the catalogue gives the native pitch. The user's pitch wins over both.
        var pixel = user?.PixelUm is > 0 ? user.PixelUm : headerPixel is { } binned ? binned / Math.Max(1, binning) : camera.Camera?.PixelUm;
        var pixelSource = user?.PixelUm is > 0 ? EquipmentSource.User : headerPixel is not null || pixel is null ? EquipmentSource.Header : EquipmentSource.Catalog;
        var width = Mode(ownFrames.Select(frame => frame.Width.Value)) ?? camera.Camera?.Width;
        var height = Mode(ownFrames.Select(frame => frame.Height.Value)) ?? camera.Camera?.Height;
        // Resampled frames keep the sensor's physical size but not its pitch: read them at the native pitch.
        if (user?.PixelUm is not > 0 && pixel is { } read && width is { } across && camera.Camera is { PixelUm: { } native, Width: { } nativeWidth }
            && Math.Abs(read - native) / native > 0.1 && Math.Abs(read * across * binning - native * nativeWidth) / (native * nativeWidth) < 0.03)
        {
            pixel = native;
            width = nativeWidth / Math.Max(1, binning);
            height = camera.Camera.Height is { } nativeHeight ? nativeHeight / Math.Max(1, binning) : height;
            pixelSource = EquipmentSource.Catalog;
        }

        var filters = ownFrames
            .GroupBy(frame => WheelLabel(frame), StringComparer.OrdinalIgnoreCase)
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

        return new InstrumentProfile(key, camera, optics.Telescope, pixel, width, height, binning, filters)
        {
            NativeFocalMm = optics.NativeFocalMm,
            ReducerFactor = optics.ReducerFactor,
            ApertureMm = optics.ApertureMm,
            TelescopeSource = optics.TelescopeSource,
            FocalSource = optics.FocalSource,
            PixelSource = pixelSource,
            Override = user,
            DetectedCamera = detectedCamera,
            CameraSource = user?.HasCamera == true ? EquipmentSource.User : detectedCamera.Camera is null ? EquipmentSource.Header : EquipmentSource.Catalog
        };
    }

    // INSTRUME as the capture software wrote it, before the user's profile renamed the camera.
    private static string? CapturedCamera(FrameMetadata frame) => frame.RawCameraName ?? frame.Camera.Value;

    private sealed record Optics(TelescopeIdentity Telescope, double? NativeFocalMm, double? ReducerFactor, double? ApertureMm,
        EquipmentSource TelescopeSource, EquipmentSource FocalSource);

    // The user's optics: a catalogue pick or a custom description, then native focal × reducer as the effective focal length.
    private static Optics ApplyOptics(EquipmentOverride user, Optics detected, EquipmentCatalog catalog)
    {
        var catalogTelescope = detected.Telescope.Telescope;
        var rawName = detected.Telescope.RawName;
        var (native, aperture, telescopeSource) = (detected.NativeFocalMm, detected.ApertureMm, detected.TelescopeSource);
        if (user.HasTelescope)
        {
            var named = !string.IsNullOrWhiteSpace(user.TelescopeId) || !string.IsNullOrWhiteSpace(user.TelescopeName);
            // A catalogue id or a custom name replaces the detected optics; aperture or focal alone refine them.
            if (named)
            {
                catalogTelescope = catalog.FindTelescope(user.TelescopeId);
                rawName = !string.IsNullOrWhiteSpace(user.TelescopeName) ? user.TelescopeName.Trim() : catalogTelescope?.Name ?? user.TelescopeId!.Trim();
                native = catalogTelescope?.FocalMm;
                aperture = catalogTelescope?.ApertureMm;
            }
            native = user.FocalMm is > 0 ? user.FocalMm : native;
            aperture = user.ApertureMm is > 0 ? user.ApertureMm : aperture;
            telescopeSource = EquipmentSource.User;
        }
        var factor = user.ReducerFactor is > 0 ? user.ReducerFactor : detected.ReducerFactor;
        if (factor is { } one && Math.Abs(one - 1) < 0.005) factor = null;
        var reducer = factor is { } value
            ? (catalogTelescope is null ? null : catalog.ReducersFor(catalogTelescope).FirstOrDefault(item => Math.Abs(item.Factor - value) < 0.005))
              ?? new TelescopeReducer($"{value.ToString("0.###", CultureInfo.InvariantCulture)}×", value)
            : null;
        var telescope = new TelescopeIdentity(rawName, catalogTelescope, native * (factor ?? 1), reducer, 1);
        return new Optics(telescope, native, factor, aperture, telescopeSource, EquipmentSource.User);
    }

    /// <summary>Stable key for a camera's wheel profile: the catalogue id when known, the header name otherwise.</summary>
    public static string CameraKeyFor(CameraIdentity camera) =>
        camera.Camera?.Id ?? (camera.RawName.Length == 0 ? "unknown" : FilterRecognizer.Normalize(camera.RawName));

    // The wheel shows slots by the name the capture software wrote, not the shared channel name Forge files them under.
    private static string WheelLabel(FrameMetadata frame) =>
        ((frame.FilterName.HasOverride ? frame.FilterName.Value : frame.RawFilterName ?? frame.FilterName.Value) ?? "").Trim();

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
