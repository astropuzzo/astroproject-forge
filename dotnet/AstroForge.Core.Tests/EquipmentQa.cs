using AstroForge.Core.Equipment;
using AstroForge.Core.Models;
using AstroForge.Core.Parsing;
using AstroForge.Core.Persistence;
using System.Text.Json;

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
        Overrides();
    }

    // The user's equipment profile wins over headers and catalogue, per camera, and survives the state file.
    private static void Overrides()
    {
        var frames = Enumerable.Range(1, 3).Select(index => FrameClassifier.Classify(Path.Combine(Path.GetTempPath(), $"train_{index:0000}.fits"),
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["IMAGETYP"] = "Light", ["INSTRUME"] = "ZWO ASI2600MM Pro", ["TELESCOP"] = "Askar FRA400", ["FOCALLEN"] = 280.0, ["XPIXSZ"] = 3.76,
                ["NAXIS1"] = 6248, ["NAXIS2"] = 4176, ["FILTER"] = "Ha", ["EXPTIME"] = 300.0, ["DATE-OBS"] = $"2026-06-19T23:{index:00}:00"
            }, new(TimeZoneInfo.Utc, new TimeOnly(12, 0)))).ToList();

        // No override: the profile reads exactly as before, with the reducer and sources exposed.
        var detected = InstrumentProfile.Build(frames)!;
        var unused = InstrumentProfile.Build(frames, null, key => key == "other-camera" ? new EquipmentOverride { PixelUm = 9 } : null)!;
        var empty = InstrumentProfile.Build(frames, null, _ => new EquipmentOverride())!;
        Assert(detected is { CameraKey: "asi2600mm", FocalMm: 280, NativeFocalMm: 400, ReducerFactor: 0.7, ApertureMm: 72, PixelUm: 3.76, Override: null }
            && detected.Telescope.Telescope?.Id == "fra400" && detected.TelescopeSource == EquipmentSource.Catalog
            && detected.FocalSource == EquipmentSource.Header && detected.PixelSource == EquipmentSource.Header,
            $"Profilo rilevato errato: {detected.Telescope.DisplayName} {detected.FocalMm} mm ({detected.NativeFocalMm} × {detected.ReducerFactor}).");
        foreach (var same in new[] { unused, empty })
            Assert(same.Telescope == detected.Telescope && same.FocalMm == detected.FocalMm && same.PixelUm == detected.PixelUm && same.ImageScale == detected.ImageScale
                && same.FieldOfView == detected.FieldOfView && same.Override is null && same.TelescopeSource == detected.TelescopeSource,
                "Senza un profilo per questa camera lo strumento non deve cambiare.");
        Assert(detected.FieldOfViewAt(detected.FocalMm!.Value) == detected.FieldOfView && detected.ImageScaleAt(280) == detected.ImageScale, "FieldOfViewAt deve coincidere con il campo effettivo.");

        // Another catalogue telescope: identity, aperture and focal (native × detected reducer) come from the user.
        var esprit = InstrumentProfile.Build(frames, null, key => key == "asi2600mm" ? new EquipmentOverride { TelescopeId = "esprit100", ReducerFactor = 0.77 } : null)!;
        Assert(esprit.Telescope.Telescope?.Id == "esprit100" && esprit.Telescope.Reducer?.Factor == 0.77 && Math.Abs(esprit.FocalMm!.Value - 550 * 0.77) < 1e-9
            && esprit is { NativeFocalMm: 550, ApertureMm: 100, TelescopeSource: EquipmentSource.User, FocalSource: EquipmentSource.User, PixelSource: EquipmentSource.Header },
            $"Il telescopio scelto dall'utente deve sostituire quello rilevato: {esprit.Telescope.DisplayName} {esprit.FocalMm} mm.");
        Assert(Math.Abs(esprit.ImageScale!.Value - EquipmentRecognizer.ImageScale(3.76, 550 * 0.77)) < 1e-9, "Scala immagine non ricalcolata sulla focale dell'utente.");

        // Custom optics the catalogue lacks.
        var custom = InstrumentProfile.Build(frames, null, _ => new EquipmentOverride { TelescopeName = "Newton autocostruito", ApertureMm = 200, FocalMm = 1000, ReducerFactor = 1 })!;
        Assert(custom.Telescope is { Telescope: null, Reducer: null, FocalMm: 1000, RawName: "Newton autocostruito" } && custom is { ApertureMm: 200, ReducerFactor: null, FocalRatio: 5 },
            $"Ottica personalizzata non applicata: {custom.Telescope.DisplayName} {custom.FocalMm} mm.");

        // Reducer only: the same FRA400 without its 0.7× goes back to 400 mm and a narrower field.
        var bare = InstrumentProfile.Build(frames, null, _ => new EquipmentOverride { ReducerFactor = 1 })!;
        Assert(bare.Telescope.Telescope?.Id == "fra400" && bare is { FocalMm: 400, NativeFocalMm: 400, ReducerFactor: null, TelescopeSource: EquipmentSource.Catalog, FocalSource: EquipmentSource.User }
            && bare.Telescope.Reducer is null, $"Togliere il riduttore deve riportare la focale nativa: {bare.FocalMm} mm.");
        Assert(bare.FieldOfView is { } narrow && detected.FieldOfView is { } wide && Math.Abs(narrow.Width / wide.Width - 0.7) < 0.01 && bare.FieldOfViewAt(280) == detected.FieldOfView,
            "Il campo con e senza riduttore deve scalare con la focale.");
        var reduced = InstrumentProfile.Build(frames, null, _ => new EquipmentOverride { ReducerFactor = 0.7 })!;
        Assert(reduced.Telescope.Reducer?.Name == detected.Telescope.Reducer?.Name && Math.Abs(reduced.FocalMm!.Value - 280) < 1e-9, "Un fattore da catalogo deve riusare il riduttore del catalogo.");

        // Pixel size: the user's pitch rescales the sampling, the optics stay detected.
        var pixel = InstrumentProfile.Build(frames, null, _ => new EquipmentOverride { PixelUm = 7.52 })!;
        Assert(pixel is { PixelUm: 7.52, PixelSource: EquipmentSource.User, FocalMm: 280, FocalSource: EquipmentSource.Header } && pixel.Telescope == detected.Telescope
            && Math.Abs(pixel.ImageScale!.Value - 2 * detected.ImageScale!.Value) < 1e-9 && Math.Abs(pixel.SensorWidthMm!.Value - 2 * detected.SensorWidthMm!.Value) < 1e-9,
            "Il pixel dell'utente deve cambiare scala immagine e sensore.");

        // The state file: profiles round-trip case-insensitively, and a state written before them still loads.
        var options = new JsonSerializerOptions { WriteIndented = true };
        var state = new StateProbe { EquipmentProfiles = { ["asi2600mm"] = new() { TelescopeId = "esprit100", ReducerFactor = 0.77, PixelUm = 3.76 } } };
        var json = JsonSerializer.Serialize(state, options);
        Assert(!json.Contains("IsEmpty") && !json.Contains("HasTelescope"), "Le proprietà calcolate non vanno salvate.");
        var loaded = JsonSerializer.Deserialize<StateProbe>(SettingsMigration.Migrate(json), options)!;
        var profiles = EquipmentOverride.Normalize(loaded.EquipmentProfiles);
        Assert(profiles.TryGetValue("ASI2600MM", out var back) && back == state.EquipmentProfiles["asi2600mm"], "Profilo attrezzatura perso nel salvataggio.");
        const string oldState = """{ "SchemaVersion": 2, "ProjectName": "M31", "FilterWheelProfiles": { "asi2600mm": { "filtro3": "baader-sii" } } }""";
        var old = JsonSerializer.Deserialize<StateProbe>(SettingsMigration.Migrate(oldState), options)!;
        Assert(old.EquipmentProfiles.Count == 0 && old.FilterWheelProfiles.Count == 1 && EquipmentOverride.Normalize(null).Count == 0,
            "Uno stato senza profili attrezzatura deve caricarsi vuoto.");
        var nulls = JsonSerializer.Deserialize<StateProbe>("""{ "EquipmentProfiles": { "a": null, "b": {}, "c": { "PixelUm": 4.63 } } }""", options)!;
        Assert(EquipmentOverride.Normalize(nulls.EquipmentProfiles) is { Count: 1 } kept && kept["C"].PixelUm == 4.63, "Profili vuoti o nulli vanno scartati.");

        Console.WriteLine("PASS: profilo attrezzatura dell'utente (telescopio, riduttore, pixel) applicato e persistito.");
    }

    // The persisted shape of AppState's equipment and wheel profiles (AppState lives in the UI assembly).
    private sealed class StateProbe
    {
        public int SchemaVersion { get; set; } = SettingsMigration.CurrentSchema;
        public Dictionary<string, Dictionary<string, string>> FilterWheelProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, EquipmentOverride> EquipmentProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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
