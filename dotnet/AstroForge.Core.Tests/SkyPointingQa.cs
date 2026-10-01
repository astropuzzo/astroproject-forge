using System.Net;
using AstroForge.Core.Analysis;
using AstroForge.Core.Models;

internal static class SkyPointingQa
{
    public static void Run()
    {
        RunPointing();
        RunImages().GetAwaiter().GetResult();
    }

    private static void RunPointing()
    {
        // Where the frame points, in order of trust: the solved WCS, the mount, the target. Degrees and sexagesimal text both read.
        var wcs = SkyPointings.FromHeaders(new Dictionary<string, object?> { ["CTYPE1"] = "RA---TAN", ["CRVAL1"] = 314.28, ["CRVAL2"] = 31.72, ["RA"] = 10.0, ["DEC"] = 10.0 });
        Assert(wcs is { Source: "wcs" } && Near(wcs.RaDeg, 314.28) && Near(wcs.DecDeg, 31.72), "Il WCS risolto deve avere la precedenza sulle coordinate della montatura.");
        var mount = SkyPointings.FromHeaders(new Dictionary<string, object?> { ["RA"] = 314.28, ["DEC"] = 31.72 });
        Assert(mount is { Source: "mount" } && Near(mount.RaDeg, 314.28), "RA/DEC in gradi devono bastare.");
        var sexagesimal = SkyPointings.FromHeaders(new Dictionary<string, object?> { ["OBJCTRA"] = "20 56 24", ["OBJCTDEC"] = "+31 43 00" });
        Assert(sexagesimal is { Source: "object" } && Near(sexagesimal.RaDeg, 314.1) && Near(sexagesimal.DecDeg, 31.7166667), $"OBJCTRA/OBJCTDEC sessagesimali letti male: {sexagesimal}.");
        var southern = SkyPointings.FromHeaders(new Dictionary<string, object?> { ["OBJCTRA"] = "05 35 17", ["OBJCTDEC"] = "-05 23 28" });
        Assert(southern is not null && southern.DecDeg < 0 && Near(southern.DecDeg, -5.391111), "La declinazione negativa sessagesimale deve restare negativa.");
        Assert(SkyPointings.FromHeaders(new Dictionary<string, object?> { ["RA"] = 0.0, ["DEC"] = 0.0 }) is null, "(0, 0) vuol dire 'sconosciuto', non un punto del cielo.");
        Assert(SkyPointings.FromHeaders(new Dictionary<string, object?> { ["OBJECT"] = "M31" }) is null && SkyPointings.FromHeaders(new Dictionary<string, object?> { ["RA"] = 10.0, ["DEC"] = 95.0 }) is null,
            "Senza coordinate valide non si deve inventare un puntamento.");

        // Position angle: from the WCS matrix, or from the rotation the capture software declares.
        Assert(Near(SkyPointings.PositionAngle(new Dictionary<string, object?> { ["CD1_1"] = -0.001, ["CD1_2"] = 0.0, ["CD2_1"] = 0.0, ["CD2_2"] = 0.001 }) ?? double.NaN, 0),
            "Un WCS a nord in alto ha angolo di posizione 0.");
        Assert(Near(SkyPointings.PositionAngle(new Dictionary<string, object?> { ["CROTA2"] = 30.0 }) ?? double.NaN, -30), "CROTA2 è l'angolo opposto.");
        Assert(Near(SkyPointings.PositionAngle(new Dictionary<string, object?> { ["OBJCTROT"] = 200.0 }) ?? double.NaN, -160), "L'angolo va riportato tra -180 e 180.");
        Assert(SkyPointings.PositionAngle(new Dictionary<string, object?>()) is null, "Senza rotazione dichiarata l'angolo è ignoto.");

        // The flat picture: the centre is the origin, east is positive xi, north positive eta; the inverse brings the point back.
        var (cx, cy) = SkyPointings.Gnomonic(314.28, 31.72, 314.28, 31.72);
        Assert(Near(cx, 0) && Near(cy, 0), "Il centro deve cadere nell'origine.");
        var (east, _) = SkyPointings.Gnomonic(315.28, 31.72, 314.28, 31.72);
        var (_, north) = SkyPointings.Gnomonic(314.28, 32.72, 314.28, 31.72);
        Assert(east > 0.8 && north > 0.99, $"Est e nord devono essere positivi: {east}, {north}.");
        var (ra, dec) = SkyPointings.FromGnomonic(1.3, -0.7, 314.28, 31.72);
        var (xi, eta) = SkyPointings.Gnomonic(ra, dec, 314.28, 31.72);
        Assert(Near(xi, 1.3) && Near(eta, -0.7), $"La proiezione inversa non riporta il punto: {xi}, {eta}.");
        // The sensor's footprint: with north up its top-left corner is the north-east one; turned so that up points east, the top edge is on the east side.
        var upright = SkyPointings.Corners(0, 0, 2, 1, 0);
        Assert(Near(upright[0].Xi, 1) && Near(upright[0].Eta, 0.5) && Near(upright[2].Xi, -1) && Near(upright[2].Eta, -0.5), "Il riquadro a nord in alto ha l'angolo in alto a sinistra a nord-est.");
        var turned = SkyPointings.Corners(0, 0, 2, 1, 90);
        Assert(Near(turned[0].Xi, 0.5) && Near(turned[0].Eta, -1), $"Con la camera ruotata di 90° il bordo alto del sensore guarda a est: {turned[0]}.");
        var (centreRa, centreDec) = SkyPointings.Centre([(359.0, 0.0), (1.0, 0.0)]);
        Assert(SkyPointings.Separation(centreRa, centreDec, 0, 0) < 1e-3, "Il centro di due punti a cavallo di 0h è 0h.");
        Assert(Near(SkyPointings.Separation(0, 0, 1, 0), 1) && Near(SkyPointings.Separation(10, 89.5, 190, 89.5), 1), "La distanza angolare è sbagliata.");

        // Panels: dithering stays in one panel; a two-panel mosaic is two, north and east first; the night count and the hours add up.
        FrameMetadata Light(double raDeg, double decDeg, string night, double seconds = 180)
        {
            var frame = new FrameMetadata { Path = $"{raDeg}-{decDeg}-{night}.fits", Kind = FrameKind.Light };
            frame.Headers["RA"] = raDeg; frame.Headers["DEC"] = decDeg;
            frame.ExposureSeconds.SetOriginal(seconds, MetadataSource.Header);
            frame.SessionId.SetOriginal(night, MetadataSource.Header);
            return frame;
        }
        var one = SkyPointings.Panels([Light(314.28, 31.72, "a"), Light(314.29, 31.71, "a"), Light(314.27, 31.73, "b")], 0.3);
        Assert(one.Count == 1 && one[0].Lights == 3 && one[0].Nights == 2 && Near(one[0].IntegrationSeconds, 540) && one[0].Label == "", "Il dithering deve restare in un solo pannello.");
        var mosaic = SkyPointings.Panels([Light(314.28, 31.72, "a"), Light(314.28, 33.2, "a"), Light(314.29, 33.19, "b"), Light(314.27, 31.73, "b")], 0.3);
        Assert(mosaic.Count == 2 && mosaic[0].DecDeg > mosaic[1].DecDeg && mosaic[0].Lights == 2 && mosaic[0].Label == "P1" && mosaic[1].Label == "P2", "Un mosaico a due pannelli deve restare diviso in due, il più a nord per primo.");
        var elsewhere = SkyPointings.Mosaic(SkyPointings.Panels([Light(314.28, 31.72, "a"), Light(314.29, 31.71, "a"), Light(314.27, 31.73, "b"), Light(83.8, -5.4, "c"), Light(314.28, 33.2, "a")], 0.3), 3);
        Assert(elsewhere.Count == 2 && elsewhere[0].Lights == 3 && elsewhere.All(panel => panel.RaDeg > 300), "Un altro bersaglio nello stesso progetto non deve entrare nel mosaico mostrato.");
        Assert(SkyPointings.Panels([new FrameMetadata { Path = "x.fits", Kind = FrameKind.Light }], 0.3).Count == 0, "Un Light senza coordinate non fa un pannello.");

        Console.WriteLine("PASS: puntamento sul cielo (WCS, montatura, oggetto, angolo, proiezione, pannelli del mosaico) verificato.");
    }

    private static async Task RunImages()
    {
        var folder = Path.Combine(Path.GetTempPath(), "AstroForge-sky-" + Guid.NewGuid().ToString("N"));
        try
        {
            var picture = new byte[4000];
            picture[0] = 0xFF; picture[1] = 0xD8;
            var requests = new List<string>();
            var answer = HttpStatusCode.OK;
            byte[] body = picture;
            var http = new HttpClient(new FakeHandler(request =>
            {
                requests.Add(request.RequestUri!.ToString());
                return new HttpResponseMessage(answer) { Content = new ByteArrayContent(body) };
            }));
            var client = new SkyImageClient(http, folder);

            // The same field, a few arc-seconds apart, is one picture; the first call goes to the network, the second to the disk.
            var first = await client.GetAsync(314.2803, 31.7198, 5.1);
            var second = await client.GetAsync(314.2791, 31.7211, 5.2);
            Assert(first is not null && second is not null && requests.Count == 1, $"Lo stesso campo deve scaricare una volta sola, richieste: {requests.Count}.");
            Assert(first!.FovDeg >= 5.1 && Near(first.RaDeg, 314.28) && Near(first.DecDeg, 31.72), $"Centro e ampiezza arrotondati male: {first.RaDeg}, {first.DecDeg}, {first.FovDeg}.");
            Assert(requests[0].Contains("ra=314.28") && requests[0].Contains("dec=31.72") && requests[0].Contains("DSS2") && requests[0].Contains("projection=TAN") && requests[0].StartsWith("https://"),
                $"L'indirizzo della richiesta non è quello atteso: {requests[0]}");
            Assert(!requests[0].Contains("?" + folder, StringComparison.OrdinalIgnoreCase) && !requests[0].Contains("gianm", StringComparison.OrdinalIgnoreCase), "La richiesta non deve contenere altro che posizione e dimensione.");

            // A server that answers with an error page, or fails, gives no picture and keeps nothing.
            body = System.Text.Encoding.UTF8.GetBytes(new string('x', 3000));
            Assert(await client.GetAsync(10, 10, 2) is null, "Una pagina d'errore non è un'immagine del cielo.");
            answer = HttpStatusCode.ServiceUnavailable;
            Assert(await client.GetAsync(20, 20, 2) is null && Directory.GetFiles(folder).Length == 1, "Un errore del servizio non deve lasciare file nella cache.");

            // Offline: the picture already kept is still shown; one never seen is simply not there, and nothing is requested.
            Environment.SetEnvironmentVariable(SkyImageClient.OfflineVariable, "1");
            try
            {
                var before = requests.Count;
                answer = HttpStatusCode.OK; body = picture;
                Assert(await client.GetAsync(314.28, 31.72, 5.1) is not null, "Offline, un campo già visto deve aprirsi dalla cache.");
                Assert(await client.GetAsync(100, -20, 3) is null && requests.Count == before, "Offline non si deve contattare la rete.");
            }
            finally { Environment.SetEnvironmentVariable(SkyImageClient.OfflineVariable, null); }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

        Console.WriteLine("PASS: immagine del cielo (richiesta, arrotondamento del campo, cache su disco, errori, modalità offline) verificata.");
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }

    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-3;
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
