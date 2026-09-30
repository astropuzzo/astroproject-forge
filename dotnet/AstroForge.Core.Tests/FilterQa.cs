using AstroForge.Core.Filters;
using AstroForge.Core.Models;
using AstroForge.Core.Parsing;
using AstroForge.Core.Sessions;

internal static class FilterQa
{
    public static void Run()
    {
        var catalog = FilterCatalog.Default;
        Assert(catalog.Filters.Count >= 80, $"Catalogo filtri incompleto: {catalog.Filters.Count} voci.");
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
