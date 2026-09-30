using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using AstroForge.Core.Analysis;
using AstroForge.Core.Demo;
using AstroForge.Core.Equipment;
using AstroForge.Core.Export;
using AstroForge.Core.Filters;
using AstroForge.Core.Matching;
using AstroForge.Core.Models;
using AstroForge.Core.Parsing;
using AstroForge.Core.Quality;
using AstroForge.Core.Scanning;
using AstroForge.Core.Sessions;
using AstroForge.Core.Wbpp;

/// <summary>
/// End-to-end run on the demo Cygnus Loop dataset: real FITS files on disk, from scan to WBPP instance, the same path
/// a user follows in the app. The dataset is also what the tutorials use, so these checks pin its story too.
/// </summary>
internal static class DemoProjectE2e
{
    public static async Task RunAsync()
    {
        // Run as the typical user does: on a system with Italian number formatting.
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("it-IT");
        try { await RunCoreAsync(); }
        finally { CultureInfo.CurrentCulture = culture; }
        Console.WriteLine("PASS E2E: progetto demo Cygnus Loop (ASIAIR + N.I.N.A.) scansionato, calibrato, filtrato dal Quality Lab, esportato e convertito in istanza WBPP.");
    }

    private static async Task RunCoreAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AstroForge-Demo-{Guid.NewGuid():N}");
        try
        {
            var dataset = await DemoDatasetGenerator.GenerateAsync(Path.Combine(root, "dati"));
            await DeterministicAsync(dataset, Path.Combine(root, "copia"));

            var settings = new SessionSettings(RomeTimeZone(), new TimeOnly(12, 0));
            var frames = await new ProjectScanner().ScanAsync([dataset.Root], settings);
            Scan(dataset, frames);
            Recognition(frames);

            var analysis = ProjectAnalyzer.Analyze(frames);
            Calibration(analysis);

            var cloudy = await QualityAsync(dataset, analysis);
            await ExportAndWbppAsync(analysis, Path.Combine(root, "progetti"), cloudy);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static async Task DeterministicAsync(DemoDataset dataset, string otherRoot)
    {
        var again = await DemoDatasetGenerator.GenerateAsync(otherRoot);
        Assert(again.Frames.Select(frame => frame.RelativePath).SequenceEqual(dataset.Frames.Select(frame => frame.RelativePath)), "Il dataset demo deve avere sempre gli stessi file.");
        foreach (var frame in dataset.Frames.Where((_, index) => index % 7 == 0))
            Assert(Hash(dataset.PathOf(frame)) == Hash(again.PathOf(frame)), $"Il dataset demo non è deterministico: {frame.RelativePath}.");
        var bytes = dataset.Frames.Sum(frame => new FileInfo(dataset.PathOf(frame)).Length);
        Assert(bytes < 8 * 1024 * 1024, $"Il dataset demo deve restare piccolo: {bytes / 1024 / 1024} MB.");
        Assert(File.Exists(Path.Combine(dataset.Root, "astroforge-demo.json")), "Manifest del dataset demo assente.");
    }

    private static void Scan(DemoDataset dataset, IReadOnlyList<FrameMetadata> frames)
    {
        Assert(frames.Count == dataset.Frames.Count, $"Attesi {dataset.Frames.Count} file FITS, scansionati {frames.Count}.");
        Assert(frames.All(frame => !frame.Issues.Any(issue => issue.Severity == IssueSeverity.Error)), "Il dataset demo non deve produrre errori di lettura: " +
            string.Join(", ", frames.SelectMany(frame => frame.Issues.Where(issue => issue.Severity == IssueSeverity.Error).Select(issue => $"{frame.FileName}: {issue.Code}"))));
        foreach (var expected in dataset.Frames)
        {
            var frame = frames.Single(item => Path.GetFullPath(item.Path) == dataset.PathOf(expected));
            Assert(frame.Kind == expected.Kind, $"{expected.RelativePath}: tipo {frame.Kind}, atteso {expected.Kind}.");
            Assert(frame.FilterName.Value == expected.FilterName, $"{expected.RelativePath}: filtro '{frame.FilterName.Value}', atteso '{expected.FilterName}'.");
            Assert(frame.SessionId.Value == expected.SessionId, $"{expected.RelativePath}: notte {frame.SessionId.Value}, attesa {expected.SessionId}.");
            Assert(frame.Camera.Value == DemoDatasetGenerator.Camera && frame.Gain.Value == DemoDatasetGenerator.Gain && frame.Offset.Value == DemoDatasetGenerator.Offset,
                $"{expected.RelativePath}: camera, gain o offset non letti.");
            Assert(frame.Width.Value == DemoDatasetGenerator.Width && frame.Height.Value == DemoDatasetGenerator.Height && frame.XBin.Value == 1, $"{expected.RelativePath}: geometria errata.");
            Assert(frame.IsMaster == (expected.Software == DemoCaptureSoftware.Library), $"{expected.RelativePath}: stato Master errato.");
            Assert(frame.EffectiveTemperatureC is { } temperature && Math.Abs(temperature - DemoDatasetGenerator.SetTemperatureC) <= 0.2, $"{expected.RelativePath}: temperatura non letta.");
            Assert(!frame.Issues.Any(issue => issue.Code == "metadata.filter_missing"), $"{expected.RelativePath}: filtro segnalato come mancante.");

            if (expected.Software == DemoCaptureSoftware.Asiair)
            {
                // ASIAIR with a filter wheel writes the filter both in FILTER and in the file name: they must agree.
                var name = FileNameMetadata.Parse(frame.Path);
                Assert(name.Kind == expected.Kind && name.Filter == frame.FilterName.Value && name.Gain == frame.Gain.Value && name.ExposureSeconds == frame.ExposureSeconds.Value,
                    $"{expected.RelativePath}: nome file ASIAIR e header non concordano ({name}).");
                Assert(frame.SetTemperatureC.Value is null && frame.SensorTemperatureC.Source == MetadataSource.Header, $"{expected.RelativePath}: ASIAIR scrive solo CCD-TEMP.");
                Assert(frame.ObjectName.Value is null && frame.FocalLengthMm.Value == DemoDatasetGenerator.FocalLengthMm, $"{expected.RelativePath}: OBJECT o FOCALLEN ASIAIR errati.");
            }
            if (expected.Software == DemoCaptureSoftware.Nina)
            {
                var name = FileNameMetadata.Parse(frame.Path);
                Assert(name.Filter == expected.FilterName && name.ExposureSeconds == frame.ExposureSeconds.Value, $"{expected.RelativePath}: nome file N.I.N.A. non interpretato ({name}).");
                Assert(frame.SetTemperatureC.Value == DemoDatasetGenerator.SetTemperatureC && frame.CapturedAt.Value is not null, $"{expected.RelativePath}: SET-TEMP o DATE-LOC N.I.N.A. non letti.");
                Assert(expected.Kind != FrameKind.Light || frame.ObjectName.Value == DemoDatasetGenerator.Target, $"{expected.RelativePath}: OBJECT N.I.N.A. non letto.");
            }
        }
        Assert(frames.Count(frame => frame.Kind == FrameKind.Light) == 16 && frames.Count(frame => frame.Kind == FrameKind.Flat) == 12, "Conteggio Light o Flat del demo errato.");
    }

    private static void Recognition(IReadOnlyList<FrameMetadata> frames)
    {
        var camera = EquipmentRecognizer.Camera(DemoDatasetGenerator.Camera);
        Assert(camera.Camera?.Id == "asi2600mm" && camera.Type == CameraSensorType.Mono, "La camera del demo deve essere la ASI2600MM Pro mono del catalogo.");
        var telescope = EquipmentRecognizer.Telescope(DemoDatasetGenerator.Telescope, DemoDatasetGenerator.FocalLengthMm);
        Assert(telescope.Telescope?.Id == "fra400" && telescope.Reducer?.Factor == 0.7, "FRA400 + riduttore 0,7× non riconosciuti da TELESCOP e FOCALLEN.");

        var names = frames.Select(frame => frame.FilterName.Value).OfType<string>().Distinct().Order().ToArray();
        Assert(names.SequenceEqual(["Filtro 3", "Ha", "OIII"]), $"Filtri del demo inattesi: {string.Join(", ", names)}.");
        Assert(FilterRecognizer.Recognize("Ha") is { Kind: FilterKind.Narrowband } ha && ha.Lines.SequenceEqual([EmissionLines.Ha]), "Hα non riconosciuto.");
        Assert(FilterRecognizer.Recognize("OIII") is { Kind: FilterKind.Narrowband } oiii && oiii.Lines.SequenceEqual([EmissionLines.Oiii]), "OIII non riconosciuto.");
        // "Filtro 3" says nothing about the glass: Forge asks once, then the wheel profile answers for good.
        Assert(FilterRecognizer.Recognize(DemoDatasetGenerator.CustomFilterName).NeedsConfirmation, "'Filtro 3' deve richiedere conferma senza profilo.");
        var dataset = new Dictionary<string, string> { [FilterRecognizer.Normalize(DemoDatasetGenerator.CustomFilterName)] = DemoDatasetGenerator.CustomFilterCatalogId };
        var sii = FilterRecognizer.Recognize(DemoDatasetGenerator.CustomFilterName, wheelProfile: dataset);
        Assert(sii is { Source: FilterMatchSource.UserProfile, Kind: FilterKind.Narrowband, NeedsConfirmation: false } && sii.Lines.SequenceEqual([EmissionLines.Sii]),
            "Con il profilo della ruota 'Filtro 3' deve risultare SII.");
    }

    private static void Calibration(ProjectAnalysis analysis)
    {
        Assert(analysis.Ready, $"Il progetto demo deve essere pronto: {analysis.UnresolvedCount} calibrazioni irrisolte. " +
            string.Join(" | ", analysis.Lights.Where(item => !item.Flat.IsAccepted || !item.Dark.IsAccepted || !item.Bias.IsAccepted)
                .Select(item => $"{item.Light.FileName}: Flat {item.Flat.Status}, Dark {item.Dark.Status}, Bias {item.Bias.Status}")));
        Assert(analysis.FlatGroups.Count == 4 && analysis.FlatGroups.All(group => group.Frames.Count == 3), $"Attesi 4 Flat Set da 3 file, trovati {analysis.FlatGroups.Count}.");
        foreach (var item in analysis.Lights)
        {
            var light = item.Light;
            Assert(item.FlatGroup!.Frames.All(flat => flat.FilterName.Value == light.FilterName.Value && flat.SessionId.Value == light.SessionId.Value),
                $"{light.FileName}: Flat di un'altra notte o di un altro filtro.");
            Assert(item.Dark.Selected!.Frame.IsMaster && item.Dark.Selected.Frame.ExposureSeconds.Value == light.ExposureSeconds.Value, $"{light.FileName}: Master Dark errato.");
            Assert(item.Bias.Selected!.Frame.IsMaster && item.Bias.Selected.Frame.Kind == FrameKind.Bias, $"{light.FileName}: Master Bias errato.");
        }
        var statistics = ProjectStatisticsCalculator.Calculate(analysis);
        Assert(statistics.LightCount == 16 && Math.Abs(statistics.ExposureHours - 16 * 300 / 3600d) < 1e-9, "Integrazione totale del demo errata.");
        // Nights are counted per filter: Hα on both nights, OIII on the first, SII on the second.
        Assert(statistics.FilterCount == 3 && statistics.NightCount == 4 && statistics.Nights.Select(night => night.Night).Distinct().Count() == 2,
            $"Filtri o notti del demo errati: {statistics.FilterCount} filtri, {statistics.NightCount} notti filtro.");
        var recipe = WbppRecipeEngine.Recommend(analysis);
        // Hα spans both nights with different dust shadows: WBPP must keep the two Flat Sets apart.
        Assert(recipe.Keywords.Select(keyword => keyword.Keyword).SequenceEqual(["FLATSET"]) && recipe.Keywords[0].Pre && !recipe.Keywords[0].Post,
            $"Ricetta WBPP del demo errata: {string.Join(", ", recipe.Keywords.Select(keyword => keyword.Keyword))}.");
    }

    private static async Task<string> QualityAsync(DemoDataset dataset, ProjectAnalysis analysis)
    {
        var metrics = new List<QualityMetrics>();
        foreach (var item in analysis.Lights) metrics.Add(await FitsQualityAnalyzer.AnalyzeAsync(item.Light.Path));
        var cloudyPath = dataset.PathOf(dataset.Frames.Single(frame => frame.Cloudy));
        var cloudy = metrics.Single(item => Path.GetFullPath(item.Path) == cloudyPath);
        var clear = metrics.Where(item => item != cloudy).ToArray();
        Assert(clear.All(item => item.StarCount >= 20 && item.FwhmPixels is > 1.5 and < 4.5), "Le stelle dei Light sereni devono essere rilevate: " +
            string.Join(", ", clear.Select(item => $"{Path.GetFileName(item.Path)} {item.StarCount} stelle FWHM {item.FwhmPixels:0.0}")));
        var sameFilter = clear.Where(item => item.Path.Contains(DemoDatasetGenerator.CustomFilterName)).ToArray();
        Assert(sameFilter.All(item => cloudy.StarCount < item.StarCount / 2 && cloudy.Background > item.Background + 200 && cloudy.Snr < item.Snr),
            $"Il Light velato deve risultare il peggiore del suo filtro: {cloudy.StarCount} stelle, fondo {cloudy.Background:0}, SNR {cloudy.Snr:0.0}.");
        return cloudy.Path;
    }

    private static async Task ExportAndWbppAsync(ProjectAnalysis analysis, string destination, string cloudy)
    {
        var plan = ProjectExporter.BuildPlan("Cygnus Loop demo", destination, analysis, new HashSet<string>(StringComparer.Ordinal) { cloudy });
        Assert(plan.Files.Count(file => file.Role == "light") == 15 && plan.Files.Count(file => file.Role == "excluded-light") == 1, "Piano export: Light inclusi o esclusi errati.");
        Assert(plan.Files.Count(file => file.Role == "flat") == 12 && plan.Files.Count(file => file.Role == "dark") == 1 && plan.Files.Count(file => file.Role == "bias") == 1, "Piano export: calibrazioni errate.");
        var sep = Path.DirectorySeparatorChar;
        Assert(plan.Files.Where(file => file.Role == "light").All(file => file.RelativePath.StartsWith($"Light{sep}FILTER_") && file.RelativePath.Contains($"{sep}FLATSET_AUTO-") && file.RelativePath.Contains($"{sep}NIGHT_")),
            "I Light devono essere raggruppati per filtro, Flat Set e notte.");
        Assert(plan.Files.Any(file => file.RelativePath.Contains("FILTER_Filtro_3")), "Il nome del filtro personalizzato deve diventare un nome cartella sicuro.");

        var exported = await ProjectExporter.ExecuteAsync(plan);
        Assert(plan.Files.All(file => File.Exists(Path.Combine(exported, file.RelativePath))), "Export demo incompleto.");
        foreach (var file in plan.Files.Where((_, index) => index % 5 == 0))
            Assert(Hash(Path.Combine(exported, file.RelativePath)) == Hash(file.Frame.Path), $"Copia alterata durante l'export: {file.RelativePath}.");
        Assert(File.Exists(Path.Combine(exported, "_AstroForge", "manifest.json")) && File.Exists(Path.Combine(exported, "_AstroForge", "project-statistics.csv")), "Manifest o statistiche dell'export demo assenti.");

        var instance = WbppInstanceGenerator.Generate(plan);
        Assert(File.Exists(instance) && File.Exists(WbppInstanceGenerator.LauncherPath(instance)), "Istanza WBPP o launcher assenti.");
        XNamespace xpsm = "http://www.pixinsight.com/xpsm";
        var parameters = XDocument.Load(instance).Descendants(xpsm + "tr")
            .ToDictionary(row => row.Elements().First(cell => (string?)cell.Attribute("id") == "id").Value, row => row.Elements().First(cell => (string?)cell.Attribute("id") == "value").Value);
        string Decode(string key) => Encoding.UTF8.GetString(Convert.FromBase64String(parameters[key]));
        Assert(parameters["groupingKeywordsEnabled"] == "true" && Decode("keywords").Contains("\"FLATSET\""), "Keyword FLATSET assente dall'istanza WBPP.");

        using var groups = JsonDocument.Parse(Decode("groups"));
        var items = groups.RootElement.EnumerateArray()
            .SelectMany(group => group.GetProperty("fileItems").EnumerateArray().Select(item => (Type: group.GetProperty("imageType").GetInt32(), Path: item.GetProperty("filePath").GetString()!)))
            .ToArray();
        Assert(items.Count(item => item.Type == 4) == 15 && items.Count(item => item.Type == 3) == 12 && items.Count(item => item.Type == 2) == 1 && items.Count(item => item.Type == 1) == 1,
            $"Gruppi WBPP del demo errati: {string.Join(", ", items.GroupBy(item => item.Type).Select(group => $"{group.Key}×{group.Count()}"))}.");
        Assert(items.All(item => File.Exists(item.Path) && Path.GetFullPath(item.Path).StartsWith(Path.GetFullPath(exported))), "L'istanza WBPP punta a file fuori dal progetto esportato.");
        Assert(!items.Any(item => item.Path.Contains("Excluded")), "Il Light escluso dal Quality Lab è finito nell'istanza WBPP.");
        var lightFilters = groups.RootElement.EnumerateArray().Where(group => group.GetProperty("imageType").GetInt32() == 4)
            .Select(group => group.GetProperty("filter").GetString()).Distinct().Order().ToArray();
        Assert(lightFilters.SequenceEqual(["Filtro 3", "Ha", "OIII"]), $"Filtri dei gruppi Light WBPP errati: {string.Join(", ", lightFilters)}.");
    }

    private static TimeZoneInfo RomeTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"); }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
