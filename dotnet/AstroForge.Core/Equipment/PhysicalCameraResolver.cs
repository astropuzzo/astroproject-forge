using AstroForge.Core.Models;

namespace AstroForge.Core.Equipment;

/// <summary>
/// Lets the user say which camera really shot the frames. A capture program writes whatever its driver or profile calls
/// the camera ("ASIAIR", "QHYCCD-Cameras-Capture", nothing at all); calibration matching compares that name
/// between Lights and Masters, so a wrong or missing one blocks Dark and Bias. When the user's equipment profile for a camera
/// names the real one, every frame of that camera, Lights, Flats and Masters alike, reads under that name.
/// </summary>
/// <remarks>
/// The profile is stored under the key of the camera as <em>detected</em> from the headers, so the answer
/// keeps applying to the same frames in every later project. The captured name stays in
/// <see cref="FrameMetadata.RawCameraName"/>; running again re-derives everything from it, and a frame the user edited by hand wins.
/// </remarks>
public static class PhysicalCameraResolver
{
    public static void Apply(IEnumerable<FrameMetadata> frames, Func<string, EquipmentOverride?>? equipmentProfiles = null, EquipmentCatalog? catalog = null)
    {
        foreach (var frame in frames)
        {
            if (frame.RawCameraName is null && frame.Camera.OriginalSource != MetadataSource.EquipmentProfile)
            {
                frame.RawCameraName = frame.Camera.OriginalValue?.Trim() ?? "";
                frame.RawCameraSource = frame.Camera.OriginalSource;
            }
            if (frame.RawCameraName is not { } raw) continue;

            // Start from the captured name; the profile below decides again whether to replace it.
            var captured = raw.Length == 0 ? null : raw;
            if (frame.Camera.OriginalValue != captured || frame.Camera.OriginalSource != frame.RawCameraSource)
                frame.Camera.SetOriginal(captured, frame.RawCameraSource);

            var key = InstrumentProfile.CameraKeyFor(EquipmentRecognizer.Camera(raw, catalog));
            if (equipmentProfiles?.Invoke(key)?.UserCamera(catalog) is { } real)
                frame.Camera.SetOriginal(real.DisplayName, MetadataSource.EquipmentProfile);
        }
    }
}
