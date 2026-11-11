using AstroForge.Core.Analysis;
using AstroForge.Core.Equipment;
using AstroForge.Core.Export;
using AstroForge.Core.Filters;
using AstroForge.Core.IO;
using AstroForge.Core.Scanning;
using AstroForge.Core.Matching;
using AstroForge.Core.Models;
using AstroForge.Core.Parsing;
using AstroForge.Core.Quality;
using AstroForge.Core.Sessions;
using AstroForge.Core.Wbpp;
using System.Text.Json;
using System.Buffers.Binary;
using System.Text;

/// <summary>Regressions for the 1.14 algorithm review: capture time, nights, statistics and calibration ties.</summary>
internal static class AlgorithmQa
{
    public static async Task RunAsync()
    {
        await FwhmFollowsTheStarAsync();
        CaptureTimeIsUtcUnlessTheSiteClockIsKnown();
        RemoteSiteKeepsItsOwnNight();
        NightsAreCountedOncePerProject();
        ClosestDarkWinsATie();
        await NamesFoldersAndHeadersAsync();
        MatchingRules();
        SkyCoordinates();
        await RawSetsExportAndWbppAsync();
        DarkFlatsStandInForTheBias();
        Console.WriteLine("PASS: FWHM su stelle sintetiche, ora di scatto, notti di un sito remoto, conteggio notti, spareggio dei Dark, nomi file, cartelle libreria, card FITS/XISF, regole di abbinamento, coordinate e set di Dark/Bias grezzi fino all'istanza WBPP e Dark Flat al posto del Bias verificati.");
    }

    private static async Task FwhmFollowsTheStarAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AstroForge-QA-Fwhm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var previous = 0d;
            foreach (var sigma in new[] { 1.0, 1.5, 2.0, 2.8, 3.5, 5.0 })
            {
                var expected = 2.35482 * sigma;
                var widths = new List<double>();
                foreach (var snr in new[] { 10, 30, 300 })
                {
                    var path = Path.Combine(root, FormattableString.Invariant($"stars-{sigma:0.0}-{snr}.fits"));
                    WriteStarField(path, sigma, snr);
                    var metrics = await FitsQualityAnalyzer.AnalyzeAsync(path);
                    Assert(metrics.StarCount >= 30, FormattableString.Invariant($"σ {sigma}, SNR {snr}: solo {metrics.StarCount} stelle misurate su 49."));
                    Assert(Math.Abs(metrics.FwhmPixels - expected) <= 0.1 * expected,
                        FormattableString.Invariant($"σ {sigma}, SNR {snr}: FWHM {metrics.FwhmPixels:0.00} px, attesa {expected:0.00} px (±10%)."));
                    widths.Add(metrics.FwhmPixels);
                }
                // The same star must not read sharper when it is fainter: that is what hid hazy frames.
                Assert(widths.Max() - widths.Min() <= 0.12 * expected, FormattableString.Invariant($"σ {sigma}: la FWHM dipende dalla luminosità ({string.Join(" / ", widths.Select(value => value.ToString("0.00")))})."));
                Assert(widths.Average() > previous, FormattableString.Invariant($"σ {sigma}: la FWHM non cresce con la stella."));
                previous = widths.Average();
            }
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>A 7 × 7 grid of Gaussian stars at sub-pixel positions on a flat sky with Gaussian noise, as 32-bit floats.</summary>
    private static void WriteStarField(string path, double sigma, double snr)
    {
        const int size = 420;
        const double sky = 1000, noise = 10;
        var random = new Random(20261111);
        var image = new double[size * size];
        for (var index = 0; index < image.Length; index++)
        {
            // Box-Muller: two uniforms make one Gaussian sample.
            var gaussian = Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
            image[index] = sky + noise * gaussian;
        }
        for (var row = 0; row < 7; row++)
        for (var column = 0; column < 7; column++)
        {
            double cx = 60 + column * 50 + 0.3 * ((row + column) % 3 - 1), cy = 60 + row * 50 + 0.2 * ((row * 2 + column) % 3 - 1);
            var reach = (int)Math.Ceiling(5 * sigma);
            for (var y = (int)cy - reach; y <= (int)cy + reach; y++)
            for (var x = (int)cx - reach; x <= (int)cx + reach; x++)
                image[y * size + x] += snr * noise * Math.Exp(-((x - cx) * (x - cx) + (y - cy) * (y - cy)) / (2 * sigma * sigma));
        }
        static string Card(string key, string value = "") => (value.Length == 0 ? key : $"{key.PadRight(8)}= {value}").PadRight(80)[..80];
        var header = Card("SIMPLE", "T") + Card("BITPIX", "-32") + Card("NAXIS", "2") + Card("NAXIS1", size.ToString()) + Card("NAXIS2", size.ToString()) + Card("END");
        var data = new byte[size * size * 4];
        for (var index = 0; index < image.Length; index++) BinaryPrimitives.WriteSingleBigEndian(data.AsSpan(index * 4, 4), (float)image[index]);
        using var output = File.Create(path);
        output.Write(Encoding.ASCII.GetBytes(header.PadRight(2880)));
        output.Write(data);
        output.Write(new byte[(2880 - data.Length % 2880) % 2880]);
    }

    private static async Task NamesFoldersAndHeadersAsync()
    {
        var settings = new SessionSettings(TimeZoneInfo.Utc, new TimeOnly(12, 0));

        // N.I.N.A. without a filter wheel: the slot for the filter is empty and the temperature follows the time.
        var bare = FileNameMetadata.Parse("2026-06-15_00-21-26__-10.00_300.00s_0001.fits");
        Assert(bare.Filter is null && bare.SensorTemperatureC == -10 && bare.ExposureSeconds == 300, $"Nome N.I.N.A. senza filtro letto male: filtro '{bare.Filter}', {bare.SensorTemperatureC} °C.");
        var wizard = ClassifyAt("2026-06-15_05-40-02_FlatWizard_-9.90_0.96s_0000.fits", settings, ("IMAGETYP", "FLAT"), ("SWCREATE", "N.I.N.A. 3.1.2.9001"), ("BAYERPAT", "RGGB"), ("FILTER", null));
        var target = ClassifyAt("2026-06-15_00-21-26_M101_-9.80_120.00s_0007.fits", settings, ("SWCREATE", "N.I.N.A. 3.1.2.9001"), ("BAYERPAT", "RGGB"), ("FILTER", null));
        Assert(wizard.FilterName.Value is null && target.FilterName.Value is null, $"Bersaglio o FlatWizard presi per un filtro: '{target.FilterName.Value}', '{wizard.FilterName.Value}'.");
        var named = ClassifyAt("2026-06-15_00-21-26_Filtro 3_-9.80_120.00s_0007.fits", settings, ("FILTER", null));
        Assert(named.FilterName.Value == "Filtro 3", "Senza SWCREATE il filtro del nome file deve restare.");

        foreach (var name in new[] { "FlatDark_0001.fits", "dark_flat_001.fit", "Flat-Dark_2s_0003.fits" })
            Assert(ClassifyAt(name, settings, ("IMAGETYP", null)).Kind == FrameKind.DarkFlat, $"{name}: un Dark Flat non deve diventare un Flat.");
        Assert(ClassifyAt("cal_0001.fits", settings, ("IMAGETYP", "FLATDARK")).Kind == FrameKind.DarkFlat, "IMAGETYP FLATDARK non riconosciuto.");
        Assert(ClassifyAt("Flat_3.0s_Bin1_Ha_0001.fit", settings, ("IMAGETYP", null)).Kind == FrameKind.Flat, "Un Flat ASIAIR deve restare un Flat.");

        Assert(ClassifyAt("masterFlat_BIN-1_Ha.fits", settings, ("IMAGETYP", "Flat")).IsMaster && ClassifyAt("MasterDark300s.fits", settings, ("IMAGETYP", "Dark")).IsMaster,
            "Un Master con nome camelCase non è stato riconosciuto.");
        Assert(!ClassifyAt("Remastered_Ha_0001.fits", settings).IsMaster, "\"Remastered\" non è un Master.");

        var split = ClassifyAt("legacy.fits", settings, ("DATE-OBS", "2026-08-14"), ("TIME-OBS", "22:10:05"));
        Assert(split.CapturedAt.Value?.UtcDateTime == new DateTime(2026, 8, 14, 22, 10, 5), "DATE-OBS e TIME-OBS separati non composti.");

        var root = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()))!;
        Assert(PathIdentity.IsWithin(Path.Combine(root, "Progetti", "M31"), root), "La radice di un disco deve contenere le sue cartelle.");
        Assert(!PathIdentity.IsWithin(root, Path.Combine(root, "Progetti")), "Una cartella non contiene la radice.");

        Assert(EquipmentRecognizer.Telescope("Askar FRA400", 0).FocalMm == 400, "FOCALLEN 0 deve lasciare la focale del catalogo.");
        Assert(FilterRecognizer.Recognize("Ha 656nm 7nm").BandwidthNm == 7, "La banda passante dopo la lunghezza d'onda non è stata letta.");

        var library = Path.Combine(Path.GetTempPath(), $"AstroForge-QA-Library-{Guid.NewGuid():N}");
        FrameMetadata Master(FrameKind kind, params string[] parts)
        {
            var frame = new FrameMetadata { Path = Path.Combine([library, .. parts]), Kind = kind, IsMaster = true };
            frame.Camera.SetOriginal("QA Camera", MetadataSource.Header);
            return frame;
        }
        var labelled = Master(FrameKind.Dark, "Darks", "Gain100", "Temp-10", "m.xisf");
        var suffixed = Master(FrameKind.Dark, "Darks", "Gain100", "Temp-15C", "m.xisf");
        var plain = Master(FrameKind.Dark, "GAIN_100", "-20", "MasterDark_300s.fits");
        var dated = Master(FrameKind.Dark, "GAIN_100", "2024", "MasterDark_300s.fits");
        LibraryMetadataResolver.Apply([labelled, suffixed, plain, dated], library);
        Assert(labelled.SetTemperatureC.Value == -10 && suffixed.SetTemperatureC.Value == -15, $"Cartelle Temp-10 / Temp-15C lette come {labelled.SetTemperatureC.Value} e {suffixed.SetTemperatureC.Value}.");
        Assert(plain.SetTemperatureC.Value == -20 && plain.Gain.Value == 100, "La cartella della temperatura sotto GAIN_100 non è stata letta.");
        Assert(dated.SetTemperatureC.Value is null, "Una cartella \"2024\" non è una temperatura.");

        var files = Path.Combine(Path.GetTempPath(), $"AstroForge-QA-Headers-{Guid.NewGuid():N}");
        Directory.CreateDirectory(files);
        try
        {
            var xisf = Path.Combine(files, "exposure.xisf");
            var body = Encoding.UTF8.GetBytes("<xisf><Image geometry=\"100:80:1\"><Property id=\"Instrument:ExposureTime\" type=\"Float32\" value=\"300\"/></Image></xisf>");
            var bytes = new byte[16 + body.Length];
            "XISF0100"u8.CopyTo(bytes);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), (uint)body.Length);
            body.CopyTo(bytes, 16);
            File.WriteAllBytes(xisf, bytes);
            Assert(Convert.ToDouble((await XisfHeaderReader.ReadAsync(xisf))["EXPTIME"]) == 300, "Instrument:ExposureTime XISF non letto come EXPTIME.");

            static string Card(string text) => text.PadRight(80)[..80];
            var fits = Path.Combine(files, "long.fits");
            var header = Card("SIMPLE  = T") + Card("BITPIX  = 16") + Card("NAXIS   = 0") + Card("OBJECT  = 'Sh2-240 Simeis 147 &'") + Card("CONTINUE  'Spaghetti Nebula' / long name") + Card("GAIN    = 100") + Card("END");
            File.WriteAllBytes(fits, Encoding.ASCII.GetBytes(header.PadRight(2880)));
            var read = await FitsHeaderReader.ReadAsync(fits);
            Assert((string?)read["OBJECT"] == "Sh2-240 Simeis 147 Spaghetti Nebula" && Convert.ToInt32(read["GAIN"]) == 100, $"Card CONTINUE non unita: '{read["OBJECT"]}'.");
        }
        finally { Directory.Delete(files, true); }
    }

    private static FrameMetadata ClassifyAt(string fileName, SessionSettings settings, params (string Key, object? Value)[] extra)
    {
        var headers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["IMAGETYP"] = "Light", ["INSTRUME"] = "QA Camera", ["GAIN"] = 100, ["OFFSET"] = 50, ["SET-TEMP"] = -10,
            ["FILTER"] = "Ha", ["EXPTIME"] = 300, ["NAXIS1"] = 6248, ["NAXIS2"] = 4176, ["XBINNING"] = 1, ["YBINNING"] = 1
        };
        foreach (var (key, value) in extra)
            if (value is null) headers.Remove(key); else headers[key] = value;
        return FrameClassifier.Classify(Path.Combine(Path.GetTempPath(), fileName), headers, settings);
    }

    private static void MatchingRules()
    {
        var settings = new SessionSettings(TimeZoneInfo.Utc, new TimeOnly(12, 0));

        // A colour camera without a wheel: no FILTER on Lights or Flats is still a matching pair.
        var colourLight = ClassifyAt("osc-light.fits", settings, ("FILTER", null), ("BAYERPAT", "RGGB"), ("DATE-OBS", "2026-08-14T22:00:00Z"));
        var colourFlat = ClassifyAt("osc-flat.fits", settings, ("IMAGETYP", "Flat"), ("FILTER", null), ("BAYERPAT", "RGGB"), ("EXPTIME", 2), ("DATE-OBS", "2026-08-15T04:00:00Z"));
        var noFilter = CalibrationMatcher.Find(colourLight, [colourFlat], FrameKind.Flat);
        Assert(noFilter.IsAccepted, $"Light e Flat a colori senza FILTER devono abbinarsi: {noFilter.Status}.");
        var filtered = ClassifyAt("ha-flat.fits", settings, ("IMAGETYP", "Flat"), ("BAYERPAT", "RGGB"), ("EXPTIME", 2));
        Assert(CalibrationMatcher.Find(colourLight, [filtered], FrameKind.Flat).Status == MatchStatus.InsufficientMetadata, "Un filtro su un solo lato resta un dato mancante.");

        // A Bias hardly changes with temperature: 5 °C away is accepted, and the closer one is preferred.
        var cold = ClassifyAt("light-15.fits", settings, ("SET-TEMP", -15));
        var biasTen = ClassifyAt("masterBias-10.fits", settings, ("IMAGETYP", "Master Bias"), ("EXPTIME", 0), ("SET-TEMP", -10));
        var biasFifteen = ClassifyAt("masterBias-15.fits", settings, ("IMAGETYP", "Master Bias"), ("EXPTIME", 0), ("SET-TEMP", -15));
        var warmBias = CalibrationMatcher.Find(cold, [biasTen], FrameKind.Bias);
        Assert(warmBias.Status == MatchStatus.WithinTolerance && warmBias.Selected?.Frame == biasTen, $"Un Bias a -10 °C per un Light a -15 °C deve essere accettato: {warmBias.Status}.");
        Assert(CalibrationMatcher.Find(cold, [biasTen, biasFifteen], FrameKind.Bias).Selected?.Frame == biasFifteen, "Tra due Bias vince quello alla temperatura del Light.");

        // A Flat Set given to part of a night stays with that part, whatever the order of the files.
        FrameMetadata NightLight(string name, string? set)
        {
            var frame = ClassifyAt(name, settings, ("DATE-OBS", "2026-08-14T22:00:00Z"));
            if (set is not null) frame.FlatSetId.SetOverride(set);
            return frame;
        }
        FrameMetadata SetFlat(string name, string set, string date)
        {
            var frame = ClassifyAt(name, settings, ("IMAGETYP", "Flat"), ("EXPTIME", 2), ("DATE-OBS", date));
            frame.FlatSetId.SetOverride(set);
            return frame;
        }
        var flatA = SetFlat("flat-a.fits", "A", "2026-08-15T04:00:00Z");
        var flatB = SetFlat("flat-b.fits", "B", "2026-08-16T04:00:00Z");
        foreach (var order in new[] { new[] { "A", "B", "A" }, ["B", "A", "A"] })
        {
            var lights = order.Select((set, index) => NightLight($"night-{index}-{set}.fits", set)).ToArray();
            var analysis = ProjectAnalyzer.Analyze([.. lights, flatA, flatB]);
            Assert(analysis.Lights.All(item => item.Flat.Selected?.Frame == (item.Light.FlatSetId.Value == "A" ? flatA : flatB)),
                $"Flat Set A e B nella stessa notte non rispettati con ordine {string.Join(",", order)}.");
        }

        // An old stack in a scanned folder is not a sub to calibrate again.
        var stack = ClassifyAt("masterLight_Ha.fits", settings, ("IMAGETYP", "Master Light"));
        var withStack = ProjectAnalyzer.Analyze([colourLight, stack]);
        Assert(withStack.Lights.Count == 1 && withStack.Lights[0].Light == colourLight, "Un master Light non deve entrare tra i Light da calibrare.");

        // "Ha" and "HA" are one filter in the statistics.
        var mixed = ProjectStatisticsCalculator.Calculate(ProjectAnalyzer.Analyze([ClassifyAt("a.fits", settings, ("FILTER", "Ha")), ClassifyAt("b.fits", settings, ("FILTER", "HA"))]));
        Assert(mixed.FilterCount == 1 && mixed.Filters[0].LightCount == 2, $"Ha e HA contati come {mixed.FilterCount} filtri.");
    }

    private static void SkyCoordinates()
    {
        var text = SkyPointings.FromHeaders(new Dictionary<string, object?> { ["RA"] = "22 47 14", ["DEC"] = "+58 02 19" });
        Assert(text is { Source: "mount" } && Math.Abs(text.RaDeg - 341.8083) < 0.01 && Math.Abs(text.DecDeg - 58.0386) < 0.01, $"RA in ore come testo letta male: {text?.RaDeg}.");
        var number = SkyPointings.FromHeaders(new Dictionary<string, object?> { ["RA"] = 314.28, ["DEC"] = 31.72 });
        Assert(number is not null && Math.Abs(number.RaDeg - 314.28) < 1e-9, "RA numerica deve restare in gradi.");
        var unknownTarget = SkyPointings.FromHeaders(new Dictionary<string, object?> { ["OBJCTRA"] = "00 00 00", ["OBJCTDEC"] = "+00 00 00", ["RA"] = 314.28, ["DEC"] = 31.72 });
        Assert(unknownTarget is { Source: "mount" } && Math.Abs(unknownTarget.RaDeg - 314.28) < 1e-9, "Un bersaglio (0, 0) deve lasciare il posto alla montatura.");
        Assert(SkyPointings.FormatRa(314.9999) == "21h 00.0m" && SkyPointings.FormatDec(31.9999) == "+32° 00′", $"Arrotondamento coordinate: {SkyPointings.FormatRa(314.9999)} {SkyPointings.FormatDec(31.9999)}.");
        Assert(SkyPointings.FormatRa(359.9999) == "0h 00.0m", $"RA a fine giro: {SkyPointings.FormatRa(359.9999)}.");
    }

    /// <summary>
    /// The way an ASIAIR folder really looks: raw Darks and Bias, no Masters, a colour camera without FILTER, a target
    /// name with a colon and a space, and Flats at two exposures. Exported for real and turned into a WBPP instance.
    /// </summary>
    private static async Task RawSetsExportAndWbppAsync()
    {
        var settings = new SessionSettings(TimeZoneInfo.Utc, new TimeOnly(12, 0));
        var root = Path.Combine(Path.GetTempPath(), $"AstroForge-QA-RawSets-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        try
        {
            FrameMetadata File_(string name, params (string Key, object? Value)[] headers)
            {
                var all = new List<(string, object?)> { ("FILTER", null), ("BAYERPAT", "RGGB"), ("SET-TEMP", null), ("OBJECT", "NGC 6960: Veil") };
                all.AddRange(headers);
                var frame = ClassifyAt(name, settings, [.. all]);
                var onDisk = new FrameMetadata { Path = Path.Combine(source, name), Kind = frame.Kind, IsMaster = frame.IsMaster }.CopyFrom(frame);
                onDisk.ObjectName.SetOriginal(frame.ObjectName.Value, MetadataSource.Header);
                onDisk.BayerPattern.SetOriginal(frame.BayerPattern.Value, MetadataSource.Header);
                onDisk.SensorTemperatureC.SetOriginal(frame.SensorTemperatureC.Value, MetadataSource.Header);
                onDisk.CapturedAt.SetOriginal(frame.CapturedAt.Value, MetadataSource.Header);
                onDisk.SessionId.SetOriginal(frame.SessionId.Value, MetadataSource.Inferred);
                File.WriteAllText(onDisk.Path, name);
                return onDisk;
            }
            var frames = new List<FrameMetadata>();
            for (var index = 0; index < 3; index++) frames.Add(File_($"Light_{index}.fit", ("CCD-TEMP", -10 + 0.1 * index), ("DATE-OBS", $"2026-08-14T22:0{index}:00Z")));
            frames.Add(File_("Flat_0.fit", ("IMAGETYP", "Flat"), ("EXPTIME", 2.0), ("CCD-TEMP", -10.0), ("DATE-OBS", "2026-08-15T04:00:00Z")));
            frames.Add(File_("Flat_1.fit", ("IMAGETYP", "Flat"), ("EXPTIME", 2.5), ("CCD-TEMP", -10.0), ("DATE-OBS", "2026-08-15T04:01:00Z")));
            // The sensor has no set point here: the reading wanders around -10 °C, crossing the half degree.
            var darkReadings = new[] { -9.4, -9.6, -10.1, -9.8, -10.3 };
            for (var index = 0; index < darkReadings.Length; index++) frames.Add(File_($"Dark_{index}.fit", ("IMAGETYP", "Dark"), ("CCD-TEMP", darkReadings[index])));
            for (var index = 0; index < 4; index++) frames.Add(File_($"Bias_{index}.fit", ("IMAGETYP", "Bias"), ("EXPTIME", 0.0001), ("CCD-TEMP", -9.9)));
            // Darks of another night at another temperature are a different set and must stay out.
            for (var index = 0; index < 3; index++) frames.Add(File_($"DarkWarm_{index}.fit", ("IMAGETYP", "Dark"), ("CCD-TEMP", 0.2 * index)));

            var analysis = ProjectAnalyzer.Analyze(frames);
            Assert(analysis.Ready, $"Dark e Bias grezzi devono bastare a un progetto: {string.Join(", ", analysis.Lights.Select(item => $"{item.Flat.Status}/{item.Dark.Status}/{item.Bias.Status}"))}.");
            Assert(analysis.Lights.All(item => item.DarkSet is { Frames.Count: 5 } && item.BiasSet is { Frames.Count: 4 }), "Il set di Dark o di Bias non raccoglie tutti i suoi sub.");

            // A Light set aside with no Dark at all must not hold the export back, nor count as integration.
            var orphan = File_("Light_orphan.fit", ("EXPTIME", 900), ("CCD-TEMP", -10.0), ("DATE-OBS", "2026-08-14T23:00:00Z"));
            var withOrphan = ProjectAnalyzer.Analyze([.. frames, orphan]);
            Assert(!withOrphan.Ready, "Un Light senza Dark deve risultare da risolvere.");
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { orphan.Path };
            var plan = ProjectExporter.BuildPlan("NGC 6960: Veil", root, withOrphan, excluded);
            Assert(plan.Statistics is { LightCount: 3 } && Math.Abs(plan.Statistics.ExposureSeconds - 900) < 1e-6, "Il Light escluso conta ancora nelle statistiche esportate.");
            Assert(plan.ProjectName == "NGC_6960_Veil", $"Nome cartella: {plan.ProjectName}.");
            Assert(plan.Files.All(file => !file.RelativePath.Contains(':')), "Un percorso del piano contiene i due punti.");
            var darks = plan.Files.Where(file => file.Role == "dark").ToArray();
            var biases = plan.Files.Where(file => file.Role == "bias").ToArray();
            Assert(darks.Length == 5 && biases.Length == 4, $"Attesi 5 Dark e 4 Bias nel piano, trovati {darks.Length} e {biases.Length}.");
            Assert(darks.All(file => !file.RelativePath.Contains("Master", StringComparison.OrdinalIgnoreCase) && file.RelativePath.Contains("DARKSET_G100-T-10-O50")),
                $"I Dark grezzi non devono chiamarsi Master: {darks[0].RelativePath}.");

            var exported = await ProjectExporter.ExecuteAsync(plan, preflightOptions: new(MinimumReserveBytes: 0));
            Assert(ProjectExportPreflight.ExportedSources(root, "NGC 6960: Veil").Count == plan.Files.Count, "Il progetto esportato non viene ritrovato dal suo nome con spazi e due punti.");

            var instance = WbppInstanceGenerator.Generate(plan);
            var text = await File.ReadAllTextAsync(instance);
            var marker = "<td id=\"id\">groups</td>";
            var start = text.IndexOf("<td id=\"value\">", text.IndexOf(marker, StringComparison.Ordinal), StringComparison.Ordinal) + "<td id=\"value\">".Length;
            using var groups = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(text[start..text.IndexOf("</td>", start, StringComparison.Ordinal)])));
            var byType = groups.RootElement.EnumerateArray().ToLookup(group => group.GetProperty("imageType").GetInt32());
            Assert(byType[3].Count() == 1 && byType[3].Single().GetProperty("fileItems").GetArrayLength() == 2, $"I Flat a 2,0 s e 2,5 s dello stesso set devono essere un gruppo WBPP, non {byType[3].Count()}.");
            Assert(byType[2].Count() == 1 && byType[2].Single().GetProperty("fileItems").GetArrayLength() == 5 && !byType[2].Single().GetProperty("hasMaster").GetBoolean(), "I Dark grezzi devono arrivare a WBPP come un gruppo da integrare.");
            Assert(byType[1].Single().GetProperty("fileItems").GetArrayLength() == 4 && byType[4].Single().GetProperty("fileItems").GetArrayLength() == 3, "Bias o Light dell'istanza WBPP non coincidono con il piano.");
            Assert(Directory.Exists(exported) && !Directory.EnumerateFiles(exported, "Light_orphan.fit", SearchOption.AllDirectories).Any(file => !file.Contains("Excluded")), "Il Light escluso è finito tra i Light da elaborare.");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void DarkFlatsStandInForTheBias()
    {
        var settings = new SessionSettings(TimeZoneInfo.Utc, new TimeOnly(12, 0));
        var lights = Enumerable.Range(0, 2).Select(index => ClassifyAt($"df-light-{index}.fits", settings, ("DATE-OBS", $"2026-08-14T22:0{index}:00Z"))).ToArray();
        var flats = Enumerable.Range(0, 2).Select(index => ClassifyAt($"df-flat-{index}.fits", settings, ("IMAGETYP", "FLAT"), ("EXPTIME", 2.5), ("DATE-OBS", $"2026-08-15T04:0{index}:00Z"))).ToArray();
        var dark = ClassifyAt("masterDark_300s.fits", settings, ("IMAGETYP", "Master Dark"));
        // N.I.N.A.'s Flat Wizard: raw Dark Flats at the Flats' exposure, taken warmer than the Lights and with no set point.
        var darkFlats = Enumerable.Range(0, 3).Select(index => ClassifyAt($"df-darkflat-{index}.fits", settings, ("IMAGETYP", "DARKFLAT"), ("EXPTIME", 2.5), ("SET-TEMP", null), ("CCD-TEMP", -4.0 + 0.1 * index))).ToArray();
        Assert(darkFlats.All(frame => frame.Kind == FrameKind.DarkFlat), "IMAGETYP DARKFLAT non classificato come Dark Flat.");

        var withoutThem = ProjectAnalyzer.Analyze([.. lights, .. flats, dark]);
        Assert(!withoutThem.Ready && withoutThem.Lights.All(item => item.Bias.Status == MatchStatus.Missing), "Senza Bias né Dark Flat il progetto non è pronto.");

        var analysis = ProjectAnalyzer.Analyze([.. lights, .. flats, dark, .. darkFlats]);
        Assert(analysis.Ready, $"Dark, Flat e Dark Flat devono bastare: {string.Join(", ", analysis.Lights.Select(item => $"{item.Flat.Status}/{item.Dark.Status}/{item.Bias.Status}"))}.");
        Assert(analysis.Lights.All(item => item.Bias.RequestedKind == FrameKind.DarkFlat && item.DarkFlatSet is { Frames.Count: 3 }), "I Dark Flat non sono stati abbinati al set di Flat.");

        var plan = ProjectExporter.BuildPlan("Dark Flats", Path.GetTempPath(), analysis);
        Assert(!plan.Files.Any(file => file.Role == "bias"), "Senza Bias non deve esserci un Bias nel piano.");
        var exportedDarkFlats = plan.Files.Where(file => file.Frame.Kind == FrameKind.DarkFlat).ToArray();
        Assert(exportedDarkFlats.Length == 3 && exportedDarkFlats.All(file => file.Role == "dark" && file.RelativePath.Contains("DARKFLAT_G100-E2.5s")), "I Dark Flat devono andare a WBPP come Dark del loro set.");
        Assert(!plan.Recipe.Contains("BIASSET"), "I Dark Flat non richiedono la keyword BIASSET.");

        // A Dark Flat at another exposure does not fit these Flats; with a Bias present both are exported.
        var longer = ClassifyAt("df-darkflat-long.fits", settings, ("IMAGETYP", "DARKFLAT"), ("EXPTIME", 8.0));
        Assert(!ProjectAnalyzer.Analyze([.. lights, .. flats, dark, longer]).Ready, "Un Dark Flat di un'altra esposizione non calibra questi Flat.");
        var bias = ClassifyAt("masterBias.fits", settings, ("IMAGETYP", "Master Bias"), ("EXPTIME", 0));
        var both = ProjectExporter.BuildPlan("Both", Path.GetTempPath(), ProjectAnalyzer.Analyze([.. lights, .. flats, dark, bias, .. darkFlats]));
        Assert(both.Files.Count(file => file.Role == "bias") == 1 && both.Files.Count(file => file.Frame.Kind == FrameKind.DarkFlat) == 3, "Con Bias e Dark Flat vanno esportati entrambi.");
    }

    private static void ClosestDarkWinsATie()
    {
        var settings = new SessionSettings(TimeZoneInfo.Utc, new TimeOnly(12, 0));
        var light = Classify(settings, ("DATE-OBS", "2026-08-14T22:00:00Z"));
        FrameMetadata Dark(string name, double temperature, double exposure = 300, bool withOffset = true)
        {
            var frame = Classify(settings, ("IMAGETYP", "Master Dark"), ("SET-TEMP", temperature), ("EXPTIME", exposure));
            if (!withOffset) frame.Offset.SetOriginal(null, MetadataSource.Missing);
            return new FrameMetadata { Path = Path.Combine(Path.GetTempPath(), name), Kind = FrameKind.Dark, IsMaster = true }.CopyFrom(frame);
        }

        var exact = Dark("dark-10.fits", -10);
        var warmer = Dark("dark-08.fits", -8);
        var closest = CalibrationMatcher.Find(light, [warmer, exact], FrameKind.Dark);
        Assert(closest.IsAccepted && closest.Selected?.Frame == exact, $"Tra -10 °C e -8 °C deve vincere il Dark esatto, non {closest.Status}.");

        var colder = Dark("dark-12.fits", -12);
        var symmetric = CalibrationMatcher.Find(light, [warmer, colder], FrameKind.Dark);
        Assert(symmetric.Status == MatchStatus.Ambiguous, "Due Dark alla stessa distanza restano una scelta dell'utente.");

        // A library Master without OFFSET scores higher on bonuses, but a complete Dark must not lose to it.
        var incomplete = Dark("library-dark.fits", -10, withOffset: false);
        incomplete.ConfiguredLibraryPriority = 1;
        var complete = CalibrationMatcher.Find(light, [incomplete, exact], FrameKind.Dark);
        Assert(complete.IsAccepted && complete.Selected?.Frame == exact, $"Un Dark completo deve battere un Master senza offset: {complete.Status}.");
        var onlyIncomplete = CalibrationMatcher.Find(light, [incomplete], FrameKind.Dark);
        Assert(onlyIncomplete.Status == MatchStatus.InsufficientMetadata, "Senza offset il Dark resta da completare.");
    }

    private static FrameMetadata CopyFrom(this FrameMetadata target, FrameMetadata source)
    {
        target.Camera.SetOriginal(source.Camera.Value, MetadataSource.Header);
        target.Width.SetOriginal(source.Width.Value, MetadataSource.Header);
        target.Height.SetOriginal(source.Height.Value, MetadataSource.Header);
        target.XBin.SetOriginal(source.XBin.Value, MetadataSource.Header);
        target.YBin.SetOriginal(source.YBin.Value, MetadataSource.Header);
        target.Gain.SetOriginal(source.Gain.Value, MetadataSource.Header);
        target.Offset.SetOriginal(source.Offset.Value, MetadataSource.Header);
        target.SetTemperatureC.SetOriginal(source.SetTemperatureC.Value, MetadataSource.Header);
        target.ExposureSeconds.SetOriginal(source.ExposureSeconds.Value, MetadataSource.Header);
        return target;
    }

    private static void CaptureTimeIsUtcUnlessTheSiteClockIsKnown()
    {
        var rome = TimeZoneInfo.CreateCustomTimeZone("QA+2", TimeSpan.FromHours(2), "QA+2", "QA+2");
        var settings = new SessionSettings(rome, new TimeOnly(12, 0));

        // ASIAIR writes DATE-OBS only, in UTC and without a zone suffix.
        var asiair = Classify(settings, ("DATE-OBS", "2026-08-14T20:31:04.123456"));
        Assert(asiair.CapturedAt.Value?.UtcDateTime == new DateTime(2026, 8, 14, 20, 31, 4, 123, DateTimeKind.Utc).AddTicks(4560), "DATE-OBS senza suffisso deve essere letto come UTC.");
        Assert(asiair.SiteUtcOffset is null && asiair.SessionId.Value == "2026-08-14", "Un frame con il solo DATE-OBS usa il fuso della macchina per la notte.");
        Assert(AstronomicalSessionResolver.LocalTime(asiair, rome) == new DateTime(2026, 8, 14, 22, 31, 4, 123).AddTicks(4560), "L'ora locale di un frame UTC deve seguire il fuso richiesto.");

        // N.I.N.A. writes both: the difference is the site's offset.
        var nina = Classify(settings, ("DATE-LOC", "2026-08-20T22:18:40.000"), ("DATE-OBS", "2026-08-20T20:18:40.000"));
        Assert(nina.SiteUtcOffset == TimeSpan.FromHours(2), $"Offset del sito non ricavato da DATE-LOC e DATE-OBS: {nina.SiteUtcOffset}.");
        Assert(nina.CapturedAt.Value?.DateTime == new DateTime(2026, 8, 20, 22, 18, 40) && nina.CapturedAt.Value?.UtcDateTime == new DateTime(2026, 8, 20, 20, 18, 40), "L'istante e l'ora locale di N.I.N.A. non coincidono con gli header.");

        // Two clocks that disagree by an odd amount are not an offset.
        var skewed = Classify(settings, ("DATE-LOC", "2026-08-20T22:25:40"), ("DATE-OBS", "2026-08-20T20:18:40"));
        Assert(skewed.SiteUtcOffset is null && skewed.CapturedAt.Value?.UtcDateTime == new DateTime(2026, 8, 20, 20, 18, 40), "Orologi incoerenti: deve restare l'istante UTC.");

        var zulu = Classify(settings, ("DATE-OBS", "2026-07-13T22:30:00Z"));
        Assert(zulu.CapturedAt.Value?.UtcDateTime == new DateTime(2026, 7, 13, 22, 30, 0), "Il suffisso Z deve restare UTC.");
    }

    private static void RemoteSiteKeepsItsOwnNight()
    {
        // A remote observatory at UTC+10 read on a machine at UTC+2: local dusk to dawn is 09:00 to 19:00 UTC,
        // which straddles the machine's noon. Every frame must still fall in the site's night of 14 August.
        var machine = new SessionSettings(TimeZoneInfo.CreateCustomTimeZone("QA+2", TimeSpan.FromHours(2), "QA+2", "QA+2"), new TimeOnly(12, 0));
        var dusk = new DateTime(2026, 8, 14, 19, 0, 0);
        var nights = Enumerable.Range(0, 11).Select(hour =>
        {
            var local = dusk.AddHours(hour);
            return Classify(machine, ("DATE-LOC", local.ToString("s")), ("DATE-OBS", local.AddHours(-10).ToString("s"))).SessionId.Value;
        }).Distinct().ToArray();
        Assert(nights is ["2026-08-14"], $"La notte di un sito remoto è stata divisa: {string.Join(", ", nights)}.");
    }

    private static void NightsAreCountedOncePerProject()
    {
        var settings = new SessionSettings(TimeZoneInfo.Utc, new TimeOnly(12, 0));
        var frames = new List<FrameMetadata>();
        foreach (var night in new[] { 14, 15 })
            foreach (var filter in new[] { "Ha", "OIII", "SII" })
                frames.Add(Classify(settings, ("FILTER", filter), ("DATE-OBS", $"2026-08-{night}T22:00:00Z")));
        var statistics = ProjectStatisticsCalculator.Calculate(ProjectAnalyzer.Analyze(frames));
        Assert(statistics.NightCount == 2 && statistics.Nights.Count == 6, $"Due notti con tre filtri devono contare 2 notti, non {statistics.NightCount}.");
        Assert(statistics.Filters.All(filter => filter.NightCount == 2), "Ogni filtro è stato ripreso in due notti.");
    }

    private static FrameMetadata Classify(SessionSettings settings, params (string Key, object Value)[] extra)
    {
        var headers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["IMAGETYP"] = "Light", ["INSTRUME"] = "QA Camera", ["GAIN"] = 100, ["OFFSET"] = 50, ["SET-TEMP"] = -10,
            ["FILTER"] = "Ha", ["EXPTIME"] = 300, ["NAXIS1"] = 6248, ["NAXIS2"] = 4176, ["XBINNING"] = 1, ["YBINNING"] = 1
        };
        foreach (var (key, value) in extra) headers[key] = value;
        return FrameClassifier.Classify(Path.Combine(Path.GetTempPath(), $"qa-{Guid.NewGuid():N}.fits"), headers, settings);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
