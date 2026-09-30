using AstroForge.Core.Filters;
using AstroForge.Core.Models;
using AstroForge.Core.Parsing;
using AstroForge.Core.Sessions;

internal static class FilterQa
{
    public static void Run()
    {
        var catalog = FilterCatalog.Default;
        Assert(catalog.Filters.Count >= 100, $"Catalogo filtri incompleto: {catalog.Filters.Count} voci.");
        Assert(catalog.Filters.Select(filter => filter.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == catalog.Filters.Count, "Id duplicati nel catalogo filtri.");
        Assert(catalog.Filters.All(filter => filter.Bands.Count > 0 && filter.Bands.All(band => band.FromNm < band.ToNm)), "Bande del catalogo non valide.");
        Assert(catalog.Find("lextreme") is { } lextreme && lextreme.Lines.SequenceEqual([EmissionLines.Oiii, EmissionLines.Ha]), "Le righe dell'L-eXtreme devono essere OIII e Hα.");

        Product("L-eXtreme", "lextreme");
        Product("Optolong L-eXtreme 2\"", "lextreme");
        Product("LEXT", "lextreme");
        Product("L-Pro", "lpro");
        Product("L-Ultimate", "lultimate");
        Product("Antlia ALP-T 5nm", "alpt5");
        Product("Baader Ha 6.5nm", "bd65-Ha");
        Product("Astronomik OIII 6nm", "ast6-OIII");

        // Real listings from AstroBin, the most used first: sizes, series and marketing words around the model name.
        Product("Antlia ALP-T Dual Band 5nm 2\"", "alpt5");
        Product("Optolong L-Quad Enhance 2\"", "lqef");
        Product("IDAS Nebula Booster NBZ 2\"", "nbz");
        Product("IDAS NBZ-II 2\"", "nbz2");
        Product("IDAS NB-1 2\"", "nb1");
        Product("Askar ColourMagic D1 (Ha+Oiii) Duo Narrow Band 6nm 2\"", "askard1");
        Product("Radian Telescopes 2\" Triad Ultra Quad-Band Narrowband filter", "triad");
        Product("Antlia Quad Band Anti-Light Pollution Filter 2\" Mounted", "aquad");
        Product("Antlia 3nm Narrowband H-alpha 2\"", "ant3-Ha");
        Product("Baader H-alpha Ultra-Narrowband 3.5nm (CMOS-Optimized) 36 mm", "bdunb-Ha");
        Product("Chroma OIII 3nm Bandpass 50x50 mm", "chr3-OIII");
        Product("Baader S-II 8nm 2\"", "bdccd-SII");
        Product("Baader H-alpha 7nm 50 mm", "bdccd-Ha");
        Product("Astrodon H-alpha 5nm 1.25\"", "ado5-Ha");
        Product("Astrodon O3 3nm 36mm", "ado3-OIII");
        Product("Antlia EDGE H-alpha 4.5nm 2\"", "edge-Ha");
        Product("Antlia EDGE OIII 4.5 nm 36 mm", "edge-OIII");
        Product("Chroma H-alpha 8nm Bandpass 50 mm", "chr8-Ha");
        Product("ToupTek OIII 6.5nm 1.25\"", "tt65-OIII");
        Product("SVBony SV227 H-Alpha 5nm 2\"", "sv227-Ha");
        Product("Astronomik CLS-CCD 2\"", "clsccd");
        Product("ZWO Seestar S50 Integrated LP Filter", "seestar");
        Product("Altair Ha+OIII ULTRA DualBand 4nm CERTIFIED CMOS 2\"", "altair4");
        Product("Baader Red (CMOS-Optimized) 36 mm", "bdcmos-R");
        Product("Baader UV/IR CUT Luminance (CMOS Optimized) 2\"", "bdcmos-L");
        Generic("Baader Red (R-CCD) 2\"", FilterKind.Broadband, []);
        Generic("Baader UHC-L Booster (CMOS-Optimized) 2''", FilterKind.LightPollution, []);
        Generic("SVBony CLS 2\"", FilterKind.LightPollution, []);
        Generic("Baader H-alpha 35nm", FilterKind.Narrowband, [EmissionLines.Ha]);
        Generic("Chroma Blue 50 mm", FilterKind.Broadband, []);
        Generic("Astrodon Gen2 E-Series Tru-Balance Lum 31mm", FilterKind.Broadband, []);
        Generic("Baader Red (R-CCD) 36 mm", FilterKind.Broadband, []);
        Generic("Astronomik Deep-Sky Green 1.25\"", FilterKind.Broadband, []);
        Generic("Astrodon Blauw Tru-balance E-series Gen 2 1,25\"", FilterKind.Broadband, []);
        Generic("Astronomik L-2 Luminance UV/IR Block 2\"", FilterKind.Broadband, []);
        Generic("Astronomik IR 742nm", FilterKind.Broadband, []);
        Generic("Lumicon Deep Sky 2\"", FilterKind.LightPollution, []);
        Generic("SVBony SV260 Multiband 2\"", FilterKind.Multiband, []);
        Assert(FilterRecognizer.Recognize("Chroma Blue 50 mm").DisplayName == "B", "Un filtro blu di un set LRGB deve risultare B.");
        Assert(FilterRecognizer.Recognize("Astrodon B 31mm").Product is null, "Un alias corto ('NB3') non deve comparire dentro altre parole.");

        Generic("Ha", FilterKind.Narrowband, [EmissionLines.Ha]);
        Generic("H-alpha 7nm", FilterKind.Narrowband, [EmissionLines.Ha]);
        Generic("Hα", FilterKind.Narrowband, [EmissionLines.Ha]);
        Generic("S2", FilterKind.Narrowband, [EmissionLines.Sii]);
        Generic("O3", FilterKind.Narrowband, [EmissionLines.Oiii]);
        Generic("SII 3nm", FilterKind.Narrowband, [EmissionLines.Sii]);
        Generic("HOO", FilterKind.Multiband, [EmissionLines.Ha, EmissionLines.Oiii]);
        Generic("DualBand", FilterKind.Multiband, [EmissionLines.Ha, EmissionLines.Oiii]);
        Generic("L", FilterKind.Broadband, []);
        Generic("Red", FilterKind.Broadband, []);
        Generic("UV/IR Cut", FilterKind.Broadband, []);
        Assert(FilterRecognizer.Recognize("H-alpha 7nm").BandwidthNm == 7, "Larghezza di banda dal nome non letta.");

        foreach (var custom in new[] { "Filtro 3", "Pos4", "Slot 7" })
            Assert(FilterRecognizer.Recognize(custom) is { Source: FilterMatchSource.Unknown, NeedsConfirmation: true }, $"'{custom}' deve restare da confermare.");
        Assert(FilterRecognizer.Recognize(null, colorSensor: true) is { Kind: FilterKind.None, Source: FilterMatchSource.NoFilter }, "Una camera a colori senza filtro deve risultare 'Nessun filtro'.");
        Assert(FilterRecognizer.Recognize(null, colorSensor: false).Source == FilterMatchSource.Unknown, "Una camera mono senza filtro resta sconosciuta.");
        Assert(FilterRecognizer.Recognize("NoFilter").Kind == FilterKind.None, "'NoFilter' deve essere riconosciuto come nessun filtro.");

        var profile = new Dictionary<string, string> { [FilterRecognizer.Normalize("Filtro 3")] = "ant3-SII" };
        var mapped = FilterRecognizer.Recognize("Filtro 3", wheelProfile: profile);
        Assert(mapped.Source == FilterMatchSource.UserProfile && mapped.Product?.Id == "ant3-SII" && mapped.Confidence == 1, "Il profilo della ruota deve prevalere sul riconoscimento automatico.");

        FileNames();
    }

    private static void FileNames()
    {
        var asiair = FileNameMetadata.Parse("Light_Cygnus Loop_300.0s_Bin1_S2_gain100_20260619-231512_-10.0C_0001.fit");
        Assert(asiair.Kind == FrameKind.Light && asiair.Filter == "S2" && asiair.ExposureSeconds == 300 && asiair.Binning == 1 && asiair.Gain == 100 && asiair.SensorTemperatureC == -10,
            $"Nome file ASIAIR non interpretato: {asiair}.");
        var asiairFlat = FileNameMetadata.Parse("Flat_150.0ms_Bin1_Ha_gain100_20260619-061200_-9.8C_0012.fit");
        Assert(asiairFlat.Kind == FrameKind.Flat && asiairFlat.Filter == "Ha" && Math.Abs(asiairFlat.ExposureSeconds!.Value - 0.15) < 1e-9, $"Flat ASIAIR non interpretato: {asiairFlat}.");
        var nina = FileNameMetadata.Parse("2026-06-15_00-21-26_HOO_-10.00_300.00s_0001.fits");
        Assert(nina.Filter == "HOO" && nina.ExposureSeconds == 300 && nina.SensorTemperatureC == -10, $"Nome file N.I.N.A. non interpretato: {nina}.");
        var osc = FileNameMetadata.Parse("Light_M31_120.0s_Bin1_gain100_20260619-231512_-10.0C_0001.fit");
        Assert(osc.Filter is null && osc.Gain == 100, $"Un ASIAIR senza ruota non deve inventare un filtro: {osc}.");

        var headers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["IMAGETYP"] = "Light", ["INSTRUME"] = "ZWO ASI2600MM Pro", ["EXPTIME"] = 300.0, ["GAIN"] = 100L,
            ["XBINNING"] = 1L, ["YBINNING"] = 1L, ["DATE-OBS"] = "2026-06-19T23:15:12"
        };
        var frame = FrameClassifier.Classify(Path.Combine(Path.GetTempPath(), "Light_Cygnus_300.0s_Bin1_S2_gain100_20260619-231512_-10.0C_0001.fit"), headers, new(TimeZoneInfo.Utc, new TimeOnly(12, 0)));
        Assert(frame.FilterName.Value == "S2" && frame.FilterName.Source == MetadataSource.Filename, "Il filtro dal nome file deve riempire l'header mancante.");
        Assert(frame.Gain.Source == MetadataSource.Header && frame.SensorTemperatureC is { Value: -10, Source: MetadataSource.Filename }, "L'header deve prevalere sul nome file.");
        Assert(!frame.Issues.Any(issue => issue.Code == "metadata.filter_missing"), "Filtro ricavato dal nome file ma ancora segnalato come mancante.");

        headers["BAYERPAT"] = "RGGB"; headers["INSTRUME"] = "ZWO ASI2600MC Pro";
        var oscFrame = FrameClassifier.Classify(Path.Combine(Path.GetTempPath(), "Light_M31_120.0s_Bin1_gain100_20260619-231512_-10.0C_0001.fit"), headers, new(TimeZoneInfo.Utc, new TimeOnly(12, 0)));
        Assert(oscFrame.FilterName.Value is null && !oscFrame.Issues.Any(issue => issue.Code == "metadata.filter_missing"), "Una camera a colori senza filtro non deve generare avvisi.");
    }

    /// <summary>
    /// Share of AstroBin images, weighted by use, whose filter name maps to a catalogue product, to a generic class, or to nothing.
    /// The list is scripts/data/astrobin-equipment.json in Skyframe.
    /// </summary>
    public static void Benchmark(string astrobinList)
    {
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(astrobinList));
        var rows = document.RootElement.GetProperty("filters").EnumerateArray()
            .Select(item => (Name: item.GetProperty("name").GetString() ?? "", Count: item.GetProperty("count").GetInt32()))
            .Select(row => (row.Name, row.Count, Identity: FilterRecognizer.Recognize(row.Name))).ToArray();
        double total = rows.Sum(row => row.Count);
        string Share(FilterMatchSource source) => (rows.Where(row => row.Identity.Source == source).Sum(row => row.Count) / total).ToString("P1", System.Globalization.CultureInfo.InvariantCulture);
        Console.WriteLine($"{rows.Length} nomi, {total} immagini, {FilterCatalog.Default.Filters.Count} filtri a catalogo");
        Console.WriteLine($"prodotto {Share(FilterMatchSource.CatalogProduct)} · classe generica {Share(FilterMatchSource.GenericName)} · sconosciuto {Share(FilterMatchSource.Unknown)}");
        Console.WriteLine("Nomi più usati senza prodotto:");
        foreach (var row in rows.Where(row => row.Identity.Source != FilterMatchSource.CatalogProduct).OrderByDescending(row => row.Count).Take(40))
            Console.WriteLine($"{row.Count,6}  {row.Identity.DisplayName,-28} {row.Name}");
    }

    private static void Product(string raw, string expectedId)
    {
        var identity = FilterRecognizer.Recognize(raw);
        Assert(identity.Product?.Id == expectedId && identity.Source == FilterMatchSource.CatalogProduct, $"'{raw}' riconosciuto come {identity.Product?.Id ?? identity.DisplayName} ({identity.Source}), atteso {expectedId}.");
    }

    private static void Generic(string raw, FilterKind kind, EmissionLine[] lines)
    {
        var identity = FilterRecognizer.Recognize(raw);
        Assert(identity.Kind == kind && identity.Lines.SequenceEqual(lines) && identity.IsRecognized,
            $"'{raw}' riconosciuto come {identity.Kind} [{string.Join(", ", identity.Lines.Select(line => line.Name))}] ({identity.Source}).");
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
