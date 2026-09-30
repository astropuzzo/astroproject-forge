using AstroForge.Core.Equipment;
using AstroForge.Core.Models;
using AstroForge.Core.Parsing;

internal static class EquipmentQa
{
    public static void Run()
    {
        var catalog = EquipmentCatalog.Default;
        Assert(catalog.Cameras.Count >= 250 && catalog.Telescopes.Count >= 500, $"Catalogo attrezzatura incompleto: {catalog.Cameras.Count} camere, {catalog.Telescopes.Count} telescopi.");
        Assert(catalog.Cameras.Select(camera => camera.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == catalog.Cameras.Count, "Id camera duplicati.");
        Assert(catalog.Telescopes.All(telescope => telescope.ApertureMm > 0 && telescope.FocalMm > telescope.ApertureMm * 0.9), "Aperture o focali del catalogo non valide.");

        // Names as N.I.N.A., ASIAIR and ASCOM drivers write them in INSTRUME.
        Camera("ZWO ASI2600MM Pro", "asi2600mm", CameraSensorType.Mono);
        Camera("ASI2600MC Pro", "asi2600mc", CameraSensorType.Color);
        Camera("ZWO ASI2600MC", "asi2600mc", CameraSensorType.Color);
        Camera("ZWO CCD ASI294MC Pro", "asi294mc", CameraSensorType.Color);
        Camera("QHY600M", null, CameraSensorType.Mono);
        Camera("Poseidon-C PRO", "poseidonc", CameraSensorType.Color);
        Camera("Canon EOS 6D", "canon6d", CameraSensorType.Dslr);
        Assert(EquipmentRecognizer.Camera("ZWO ASI2600MM Pro").Camera?.PixelUm == 3.76, "Pixel della ASI2600MM non letto dal catalogo.");
        Assert(EquipmentRecognizer.Camera("SBIG STF-8300M") is { Camera: null, Type: CameraSensorType.Mono }, "Una camera fuori catalogo con suffisso M deve risultare mono.");
        Assert(!EquipmentRecognizer.Camera("QHYCCD-Cameras-Capture").IsRecognized, "Il nome di un driver generico non identifica la camera.");
        Assert(!EquipmentRecognizer.Camera("ZWO ASI294MM Pro").IsColor && EquipmentRecognizer.Camera("ZWO ASI294MC Pro").IsColor, "Mono e colori della stessa serie confusi.");

        Telescope("Sky-Watcher Esprit 100ED", 550, "esprit100", null);
        Telescope("Esprit 100", 424, null, 0.77);
        Telescope("Askar FRA400", 280, "fra400", 0.7);
        Telescope("RedCat 51", 250, "redcat51", null);
        Telescope("150PDS", 750, "sw150pds", null);
        Assert(!EquipmentRecognizer.Telescope("My Scope", 500).IsRecognized, "Un nome libero non deve essere associato a un telescopio.");
        Assert(EquipmentRecognizer.Telescope("RedCat 51", 900).Confidence < 0.75, "Una focale incompatibile deve abbassare la fiducia.");
        Assert(Math.Abs(EquipmentRecognizer.ImageScale(3.76, 550) - 1.41) < 0.01, "Campionamento in arcsec/px errato.");

        // A colour camera named only in INSTRUME (no BAYERPAT) with no filter is a normal one-shot-colour setup.
        var headers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["IMAGETYP"] = "Light", ["INSTRUME"] = "ZWO ASI533MC Pro", ["EXPTIME"] = 120.0, ["DATE-OBS"] = "2026-06-19T23:15:12"
        };
        var frame = FrameClassifier.Classify(Path.Combine(Path.GetTempPath(), "osc_0001.fits"), headers, new(TimeZoneInfo.Utc, new TimeOnly(12, 0)));
        Assert(!frame.Issues.Any(issue => issue.Code == "metadata.filter_missing"), "Una camera a colori riconosciuta senza filtro non deve generare avvisi.");
        headers["INSTRUME"] = "ZWO ASI533MM Pro";
        frame = FrameClassifier.Classify(Path.Combine(Path.GetTempPath(), "mono_0001.fits"), headers, new(TimeZoneInfo.Utc, new TimeOnly(12, 0)));
        Assert(frame.Issues.Any(issue => issue.Code == "metadata.filter_missing"), "Una camera mono senza filtro deve generare l'avviso.");

        Console.WriteLine("PASS: catalogo camere e telescopi, riconoscimento INSTRUME/TELESCOP e riduttori da FOCALLEN verificati.");
    }

    private static void Camera(string raw, string? expectedId, CameraSensorType type)
    {
        var identity = EquipmentRecognizer.Camera(raw);
        Assert((expectedId is null || identity.Camera?.Id == expectedId) && identity.Type == type && identity.Confidence >= 0.75,
            $"'{raw}' riconosciuta come {identity.Camera?.Id ?? "-"} {identity.Type} ({identity.Confidence:0.00}), attesa {expectedId} {type}.");
    }

    private static void Telescope(string raw, double focal, string? expectedId, double? reducer)
    {
        var identity = EquipmentRecognizer.Telescope(raw, focal);
        Assert(identity.IsRecognized && (expectedId is null || identity.Telescope!.Id == expectedId) && identity.Reducer?.Factor == reducer && identity.Confidence >= 0.75,
            $"'{raw}' a {focal} mm riconosciuto come {identity.DisplayName} ({identity.Confidence:0.00}).");
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
