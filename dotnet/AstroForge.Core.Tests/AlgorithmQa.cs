using AstroForge.Core.Analysis;
using AstroForge.Core.Equipment;
using AstroForge.Core.Filters;
using AstroForge.Core.IO;
using AstroForge.Core.Scanning;
using AstroForge.Core.Matching;
using AstroForge.Core.Models;
using AstroForge.Core.Parsing;
using AstroForge.Core.Quality;
using AstroForge.Core.Sessions;
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
        Console.WriteLine("PASS: FWHM su stelle sintetiche, ora di scatto, notti di un sito remoto, conteggio notti, spareggio dei Dark, nomi file, cartelle libreria e card FITS/XISF verificati.");
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
