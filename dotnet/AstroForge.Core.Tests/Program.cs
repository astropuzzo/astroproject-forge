using AstroForge.Core.Scanning;
using AstroForge.Core.Sessions;
using AstroForge.Core.Analysis;
using AstroForge.Core.Validation;
using AstroForge.Core.Wbpp;
using AstroForge.Core.Export;
using AstroForge.Core.Models;
using AstroForge.Core.Matching;
using AstroForge.Core.Diagnostics;
using AstroForge.Core.IO;
using System.IO.Compression;

Assert(PathIdentity.Comparer.Equals("Frame.fit", "frame.fit") == (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()), "La semantica dei path deve seguire il filesystem host.");
var pathRoot = Path.Combine(Path.GetTempPath(), "AstroForge-PathRoot");
Assert(PathIdentity.IsWithin(Path.Combine(pathRoot, "nested", "frame.fit"), pathRoot), "Il controllo di contenimento path non riconosce un discendente valido.");
Assert(!PathIdentity.IsWithin(pathRoot + "-other", pathRoot), "Il controllo di contenimento path accetta un prefisso fratello.");

var lightBeforeMidnight = Synthetic(FrameKind.Light, "2026-06-15_00-21-26_SIOIII.fits", "SIOIII", new DateTimeOffset(2026, 6, 15, 0, 21, 26, TimeSpan.Zero));
lightBeforeMidnight.SetTemperatureC.SetOriginal(-10, MetadataSource.Header);
var hooLight = Synthetic(FrameKind.Light, "2026-06-25_00-57-51_HOO.fits", "HOO", new DateTimeOffset(2026, 6, 25, 0, 57, 51, TimeSpan.Zero));
hooLight.SetTemperatureC.SetOriginal(0, MetadataSource.Header);
var sioFlat = Synthetic(FrameKind.Flat, "flat-SIOIII.fits", "SIOIII", new DateTimeOffset(2026, 6, 18, 3, 48, 20, TimeSpan.Zero));
var hooFlat = Synthetic(FrameKind.Flat, "flat-HOO.fits", "HOO", new DateTimeOffset(2026, 6, 28, 4, 31, 18, TimeSpan.Zero));
var darkMinusTen = SyntheticMaster(FrameKind.Dark, Path.Combine("library", "GAIN_100", "-10", "600s.xisf"), -10);
var darkZero = SyntheticMaster(FrameKind.Dark, Path.Combine("library", "GAIN_100", "0", "600s.xisf"), 0);
var bias100 = SyntheticMaster(FrameKind.Bias, Path.Combine("library", "GAIN_100", "masterBias100.xisf"), null);
var frames = new List<FrameMetadata> { lightBeforeMidnight, hooLight, sioFlat, hooFlat, darkMinusTen, darkZero, bias100 };
Assert(frames.Count == 7, $"Attesi 7 frame sintetici, trovati {frames.Count}.");
Assert(frames.Count(frame => frame.Kind == AstroForge.Core.Models.FrameKind.Light) == 2, "Light non riconosciuti.");
Assert(frames.Count(frame => frame.Kind == AstroForge.Core.Models.FrameKind.Flat) == 2, "Flat non riconosciuti.");
Assert(frames.Count(frame => frame.Kind == AstroForge.Core.Models.FrameKind.Dark) == 2, "Dark non riconosciuti.");
Assert(frames.Count(frame => frame.Kind == AstroForge.Core.Models.FrameKind.Bias) == 1, "Bias non riconosciuti.");
var genericHeaders = new Dictionary<string, object?>
{
    ["OBSTYPE"] = "SCIENCE", ["DETECTOR"] = "Generic CMOS", ["FILTERID"] = "Ha",
    ["EXPOSURETIME"] = "180", ["CCDGAIN"] = "120", ["BLACKLVL"] = "30", ["SENSOR-T"] = "-15",
    ["CCDXBIN"] = "2", ["CCDYBIN"] = "2", ["NAXIS1"] = "3000", ["NAXIS2"] = "2000",
    ["READMODE"] = "High Gain", ["DATE-OBS"] = "2026-07-13T22:30:00Z"
};
var genericFrame = AstroForge.Core.Parsing.FrameClassifier.Classify(Path.Combine(Path.GetTempPath(), "dark_name_but_science_header_0042.fit"), genericHeaders, SessionSettings.DefaultForLocalMachine());
Assert(genericFrame.Kind == FrameKind.Light, "Un header generico valido deve prevalere su un nome file fuorviante.");
Assert(genericFrame.Camera.Value == "Generic CMOS" && genericFrame.Gain.Value == 120 && genericFrame.Offset.Value == 30 && genericFrame.FilterName.Value == "Ha", "Alias FITS multiproduttore non risolti.");
Assert(genericFrame.Camera.Source == MetadataSource.Header && genericFrame.XBin.Value == 2 && genericFrame.ReadoutMode.Value == "High Gain", "I metadati generici devono mantenere provenienza Header.");
Assert(lightBeforeMidnight.SessionId.Value == "2026-06-14", $"Sessione astronomica errata: {lightBeforeMidnight.SessionId.Value}");
Assert(darkMinusTen.Gain.Value == 100 && darkMinusTen.EffectiveTemperatureC == -10, "Metadati libreria Dark non risolti.");
Assert(bias100.Gain.Value == 100, "Gain Master Bias non risolto.");
var defaulted = ProjectMetadataDefaultsResolver.Apply(frames, new(100, 51, null));
Assert(defaulted > 0, "I fallback progetto non sono stati applicati ai Master incompleti.");
Assert(frames.Where(frame => frame.IsMaster).All(frame => frame.Offset.Value == 51 && frame.Offset.Source == MetadataSource.ProjectDefault), "Offset fallback Master errato.");
Assert(lightBeforeMidnight.Gain.Source == MetadataSource.Header, "Il fallback non deve sostituire il Gain presente nell'header Light.");
var sameTechnicalSignature = Synthetic(FrameKind.Light, "same-signature-other-night.fits", "HOO", new DateTimeOffset(2026, 7, 1, 23, 0, 0, TimeSpan.Zero));
sameTechnicalSignature.SetTemperatureC.SetOriginal(-10, MetadataSource.Header);
Assert(CalibrationScopeMatcher.Matches(lightBeforeMidnight, sameTechnicalSignature, FrameKind.Dark), "La firma tecnica deve attraversare filtro e notte quando camera e parametri coincidono.");
sameTechnicalSignature.Gain.SetOverride(200);
Assert(!CalibrationScopeMatcher.Matches(lightBeforeMidnight, sameTechnicalSignature, FrameKind.Dark), "Gain diversi non devono condividere una calibrazione batch.");
sameTechnicalSignature.Gain.ClearOverride();
sameTechnicalSignature.ExposureSeconds.SetOverride(300);
Assert(!CalibrationScopeMatcher.Matches(lightBeforeMidnight, sameTechnicalSignature, FrameKind.Dark), "Un Dark non deve essere applicato a esposizioni diverse.");
Assert(CalibrationScopeMatcher.Matches(lightBeforeMidnight, sameTechnicalSignature, FrameKind.Bias), "L'esposizione non deve separare l'ambito di un Bias.");
var duplicateBias = CalibrationCopy(bias100, Path.Combine(Path.GetTempPath(), "duplicate-masterBias100.xisf"));
var preferredBias = CalibrationMatcher.Find(lightBeforeMidnight, [bias100, duplicateBias], FrameKind.Bias);
var lowerPriorityBias = CalibrationCopy(bias100, Path.Combine(Path.GetTempPath(), "secondary-library", "masterBias100.xisf"));
lowerPriorityBias.Gain.SetOriginal(100, MetadataSource.LibraryPath);
bias100.ConfiguredLibraryPriority = 1; lowerPriorityBias.ConfiguredLibraryPriority = 2;
var priorityMatch = CalibrationMatcher.Find(lightBeforeMidnight, [lowerPriorityBias, bias100], FrameKind.Bias);
Assert(priorityMatch.Selected?.Frame == bias100, "La libreria con priorità più alta deve vincere tra Master equivalenti.");
Assert(preferredBias.Selected?.Frame == bias100, "La libreria configurata deve avere priorità su copie Master equivalenti trovate nelle sorgenti.");
lightBeforeMidnight.ManualBiasPath.SetOverride(duplicateBias.Path);
var manualMasterAnalysis = ProjectAnalyzer.Analyze(frames.Append(duplicateBias));
Assert(manualMasterAnalysis.Lights.Single(item => item.Light == lightBeforeMidnight).Bias.Selected?.Frame == duplicateBias, "L'assegnazione manuale Bias deve avere precedenza sull'automatismo.");
lightBeforeMidnight.ManualBiasPath.ClearOverride();
var analysis = ProjectAnalyzer.Analyze(frames);
Assert(analysis.Ready, $"Analisi calibrazioni non pronta: {analysis.UnresolvedCount} casi irrisolti.");
var statistics = ProjectStatisticsCalculator.Calculate(analysis);
Assert(statistics.LightCount == 2 && Math.Abs(statistics.ExposureSeconds - 1200) < 0.001, "Statistiche integrazione reali errate.");
Assert(statistics.FilterCount == 2 && statistics.NightCount == 2, "Conteggio filtri o notti errato.");
var sioiii = analysis.Lights.Single(item => item.Light.FilterName.Value == "SIOIII");
var hoo = analysis.Lights.Single(item => item.Light.FilterName.Value == "HOO");
Assert(sioiii.Dark.Selected?.Frame.Path.EndsWith(@"GAIN_100\-10\600s.xisf", StringComparison.OrdinalIgnoreCase) == true, "Dark SIOIII errato.");
Assert(hoo.Dark.Selected?.Frame.Path.EndsWith(@"GAIN_100\0\600s.xisf", StringComparison.OrdinalIgnoreCase) == true, "Dark HOO errato.");
Assert(sioiii.Bias.Selected?.Frame.FileName == "masterBias100.xisf" && hoo.Bias.Selected?.Frame.FileName == "masterBias100.xisf", "Bias gain 100 errato.");
var recipe = WbppRecipeEngine.Recommend(analysis);
Assert(recipe.Keywords.Count == 1 && recipe.Keywords[0].Keyword == "DARKSET" && recipe.Keywords[0].Pre && !recipe.Keywords[0].Post, "Ricetta WBPP adattiva errata.");
var qualityExclusionPlan = ProjectExporter.BuildPlan("quality-exclusion", Path.GetTempPath(), analysis, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { hoo.Light.Path });
Assert(qualityExclusionPlan.Files.Any(item => item.Frame == hoo.Light && item.Role == "excluded-light" && item.RelativePath.StartsWith(Path.Combine("Excluded", "Quality"))), "Un Light escluso dal Quality Lab non è stato separato dall'insieme WBPP.");
Assert(!qualityExclusionPlan.Files.Any(item => item.Frame == hoo.Light && item.Role == "light"), "Un Light escluso compare ancora nel dataset WBPP.");

var epochFrames = new List<FrameMetadata>();
var flippedLight = Synthetic(FrameKind.Light, "flipped-light.fits", "HOO", new DateTimeOffset(2026, 6, 29, 1, 0, 0, TimeSpan.Zero));
var flippedFlat = Synthetic(FrameKind.Flat, "flipped-flat.fits", "HOO", new DateTimeOffset(2026, 6, 28, 2, 0, 0, TimeSpan.Zero));
flippedLight.RotatorAngleDeg.SetOriginal(0.30, MetadataSource.Header); flippedFlat.RotatorAngleDeg.SetOriginal(180.38, MetadataSource.Header);
var flipMatch = CalibrationMatcher.Find(flippedLight, [flippedFlat], FrameKind.Flat);
Assert(flipMatch.IsAccepted, "Un Flat equivalente dopo meridian flip deve essere compatibile modulo 180 gradi.");
flippedLight.RotatorAngleDeg.SetOriginal(178.73, MetadataSource.Header);
var softRotatorMatch = CalibrationMatcher.Find(flippedLight, [flippedFlat], FrameKind.Flat);
Assert(softRotatorMatch.IsAccepted && softRotatorMatch.Status == MatchStatus.WithinTolerance, "Il solo angolo rotatore non deve rendere incompatibile un Flat altrimenti valido.");
for (var day = 1; day <= 6; day++) epochFrames.Add(Synthetic(FrameKind.Light, $"hoo-a-{day}.fits", "HOO", new DateTimeOffset(2026, 6, day, 23, 0, 0, TimeSpan.Zero)));
for (var day = 10; day <= 12; day++) epochFrames.Add(Synthetic(FrameKind.Light, $"hoo-b-{day}.fits", "HOO", new DateTimeOffset(2026, 6, day, 23, 0, 0, TimeSpan.Zero)));
var flatA = Synthetic(FrameKind.Flat, "flat-hoo-a.fits", "HOO", new DateTimeOffset(2026, 6, 7, 5, 0, 0, TimeSpan.Zero));
var flatB = Synthetic(FrameKind.Flat, "flat-hoo-b.fits", "HOO", new DateTimeOffset(2026, 6, 13, 5, 0, 0, TimeSpan.Zero));
epochFrames.AddRange([flatA, flatB]);
var epochAnalysis = ProjectAnalyzer.Analyze(epochFrames);
Assert(epochAnalysis.Lights.Where(item => item.Light.FileName.StartsWith("hoo-a-")).All(item => item.Flat.Selected?.Frame == flatA), "Flat Epoch HOO iniziale errata.");
Assert(epochAnalysis.Lights.Where(item => item.Light.FileName.StartsWith("hoo-b-")).All(item => item.Flat.Selected?.Frame == flatB), "Flat Epoch HOO successiva errata.");
var epochStatistics = ProjectStatisticsCalculator.Calculate(epochAnalysis);
Assert(epochStatistics.FilterCount == 1 && epochStatistics.ConfigurationSessionCount == 2 && epochStatistics.NightCount == 9, "Statistiche Flat Epoch errate.");
Assert(Math.Abs(epochStatistics.ExposureHours - 1.5) < 0.001, "Ore di integrazione Flat Epoch errate.");
var epochRecipe = WbppRecipeEngine.Recommend(epochAnalysis);
Assert(epochRecipe.Keywords.Any(keyword => keyword.Keyword == "FLATSET" && keyword.Pre && !keyword.Post), "WBPP deve separare le sessioni ottiche con FLATSET Pre.");
var manualLight = Synthetic(FrameKind.Light, "manual-light.fits", "HOO", new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero));
manualLight.FlatSetId.SetOverride("HOO-DOPO-CAMBIO");
flatB.FlatSetId.SetOverride("HOO-DOPO-CAMBIO");
var manualAnalysis = ProjectAnalyzer.Analyze([manualLight, flatA, flatB]);
Assert(manualAnalysis.Lights[0].Flat.Selected?.Frame == flatB, "Override manuale Flat Epoch non rispettato.");

var exportRoot = Path.Combine(Path.GetTempPath(), $"AstroForge-Test-{Guid.NewGuid():N}");
Directory.CreateDirectory(exportRoot);
try
{
    var sourceA = Path.Combine(exportRoot, "source-a.fit");
    var sourceB = Path.Combine(exportRoot, "source-b.xisf");
    await File.WriteAllTextAsync(sourceA, "astroforge-light");
    await File.WriteAllTextAsync(sourceB, "astroforge-master");
    var organizerSource = Path.Combine(exportRoot, "raw-master-dark.fits");
    WriteMinimalFits(organizerSource);
    var organizerFrame = new FrameMetadata { Path = organizerSource, Kind = FrameKind.Dark, IsMaster = true };
    var organizerOutput = Path.Combine(exportRoot, "organized-library");
    var organizerRequest = new MasterOrganizationRequest(organizerFrame, new("Synthetic Camera", 100, 51, -10, 600, "Default"));
    var duplicatePlan = await MasterLibraryOrganizer.PlanAsync([organizerRequest, organizerRequest], organizerOutput);
    Assert(duplicatePlan.All(item => item.Status == MasterOrganizationPlanStatus.DuplicateDestination), "Il preflight deve bloccare destinazioni duplicate prima della copia.");
    var organized = await MasterLibraryOrganizer.ExecuteAsync([organizerRequest], organizerOutput);
    var organizedRelativePath = Path.GetRelativePath(organizerOutput, organized.Single().DestinationPath);
    Assert(organizedRelativePath.Split(Path.DirectorySeparatorChar)[0] == "Camera-Synthetic Camera", "La camera deve essere sempre la radice della libreria Master organizzata.");
    AssertThrows(() => MasterLibraryOrganizer.RelativePath(organizerFrame, new("", 100, 51, -10, 600, "Default")), "Una libreria Master senza camera non deve essere organizzabile.");
    var organizedHeaders = await AstroForge.Core.Parsing.FitsHeaderReader.ReadAsync(organized.Single().DestinationPath);
    Assert(organized.Single().HeaderStamped && organizedHeaders["GAIN"]?.ToString() == "100" && organizedHeaders["SET-TEMP"]?.ToString() == "-10", "Metadati Master non impressi sulla copia FITS.");
    Assert(File.Exists(Path.Combine(organizerOutput, "astroforge-master-library.json")) && File.Exists(organizerSource), "Manifest organizzatore o Master originale assente.");
    var existingPlan = await MasterLibraryOrganizer.PlanAsync([organizerRequest], organizerOutput);
    Assert(existingPlan.Single().Status == MasterOrganizationPlanStatus.ExistingFile, "Il preflight deve segnalare una destinazione già esistente.");
    var rolledBack = await MasterLibraryOrganizer.RollbackAsync(organizerOutput);
    Assert(rolledBack == 1 && !File.Exists(organized.Single().DestinationPath) && !File.Exists(Path.Combine(organizerOutput, "astroforge-master-library.json")) && File.Exists(organizerSource), "Rollback del batch non sicuro o incompleto.");
    var organizedAgain = await MasterLibraryOrganizer.ExecuteAsync([organizerRequest], organizerOutput);
    await File.AppendAllTextAsync(organizedAgain.Single().DestinationPath, "tampered");
    var tamperedRollbackBlocked = false;
    try { await MasterLibraryOrganizer.RollbackAsync(organizerOutput); }
    catch (IOException) { tamperedRollbackBlocked = true; }
    Assert(tamperedRollbackBlocked && File.Exists(organizedAgain.Single().DestinationPath), "Il rollback deve bloccarsi senza eliminare copie modificate dopo il batch.");
    var cache = new MemoryHeaderCache();
    var sourceInfo = new FileInfo(sourceA);
    cache.Put(sourceA, sourceInfo.Length, sourceInfo.LastWriteTimeUtc.Ticks, new()
    {
        ["IMAGETYP"] = "Light", ["FILTER"] = "HOO", ["EXPTIME"] = 600, ["GAIN"] = 100,
        ["NAXIS1"] = 6248, ["NAXIS2"] = 4176, ["XBINNING"] = 1, ["YBINNING"] = 1
    });
    var cacheScanner = new ProjectScanner();
    var cachedFrames = await cacheScanner.ScanAsync([sourceA], SessionSettings.DefaultForLocalMachine(), cache: cache);
    Assert(cacheScanner.LastCacheHits == 1 && cachedFrames.Single().Kind == FrameKind.Light, "Cache header valida non utilizzata.");
    await File.AppendAllTextAsync(sourceA, "-changed");
    var invalidatedFrames = await cacheScanner.ScanAsync([sourceA], SessionSettings.DefaultForLocalMachine(), cache: cache);
    Assert(cacheScanner.LastCacheHits == 0 && invalidatedFrames.Single().Issues.Any(issue => issue.Code == "image.unreadable"), "Cache non invalidata dopo modifica del file.");
    var diagnosticLog = new StructuredEventLog(Path.Combine(exportRoot, "logs"), 4096, 2);
    for (var index = 0; index < 80; index++) diagnosticLog.Write("Error", "AF-TEST-001", $@"Errore controllato {index} in C:\secret\target-{index}.fits", new IOException("private detail"));
    Assert(diagnosticLog.Files.Count is >= 2 and <= 3, "Rotazione log strutturato non applicata.");
    using (var correlatedOperation = diagnosticLog.BeginOperation("Test correlazione", "AF-TEST-START", "Operazione test avviata", "test-operation-id"))
        correlatedOperation.Complete("AF-TEST-OK", "Operazione test completata");
    var correlatedEvents = diagnosticLog.ReadRecent().Where(item => item.OperationId == "test-operation-id").ToArray();
    Assert(correlatedEvents.Length == 2 && correlatedEvents.All(item => item.Operation == "Test correlazione"), "Eventi della stessa operazione non correlati correttamente.");
    var recoveryStore = new RecoveryJournalStore(Path.Combine(exportRoot, "recovery", "current.json"));
    var recoveryEntry = recoveryStore.Begin("Analisi progetto", new Dictionary<string, int> { ["sourceCount"] = 3 }, "recovery-operation-id");
    var recoveredEntry = recoveryStore.Read<Dictionary<string, int>>();
    Assert(recoveredEntry?.OperationId == recoveryEntry.OperationId && recoveredEntry.Snapshot["sourceCount"] == 3, "Recovery journal atomico non rileggibile.");
    Assert(!recoveryStore.Complete("wrong-operation-id") && recoveryStore.Read<Dictionary<string, int>>() is not null, "Un'operazione diversa non deve eliminare il recovery journal corrente.");
    Assert(recoveryStore.Complete(recoveryEntry.OperationId) && recoveryStore.Read<Dictionary<string, int>>() is null, "Recovery journal non rimosso dopo il completamento.");
    var supportZip = Path.Combine(exportRoot, "support.zip");
    var support = await SupportBundleBuilder.BuildAsync(new(supportZip, "test", new Dictionary<string, object?> { ["uiDensity"] = "Comoda" }, new Dictionary<string, object?> { ["frameCount"] = 2 }, [new("image.unreadable", "Error", 1)], diagnosticLog.Files));
    using (var supportArchive = ZipFile.OpenRead(support.Path))
    {
        Assert(supportArchive.Entries.All(entry => !new[] { ".fit", ".fits", ".fts", ".xisf" }.Contains(Path.GetExtension(entry.FullName), StringComparer.OrdinalIgnoreCase)), "Il support bundle non deve contenere immagini astronomiche.");
        var supportText = string.Join("\n", supportArchive.Entries.Select(entry => { using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd(); }));
        Assert(!supportText.Contains(@"C:\secret", StringComparison.OrdinalIgnoreCase) && !supportText.Contains("target-", StringComparison.OrdinalIgnoreCase), "Percorsi o nomi astronomici presenti nel support bundle.");
    }
    var fakeLight = new FrameMetadata { Path = sourceA, Kind = FrameKind.Light };
    var fakeMaster = new FrameMetadata { Path = sourceB, Kind = FrameKind.Dark, IsMaster = true };
    var emptyRecipe = new WbppRecipe([], ["test"]);
    var resumePlan = new ProjectPlan("Resume", exportRoot,
    [
        new(fakeLight, Path.Combine("Light", "source-a.fit"), "light"),
        new(fakeMaster, Path.Combine("Dark", "source-b.xisf"), "dark")
    ], emptyRecipe, statistics);
    var staging = Path.Combine(exportRoot, ".Resume.astroforge-staging", "Light");
    Directory.CreateDirectory(staging);
    File.Copy(sourceA, Path.Combine(staging, "source-a.fit"));
    var exported = await ProjectExporter.ExecuteAsync(resumePlan);
    Assert(File.Exists(Path.Combine(exported, "Light", "source-a.fit")), "Ripresa esportazione non riuscita.");
    Assert(File.Exists(Path.Combine(exported, "_AstroForge", "manifest.json")), "Manifest esportazione assente.");
    Assert(File.Exists(Path.Combine(exported, "_AstroForge", "project-statistics.json")), "Statistiche JSON esportazione assenti.");
    Assert(File.Exists(Path.Combine(exported, "_AstroForge", "project-statistics.csv")), "Statistiche CSV esportazione assenti.");
}
finally
{
    if (Directory.Exists(exportRoot)) Directory.Delete(exportRoot, true);
}
await RegressionQa.RunAsync();
Console.WriteLine($"PASS: {frames.Count} fixture autosufficienti, Flat Epoch multisessione, link manuale, WBPP ed export riprendibile verificati.");

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static FrameMetadata Synthetic(FrameKind kind, string name, string filter, DateTimeOffset captured)
{
    var frame = new FrameMetadata { Path = Path.Combine(Path.GetTempPath(), name), Kind = kind };
    frame.FilterName.SetOriginal(filter, MetadataSource.Header);
    frame.Camera.SetOriginal("Synthetic Camera", MetadataSource.Header);
    frame.Width.SetOriginal(6248, MetadataSource.Header);
    frame.Height.SetOriginal(4176, MetadataSource.Header);
    frame.XBin.SetOriginal(1, MetadataSource.Header);
    frame.YBin.SetOriginal(1, MetadataSource.Header);
    frame.Gain.SetOriginal(100, MetadataSource.Header);
    frame.ExposureSeconds.SetOriginal(kind == FrameKind.Light ? 600 : 4, MetadataSource.Header);
    frame.Offset.SetOriginal(51, MetadataSource.Header);
    frame.ReadoutMode.SetOriginal("Default", MetadataSource.Header);
    frame.BayerPattern.SetOriginal("RGGB", MetadataSource.Header);
    frame.CapturedAt.SetOriginal(captured, MetadataSource.Header);
    frame.SessionId.SetOriginal(captured.AddHours(-12).ToString("yyyy-MM-dd"), MetadataSource.Inferred);
    return frame;
}

static FrameMetadata SyntheticMaster(FrameKind kind, string path, double? temperature)
{
    var frame = new FrameMetadata { Path = path, Kind = kind, IsMaster = true };
    frame.Camera.SetOriginal("Synthetic Camera", MetadataSource.Header);
    frame.Width.SetOriginal(6248, MetadataSource.Header); frame.Height.SetOriginal(4176, MetadataSource.Header);
    frame.XBin.SetOriginal(1, MetadataSource.Header); frame.YBin.SetOriginal(1, MetadataSource.Header);
    frame.Gain.SetOriginal(100, MetadataSource.LibraryPath);
    frame.ExposureSeconds.SetOriginal(kind == FrameKind.Dark ? 600 : 0, MetadataSource.Header);
    frame.SetTemperatureC.SetOriginal(temperature, MetadataSource.LibraryPath);
    frame.ReadoutMode.SetOriginal("Default", MetadataSource.Header); frame.BayerPattern.SetOriginal("RGGB", MetadataSource.Header);
    return frame;
}

static FrameMetadata CalibrationCopy(FrameMetadata source, string path)
{
    var frame = new FrameMetadata { Path = path, Kind = source.Kind, IsMaster = source.IsMaster };
    frame.Camera.SetOriginal(source.Camera.Value, MetadataSource.Header);
    frame.Width.SetOriginal(source.Width.Value, MetadataSource.Header);
    frame.Height.SetOriginal(source.Height.Value, MetadataSource.Header);
    frame.XBin.SetOriginal(source.XBin.Value, MetadataSource.Header);
    frame.YBin.SetOriginal(source.YBin.Value, MetadataSource.Header);
    frame.Gain.SetOriginal(source.Gain.Value, MetadataSource.ProjectDefault);
    frame.Offset.SetOriginal(source.Offset.Value, MetadataSource.ProjectDefault);
    frame.SetTemperatureC.SetOriginal(source.SetTemperatureC.Value, MetadataSource.ProjectDefault);
    frame.ReadoutMode.SetOriginal(source.ReadoutMode.Value, MetadataSource.Header);
    frame.BayerPattern.SetOriginal(source.BayerPattern.Value, MetadataSource.Header);
    return frame;
}

static void WriteMinimalFits(string path)
{
    static string Card(string key, string value = "") => (value.Length == 0 ? key : $"{key.PadRight(8)}= {value}").PadRight(80)[..80];
    var text = Card("SIMPLE", "T") + Card("BITPIX", "16") + Card("NAXIS", "0") + Card("END");
    File.WriteAllBytes(path, System.Text.Encoding.ASCII.GetBytes(text.PadRight(2880)));
}

static void AssertThrows(Action action, string message)
{
    try { action(); }
    catch (InvalidOperationException) { return; }
    throw new InvalidOperationException(message);
}

sealed class MemoryHeaderCache : IHeaderCache
{
    private readonly Dictionary<string, (long Length, long Ticks, Dictionary<string, object?> Headers)> _entries = new(StringComparer.OrdinalIgnoreCase);
    public bool TryGet(string path, long length, long lastWriteUtcTicks, out Dictionary<string, object?> headers)
    {
        if (_entries.TryGetValue(path, out var entry) && entry.Length == length && entry.Ticks == lastWriteUtcTicks) { headers = new(entry.Headers); return true; }
        headers = []; return false;
    }
    public void Put(string path, long length, long lastWriteUtcTicks, Dictionary<string, object?> headers) => _entries[path] = (length, lastWriteUtcTicks, new(headers));
    public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
