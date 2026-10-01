using Avalonia.Media.Imaging;
using AstroForge.Core.Analysis;
using AstroForge.CrossPlatform.Controls;

namespace AstroForge.CrossPlatform.ViewModels;

/// <summary>The real sky behind the field of view: where the Lights pointed, a DSS2 picture of that place, and the sensor frame laid on it.</summary>
public sealed partial class ObservatoryViewModel
{
    private const int MaxPanels = 12;
    private const int SkyPicturePixels = 1536;
    private SkyImageClient _skyClient = new();
    private CancellationTokenSource? _skyCancel;
    private SkyScene? _sky;
    private string _skyCaption = "", _skyNote = "";

    public SkyScene? Sky { get => _sky; private set => Set(ref _sky, value); }
    /// <summary>The target and where it is: the name the capture software wrote and the centre of the framing.</summary>
    public string SkyCaption { get => _skyCaption; private set => Set(ref _skyCaption, value); }
    /// <summary>What the person should know about the picture: loading, where it comes from, why there is none.</summary>
    public string SkyNote { get => _skyNote; private set => Set(ref _skyNote, value); }
    /// <summary>The sky being built: awaited by the smoke test, ignored by the screen.</summary>
    public Task SkyLoading { get; private set; } = Task.CompletedTask;

    /// <summary>Lets the smoke test serve a picture without the network.</summary>
    internal void UseSkyClient(SkyImageClient client) { _skyClient = client; RefreshSky(); }

    private void RefreshSky()
    {
        _skyCancel?.Cancel();
        var cancel = _skyCancel = new CancellationTokenSource();
        SkyLoading = BuildSkyAsync(cancel.Token);
    }

    private async Task BuildSkyAsync(CancellationToken cancel)
    {
        try
        {
            var lights = (_main.Analysis?.Lights ?? []).Select(item => item.Light).ToList();
            var fields = new[] { PreviewField, OtherField }.Where(field => field is not null).Select(field => field!.Value).ToList();
            if (Instrument is null || lights.Count == 0) { Show(null, "", ""); return; }
            if (fields.Count == 0)
            {
                Show(null, "", English ? "The focal length is needed to draw the field on the sky." : "Serve la focale per disegnare il campo sul cielo.");
                return;
            }

            // How big the field is, in either configuration: the picture has to hold the larger, the panels are told apart by the smaller.
            var largest = fields.Max(field => Math.Max(field.Width, field.Height));
            var smallest = fields.Min(field => Math.Min(field.Width, field.Height));
            var panels = SkyPointings.Mosaic(SkyPointings.Panels(lights, Math.Max(0.05, 0.35 * smallest)), 3 * largest, MaxPanels);
            if (panels.Count == 0)
            {
                Show(null, "", English ? "The files carry no sky coordinates (RA/DEC): the pointing is not known." : "Nei file non ci sono le coordinate del cielo (RA/DEC): non so dove puntava.");
                return;
            }

            var centre = SkyPointings.Centre(panels.Select(panel => (panel.RaDeg, panel.DecDeg)));
            var name = lights.Select(light => light.ObjectName.Value).Where(text => !string.IsNullOrWhiteSpace(text))
                .GroupBy(text => text!.Trim(), StringComparer.OrdinalIgnoreCase).OrderByDescending(group => group.Count()).Select(group => group.Key).FirstOrDefault();
            var where = $"{SkyPointings.FormatRa(centre.Ra)}  {SkyPointings.FormatDec(centre.Dec)}";
            var caption = string.IsNullOrEmpty(name) ? where : $"{name} · {where}";
            var targets = lights.Select(light => SkyPointings.Target(light.Headers)).Where(point => point is not null).Select(point => point!.Value).ToList();
            (double Ra, double Dec)? target = targets.Count == 0 ? null : SkyPointings.Centre(targets);
            var details = SkyDetails(panels);

            if (!_main.ShowRealSky)
            {
                Show(Scene(null, centre, largest, panels, target), caption, (English ? "The real sky is off (Menu)." : "Il cielo reale è spento (Menu).") + details);
                return;
            }

            // The panels first, on an empty sky, so the frame is where it belongs while the picture is on its way.
            Show(Scene(null, centre, largest, panels, target), caption, (English ? "Loading the sky…" : "Carico il cielo…") + details);
            var extent = Extent(centre, largest, panels);
            var image = await _skyClient.GetAsync(centre.Ra, centre.Dec, Math.Max(4.5 * largest, 2.5 * extent), SkyPicturePixels, cancel);
            cancel.ThrowIfCancellationRequested();
            if (image is null)
            {
                Show(Scene(null, centre, largest, panels, target), caption,
                    (English ? "The sky is not reachable (offline?): the frame is still where the data were taken." : "Cielo non raggiungibile (offline?): il riquadro è comunque dove sono stati presi i dati.") + details);
                return;
            }

            Bitmap bitmap;
            using (var stream = new MemoryStream(image.Bytes)) bitmap = new Bitmap(stream);
            Show(Scene(bitmap, (image.RaDeg, image.DecDeg), largest, panels, target, image.FovDeg), caption,
                (English ? $"{SkyImageClient.Credit} · north up, east left" : $"{SkyImageClient.Credit} · nord in alto, est a sinistra") + details);
        }
        catch (OperationCanceledException) { /* a newer sky is being built */ }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or ArgumentException)
        {
            if (!cancel.IsCancellationRequested) Show(null, "", English ? "The sky could not be loaded." : "Non sono riuscito a caricare il cielo.");
        }
    }

    private void Show(SkyScene? scene, string caption, string note)
    {
        Sky = scene;
        SkyCaption = caption;
        SkyNote = note;
    }

    /// <summary>The panels placed around a centre; the picture, when there is one, spans <paramref name="fovDeg"/> there (else a size that holds them).</summary>
    private static SkyScene Scene(Bitmap? image, (double Ra, double Dec) centre, double largest, IReadOnlyList<SkyPanel> panels, (double Ra, double Dec)? target, double? fovDeg = null)
    {
        var views = panels.Select(panel =>
        {
            var (xi, eta) = SkyPointings.Gnomonic(panel.RaDeg, panel.DecDeg, centre.Ra, centre.Dec);
            return new SkyPanelView(xi, eta, panel.PositionAngleDeg ?? 0, panel.Label);
        }).ToList();
        SkyMark? mark = null;
        if (target is { } point)
        {
            var (xi, eta) = SkyPointings.Gnomonic(point.Ra, point.Dec, centre.Ra, centre.Dec);
            mark = new SkyMark(xi, eta);
        }
        return new SkyScene(image, fovDeg ?? 4.5 * largest, views, mark);
    }

    /// <summary>Where the position and the angle of the frames come from, and the mount's own coordinates when they were far off.</summary>
    private string SkyDetails(IReadOnlyList<SkyPanel> panels)
    {
        var main = panels[0];
        var position = main.PositionSource switch
        {
            "wcs" => English ? "solved WCS" : "WCS risolto",
            "object" => English ? "target" : "bersaglio",
            _ => English ? "mount RA/DEC" : "RA/DEC montatura"
        };
        var angle = !panels.All(panel => panel.PositionAngleDeg is not null) ? (English ? "not recorded (upright)" : "non registrato (dritto)")
            : main.AngleSource switch
            {
                "wcs" => English ? "WCS" : "WCS",
                "rotator" => English ? "rotator" : "rotatore",
                _ => English ? "declared" : "dichiarato"
            };
        var text = English ? $"\nposition: {position} · angle: {angle}" : $"\nposizione: {position} · angolo: {angle}";
        var off = panels.Max(panel => panel.MountDeviationDeg);
        if (off > 0.1)
            text += English ? $"\nthe mount's RA/DEC were up to {Number(off, "0.0")}° off" : $"\nla montatura dava fino a {Number(off, "0.0")}° di scarto";
        return text;
    }

    /// <summary>The distance across of everything the frames can cover, turned any way (a square as long as the diagonal), from the centre: the picture is made to hold it with room to spare.</summary>
    private static double Extent((double Ra, double Dec) centre, double largest, IReadOnlyList<SkyPanel> panels) =>
        2 * panels.Max(panel =>
        {
            var (xi, eta) = SkyPointings.Gnomonic(panel.RaDeg, panel.DecDeg, centre.Ra, centre.Dec);
            return SkyPointings.Corners(xi, eta, largest * Math.Sqrt(2), largest * Math.Sqrt(2), 0).Max(corner => Math.Max(Math.Abs(corner.Xi), Math.Abs(corner.Eta)));
        });
}
