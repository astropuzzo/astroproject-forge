using Avalonia.Media.Imaging;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>Where one panel of the framing sits on the picture: degrees east and north of the picture's centre, and how the sensor was turned (position angle, east of north).</summary>
public sealed record SkyPanelView(double Xi, double Eta, double AngleDeg, string Label);

/// <summary>A point of the sky on the picture, degrees east and north of its centre.</summary>
public sealed record SkyMark(double Xi, double Eta);

/// <summary>
/// The sky behind the sensor frame: a picture centred on the target, how many degrees it spans, and where each panel of the framing sits on it.
/// Without a picture the panels are still known, so the frame is drawn at the right place and turn on an empty sky.
/// </summary>
/// <param name="target">Where the capture software was told to centre; drawn when the frames are not on it.</param>
public sealed class SkyScene(Bitmap? image, double fovDeg, IReadOnlyList<SkyPanelView> panels, SkyMark? target = null)
{
    public Bitmap? Image { get; } = image;
    public double FovDeg { get; } = fovDeg;
    public IReadOnlyList<SkyPanelView> Panels { get; } = panels;
    public SkyMark? Target { get; } = target;
}
