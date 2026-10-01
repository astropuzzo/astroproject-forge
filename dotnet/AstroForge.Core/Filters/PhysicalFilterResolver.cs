using AstroForge.Core.Equipment;
using AstroForge.Core.Models;

namespace AstroForge.Core.Filters;

/// <summary>
/// Makes the frames of one physical filter share one name, so grouping, Flat matching, export folders and the
/// WBPP FILTER keyword follow the glass rather than the label: "Filtro 3" confirmed as SII and N.I.N.A.'s "SII",
/// or ASIAIR's "S2" next to N.I.N.A.'s "SII", become "SII". The name the capture software wrote stays in
/// <see cref="FrameMetadata.RawFilterName"/>; user overrides always win.
/// </summary>
/// <remarks>
/// A name is only rewritten when the user confirmed the slot or when two labels of the same camera meet on one
/// channel, so a project with a single "H-alpha" keeps its folders as before. Labels that could be different
/// glasses (3 nm and 7 nm Hα, two products) are left apart. Running it again re-derives everything from the raw names.
/// </remarks>
public static class PhysicalFilterResolver
{
    public static void Apply(IEnumerable<FrameMetadata> frames, Func<string, IReadOnlyDictionary<string, string>?>? wheelProfiles = null)
    {
        var withFilter = new List<(FrameMetadata Frame, string Camera, string Raw)>();
        foreach (var frame in frames)
        {
            if (frame.RawFilterName is null && frame.FilterName.OriginalValue is { } original && frame.FilterName.OriginalSource != MetadataSource.FilterProfile)
            {
                frame.RawFilterName = original;
                frame.RawFilterSource = frame.FilterName.OriginalSource;
            }
            if (frame.RawFilterName is not { } raw) continue;
            // Start from the captured name; the rules below decide again whether to rename it.
            if (frame.FilterName.OriginalValue != raw || frame.FilterName.OriginalSource != frame.RawFilterSource)
                frame.FilterName.SetOriginal(raw, frame.RawFilterSource);
            if (raw.Trim().Length > 0) withFilter.Add((frame, CameraKey(frame), raw.Trim()));
        }

        foreach (var camera in withFilter.GroupBy(item => item.Camera, StringComparer.OrdinalIgnoreCase))
        {
            var profile = wheelProfiles?.Invoke(camera.Key);
            var colour = EquipmentRecognizer.Camera(camera.First().Frame.Camera.Value).IsColor;
            var labels = camera.Select(item => item.Raw).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(raw => (Raw: raw, Identity: FilterRecognizer.Recognize(raw, colour, profile)))
                .Select(label => (label.Raw, label.Identity, Canonical: Canonical(label.Identity)))
                .Where(label => label.Canonical is not null)
                .ToList();

            var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var channel in labels.GroupBy(label => label.Canonical!, StringComparer.Ordinal))
            {
                var members = channel.ToList();
                var confirmed = members.Any(label => label.Identity.Source == FilterMatchSource.UserProfile);
                if (members.Count < 2 && !confirmed) continue;
                if (!SameGlass(members.Select(label => label.Identity).ToList())) continue;
                foreach (var label in members) renames[label.Raw] = channel.Key;
            }
            if (renames.Count == 0) continue;
            foreach (var (frame, _, raw) in camera)
                if (renames.TryGetValue(raw, out var canonical) && canonical != frame.FilterName.OriginalValue)
                    frame.FilterName.SetOriginal(canonical, MetadataSource.FilterProfile);
        }
    }

    /// <summary>The short channel name a filter is known by in every capture program: Ha, OIII, SII, Hb, L, R, G, B.</summary>
    public static string? Canonical(FilterIdentity identity)
    {
        if (identity.NeedsConfirmation) return null;
        if (identity.Kind == FilterKind.Narrowband && identity.Lines.Count == 1)
            return identity.Lines[0] == EmissionLines.Ha ? "Ha"
                : identity.Lines[0] == EmissionLines.Oiii ? "OIII"
                : identity.Lines[0] == EmissionLines.Sii ? "SII"
                : identity.Lines[0] == EmissionLines.Hb ? "Hb"
                : null;
        if (identity.Kind == FilterKind.Broadband)
        {
            var channel = identity.Product?.Channel ?? identity.DisplayName;
            return channel is "L" or "R" or "G" or "B" ? channel : null;
        }
        return null;
    }

    /// <summary>Key of the wheel and equipment profiles: the camera as the headers name it, whatever the user later said it really was.</summary>
    public static string CameraKey(FrameMetadata frame) => InstrumentProfile.CameraKeyFor(EquipmentRecognizer.Camera(frame.RawCameraName ?? frame.Camera.Value));

    /// <summary>Labels only merge when nothing says they are different glasses: two products, or bandwidths more than 1 nm apart.</summary>
    private static bool SameGlass(IReadOnlyList<FilterIdentity> identities)
    {
        var products = identities.Select(identity => identity.Product?.Id).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (products > 1) return false;
        var widths = identities.Select(identity => identity.BandwidthNm).OfType<double>().ToArray();
        return widths.Length < 2 || widths.Max() - widths.Min() <= 1;
    }
}
