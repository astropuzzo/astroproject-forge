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
/// One rig of the project: a camera on a focal length. A project that mixes rigs (two cameras, or the same camera with and without a reducer)
/// has several; the one that shot most Lights of the camera with most Lights is the first. The first setup of a camera keeps the camera's own key,
/// so what the user said before the project had two rigs still applies to it.
/// </summary>
/// <param name="Key">Identifies the setup in a project: the camera key, then the camera key and the focal length for a camera's other rigs.</param>
/// <param name="IsPrimary">The first setup of its camera.</param>
public sealed record InstrumentSetup(string Key, string CameraKey, string CameraName, double? FocalMm, bool IsPrimary, int Lights, double IntegrationSeconds, int Nights)
{
    /// <summary>Where the user's optics, reducer and pixel size for this setup are stored: the camera's own entry for its first setup, an entry of the setup's own for the others.</summary>
    public string EquipmentKey => IsPrimary ? CameraKey : Key;
}

/// <summary>A setup and the Lights and Flats that belong to it.</summary>
public sealed record SetupGroup(InstrumentSetup Setup, IReadOnlyList<FrameMetadata> Frames);

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
    /// <summary>The rig this profile describes, among the project's.</summary>
    public InstrumentSetup? Setup { get; init; }
    /// <summary>Key of the user's equipment entry for this rig (see <see cref="InstrumentSetup.EquipmentKey"/>).</summary>
    public string EquipmentKey => Setup?.EquipmentKey ?? CameraKey;
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

    /// <summary>The rigs of a project, the main one first.</summary>
    public static IReadOnlyList<InstrumentSetup> Setups(IEnumerable<FrameMetadata> frames, EquipmentCatalog? catalog = null) =>
        GroupSetups(frames, catalog ?? EquipmentCatalog.Default).Select(group => group.Setup).ToList();

    /// <summary>
    /// The rigs and their frames. Frames are grouped by the camera that shot them, then by focal length (within 3 %): the same camera on 800 mm and on 560 mm is two rigs.
    /// Flats follow the rig whose focal length they carry, and belong to every rig of their camera when they carry none.
    /// </summary>
    public static IReadOnlyList<SetupGroup> GroupSetups(IEnumerable<FrameMetadata> frames, EquipmentCatalog? catalog = null)
    {
        catalog ??= EquipmentCatalog.Default;
        var usable = frames.Where(frame => !frame.IsMaster && frame.Kind is FrameKind.Light or FrameKind.Flat).ToList();
        var result = new List<SetupGroup>();
        var cameras = usable.GroupBy(frame => CameraKeyFor(EquipmentRecognizer.Camera(CapturedCamera(frame), catalog)))
            .OrderByDescending(group => group.Count(frame => frame.Kind == FrameKind.Light)).ThenByDescending(group => group.Count());
        foreach (var camera in cameras)
        {
            var own = camera.ToList();
            var name = Mode(own.Where(frame => frame.Kind == FrameKind.Light).Select(CapturedCamera)) ?? Mode(own.Select(CapturedCamera)) ?? "";
            var clusters = ClusterByFocal(own);
            for (var index = 0; index < clusters.Count; index++)
            {
                var (focal, members) = clusters[index];
                var lights = members.Where(frame => frame.Kind == FrameKind.Light).ToList();
                var key = index == 0 ? camera.Key : $"{camera.Key}@{(focal is { } value ? Math.Round(value).ToString(CultureInfo.InvariantCulture) : "?")}";
                result.Add(new SetupGroup(new InstrumentSetup(key, camera.Key, name, focal, index == 0, lights.Count, lights.Sum(frame => frame.ExposureSeconds.Value ?? 0),
                    lights.Select(frame => frame.SessionId.Value ?? frame.CapturedAt.Value?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "").Distinct().Count()), members));
            }
        }
        return result;
    }

    private static List<(double? Focal, List<FrameMetadata> Frames)> ClusterByFocal(IReadOnlyList<FrameMetadata> frames)
    {
        var clusters = new List<(double Focal, List<FrameMetadata> Frames)>();
        var sized = frames.Where(frame => frame.Kind == FrameKind.Light && frame.FocalLengthMm.Value is > 0).ToList();
        foreach (var group in sized.GroupBy(frame => Math.Round(frame.FocalLengthMm.Value!.Value)).OrderByDescending(group => group.Count()))
        {
            var at = clusters.FindIndex(cluster => Math.Abs(cluster.Focal - group.Key) <= 0.03 * cluster.Focal);
            if (at >= 0) clusters[at].Frames.AddRange(group); else clusters.Add((group.Key, group.ToList()));
        }
        // One focal length, or none known: the camera is one rig, Flats and all.
        if (clusters.Count <= 1) return [(clusters.Count == 1 ? clusters[0].Focal : null, frames.ToList())];

        foreach (var frame in frames.Where(frame => !sized.Contains(frame)))
        {
            if (frame.FocalLengthMm.Value is > 0 and var focal)
            {
                var nearest = clusters.OrderBy(cluster => Math.Abs(Math.Log(cluster.Focal / focal))).First();
                nearest.Frames.Add(frame);
            }
            else foreach (var cluster in clusters) cluster.Frames.Add(frame);
        }
        return clusters.OrderByDescending(cluster => cluster.Frames.Count(frame => frame.Kind == FrameKind.Light)).Select(cluster => ((double?)cluster.Focal, cluster.Frames)).ToList();
    }

    // What is about the camera is the camera's, whichever rig it is on; the optics of a second rig are its own, never the first rig's.
    private static EquipmentOverride? UserEquipment(InstrumentSetup setup, string cameraKey, Func<string, EquipmentOverride?>? lookup)
    {
        var camera = lookup?.Invoke(cameraKey) is { IsEmpty: false } found ? found : null;
        if (setup.IsPrimary) return camera;
        var own = lookup?.Invoke(setup.Key) is { IsEmpty: false } mine ? mine : null;
        var merged = new EquipmentOverride
        {
            CameraId = camera?.CameraId, CameraName = camera?.CameraName, CameraType = camera?.CameraType, PixelUm = own?.PixelUm ?? camera?.PixelUm,
            TelescopeId = own?.TelescopeId, TelescopeName = own?.TelescopeName, ApertureMm = own?.ApertureMm, FocalMm = own?.FocalMm, ReducerFactor = own?.ReducerFactor
        };
        return merged.IsEmpty ? null : merged;
    }

    /// <param name="setup">The rig to describe; the main one when null or when it is not in the project.</param>
    public static InstrumentProfile? Build(IEnumerable<FrameMetadata> frames, Func<string, IReadOnlyDictionary<string, string>?>? wheelProfiles = null,
        Func<string, EquipmentOverride?>? equipmentProfiles = null, EquipmentCatalog? catalog = null, InstrumentSetup? setup = null)
    {
        catalog ??= EquipmentCatalog.Default;
        var groups = GroupSetups(frames, catalog);
        if (groups.Count == 0) return null;

        // The camera that shot most Lights defines the profile unless another rig is asked for. The profile is keyed by the camera
        // as the headers name it, so what the user said about it keeps applying; the camera the user named wins for everything shown.
        var chosen = (setup is null ? null : groups.FirstOrDefault(group => group.Setup.Key == setup.Key)) ?? groups[0];
        var cameraName = chosen.Setup.CameraName;
        var ownFrames = chosen.Frames.ToList();
        var detectedCamera = EquipmentRecognizer.Camera(cameraName, catalog);
        var key = CameraKeyFor(detectedCamera);
        var profile = wheelProfiles?.Invoke(key);
        var user = UserEquipment(chosen.Setup, key, equipmentProfiles);
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
            Setup = chosen.Setup,
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
