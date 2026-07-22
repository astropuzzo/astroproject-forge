using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AstroForge.Core.Export;
using AstroForge.Core.Models;
using AstroForge.Core.Parsing;
using AstroForge.Core.Persistence;
using AstroForge.Core.Releases;
using AstroForge.Core.Sessions;
using AstroForge.Core.Wbpp;
using AstroForge.Core.Quality;

internal static class RegressionQa
{
    public static async Task RunAsync()
    {
        VendorHeaderMatrix();
        AstronomicalNightProperties();
        SettingsMigrations();
        LargeSyntheticDataset();
        await ParserFuzzAndLongPathsAsync();
        await FitsQualityMetricsAsync();
        await UpdateIntegrityAsync();
        await ExportPreflightSafetyAsync();
        await ExportExecutionControlAsync();
        await InterruptedExportAsync();
        Console.WriteLine("PASS QA: 5 vendor, 1.000 confini notte, 10.000 frame, fuzz FITS/XISF, metriche Quality FITS, migrazione, update SHA-256/Authenticode, controlli export, pausa e ripresa verificata.");
    }

    private static void VendorHeaderMatrix()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "vendor-header-matrix.json");
        var cases = JsonSerializer.Deserialize<List<VendorFixture>>(File.ReadAllText(fixturePath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        Assert(cases.Count == 5, "Matrice fixture multiproduttore incompleta.");
        foreach (var fixture in cases)
        {
            var name = fixture.Vendor;
            var headers = fixture.Headers.ToDictionary(item => item.Key, item => JsonValue(item.Value), StringComparer.OrdinalIgnoreCase);
            headers["EXPTIME"] = "300"; headers["NAXIS1"] = 3000; headers["NAXIS2"] = 2000;
            headers["XBINNING"] = 1; headers["YBINNING"] = 1; headers["DATE-OBS"] = "2026-07-16T23:10:00Z";
            var frame = FrameClassifier.Classify(Path.Combine(Path.GetTempPath(), $"{name}.fits"), headers, new(TimeZoneInfo.Utc, new TimeOnly(12, 0)));
            Assert(frame.Kind == FrameKind.Light, $"{name}: tipo Light non riconosciuto.");
            Assert(frame.Camera.Value is { Length: > 0 } && frame.Gain.Value is not null && frame.Offset.Value is not null && frame.SetTemperatureC.Value is not null && frame.FilterName.Value is { Length: > 0 }, $"{name}: metadati tecnici incompleti.");
        }
    }

    private static void AstronomicalNightProperties()
    {
        var settings = new SessionSettings(TimeZoneInfo.Utc, new TimeOnly(12, 0));
        var random = new Random(20260717);
        for (var index = 0; index < 1000; index++)
        {
            var day = new DateTimeOffset(2025 + random.Next(0, 3), random.Next(1, 13), random.Next(1, 20), random.Next(0, 24), random.Next(0, 60), random.Next(0, 60), TimeSpan.Zero);
            var expected = (day.TimeOfDay < TimeSpan.FromHours(12) ? day.Date.AddDays(-1) : day.Date).ToString("yyyy-MM-dd");
            Assert(AstronomicalSessionResolver.Resolve(day, settings) == expected, $"Confine notte errato per {day:O}.");
        }
        Assert(AstronomicalSessionResolver.Resolve(new DateTimeOffset(2026, 7, 17, 11, 59, 59, TimeSpan.Zero), settings) == "2026-07-16", "Il secondo prima del confine deve restare nella notte precedente.");
        Assert(AstronomicalSessionResolver.Resolve(new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero), settings) == "2026-07-17", "Il confine deve aprire una nuova sessione.");
    }

    private static void SettingsMigrations()
    {
        var migrated = SettingsMigration.Migrate("{\"UiDensity\":\"Compatta\",\"ReducedMotion\":true}");
        using var document = JsonDocument.Parse(migrated);
        var root = document.RootElement;
        Assert(root.GetProperty("SchemaVersion").GetInt32() == 2, "Schema impostazioni non migrato.");
        Assert(!root.GetProperty("CheckForUpdates").GetBoolean() && root.GetProperty("UpdateChannel").GetString() == "Beta", "Default update non privacy-safe nella migrazione.");
        Assert(root.GetProperty("UiDensity").GetString() == "Compatta" && root.GetProperty("ReducedMotion").GetBoolean(), "La migrazione ha perso preferenze esistenti.");
        AssertThrows<InvalidDataException>(() => SettingsMigration.Migrate("{\"SchemaVersion\":99}"), "Uno schema futuro deve essere rifiutato.");
    }

    private static void LargeSyntheticDataset()
    {
        var settings = new SessionSettings(TimeZoneInfo.Utc, new TimeOnly(12, 0));
        var watch = Stopwatch.StartNew();
        var kinds = 0;
        for (var index = 0; index < 10_000; index++)
        {
            var headers = new Dictionary<string, object?>
            {
                ["IMAGETYP"]="Light", ["INSTRUME"]="Synthetic QA Camera", ["GAIN"]=100, ["OFFSET"]=51,
                ["SET-TEMP"]=-10, ["FILTER"]=index % 2 == 0 ? "HOO" : "SIOIII", ["EXPTIME"]=600,
                ["NAXIS1"]=6248, ["NAXIS2"]=4176, ["XBINNING"]=1, ["YBINNING"]=1,
                ["DATE-OBS"]=$"2026-07-{1 + index % 16:00}T{20 + index % 4:00}:00:00Z"
            };
            if (FrameClassifier.Classify(Path.Combine(Path.GetTempPath(), $"qa-{index:00000}.fits"), headers, settings).Kind == FrameKind.Light) kinds++;
        }
        watch.Stop();
        Assert(kinds == 10_000, "Il dataset grande ha prodotto classificazioni instabili.");
        Assert(watch.Elapsed < TimeSpan.FromSeconds(30), $"Regressione grave parser: {watch.Elapsed} per 10.000 frame.");
    }

    private static async Task ParserFuzzAndLongPathsAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AstroForge-QA-Parser-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var unicode = Path.Combine(root, string.Concat(Enumerable.Repeat("sessione_çielo_星_", 8)));
            Directory.CreateDirectory(unicode);
            var validFits = Path.Combine(unicode, "luce_àèìòù.fits");
            WriteFits(validFits, ("IMAGETYP", "'Light'"), ("GAIN", "100"), ("SET-TEMP", "-10"));
            var headers = await FitsHeaderReader.ReadAsync(validFits);
            Assert(headers["GAIN"]?.ToString() == "100", "FITS su percorso Unicode lungo non letto.");

            var validXisf = Path.Combine(unicode, "master_星.xisf");
            WriteXisf(validXisf, "<xisf><Image geometry=\"3000:2000:1\"><FITSKeyword name=\"IMAGETYP\" value=\"'Master Dark'\"/></Image></xisf>");
            Assert((await XisfHeaderReader.ReadAsync(validXisf))["NAXIS1"]?.ToString() == "3000", "XISF su percorso Unicode lungo non letto.");

            var random = new Random(64127);
            for (var index = 0; index < 64; index++)
            {
                var bytes = new byte[random.Next(0, 8192)]; random.NextBytes(bytes);
                var fits = Path.Combine(root, $"fuzz-{index:00}.fits"); var xisf = Path.Combine(root, $"fuzz-{index:00}.xisf");
                await File.WriteAllBytesAsync(fits, bytes); await File.WriteAllBytesAsync(xisf, bytes);
                await MustCompleteAsync(() => FitsHeaderReader.ReadAsync(fits), $"FITS fuzz {index}");
                await MustCompleteAsync(() => XisfHeaderReader.ReadAsync(xisf), $"XISF fuzz {index}");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task FitsQualityMetricsAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AstroForge-QA-Quality-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        try
        {
            var sharp = Path.Combine(root, "sharp.fits");
            var soft = Path.Combine(root, "soft.fits");
            WriteStarFieldFits(sharp, 1.5);
            WriteStarFieldFits(soft, 2.8);
            var sharpMetrics = await FitsQualityAnalyzer.AnalyzeAsync(sharp);
            var softMetrics = await FitsQualityAnalyzer.AnalyzeAsync(soft);
            Assert(sharpMetrics.StarCount >= 8 && sharpMetrics.FwhmPixels > 1 && sharpMetrics.PreviewPixels.Length > 0, "Il Quality Lab non ha misurato il campo stellare FITS.");
            Assert(softMetrics.FwhmPixels > sharpMetrics.FwhmPixels, "Il Quality Lab non distingue un frame sfocato da uno più nitido.");
            Assert(sharpMetrics.Snr > 1 && sharpMetrics.Noise > 0, "SNR o rumore del Quality Lab non validi.");
            var monoPreview = await FitsQualityAnalyzer.RenderPreviewAsync(sharp, null, false, 4);
            var colorPreview = await FitsQualityAnalyzer.RenderPreviewAsync(sharp, "RGGB", true, 8);
            Assert(!monoPreview.IsColor && monoPreview.Pixels.Length == monoPreview.Width * monoPreview.Height, "Preview mono del Quality Lab non valida.");
            Assert(colorPreview.IsColor && colorPreview.Pixels.Length == colorPreview.Width * colorPreview.Height * 3, "Debayer temporaneo del Quality Lab non valido.");
            Assert(!monoPreview.Pixels.SequenceEqual(colorPreview.Pixels.Take(monoPreview.Pixels.Length)), "Lo stretch/debayer non ha modificato la preview.");
            var channelMedians = Enumerable.Range(0, 3).Select(channel =>
            {
                var values = colorPreview.Pixels.Where((_, index) => index % 3 == channel).OrderBy(value => value).ToArray();
                return (int)values[values.Length / 2];
            }).ToArray();
            Assert(channelMedians.Max() - channelMedians.Min() <= 8, "Lo stretch RGB non ha neutralizzato il fondo tra i canali Bayer.");
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task UpdateIntegrityAsync()
    {
        var payload = Encoding.UTF8.GetBytes("verified-installer-payload");
        var sha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var manifest = new ReleaseManifest(1, "AstroProject Forge", "Beta", "0.9.1-beta.1", DateTimeOffset.UtcNow,
            new("https://updates.example.test/AstroProjectForge.exe", sha, payload.Length, "AstroProjectForge.exe"), "https://updates.example.test/notes", Signed: true);
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath.EndsWith("beta.json")
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
        var service = new UpdateService(new HttpClient(handler), _ => true);
        var decision = await service.CheckAsync(new Uri("https://updates.example.test/beta.json"), "0.9.0-beta.1", ReleaseChannel.Beta);
        Assert(decision.IsAvailable && UpdateService.CompareVersions("1.0.0", "1.0.0-rc.1") > 0, "Ordinamento SemVer/update errato.");
        var root = Path.Combine(Path.GetTempPath(), $"AstroForge-QA-Update-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        try
        {
            var destination = Path.Combine(root, "installer.exe");
            await service.DownloadVerifiedAsync(manifest.Installer, destination);
            Assert(File.ReadAllBytes(destination).SequenceEqual(payload), "Download update verificato non identico.");
            var bad = manifest.Installer with { Sha256 = new string('0', 64) };
            await AssertThrowsAsync<CryptographicException>(() => service.DownloadVerifiedAsync(bad, Path.Combine(root, "bad.exe")), "Un update alterato deve essere rifiutato.");
            Assert(!File.Exists(Path.Combine(root, "bad.exe.partial")), "Il payload update parziale deve essere eliminato dopo il rifiuto.");
            var unsignedService = new UpdateService(new HttpClient(handler), _ => false);
            await AssertThrowsAsync<CryptographicException>(() => unsignedService.DownloadVerifiedAsync(manifest.Installer, Path.Combine(root, "unsigned.exe")), "Un update senza Authenticode valido deve essere rifiutato.");
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task InterruptedExportAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AstroForge-QA-Resume-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        try
        {
            var first = Path.Combine(root, "first.fit"); var second = Path.Combine(root, "second.fit");
            await File.WriteAllBytesAsync(first, Enumerable.Repeat((byte)0x41, 2 * 1024 * 1024).ToArray());
            await File.WriteAllBytesAsync(second, Enumerable.Repeat((byte)0x42, 2 * 1024 * 1024).ToArray());
            var plan = new ProjectPlan("Interrupted", root,
            [
                new(new FrameMetadata { Path = first, Kind = FrameKind.Light }, Path.Combine("Light", "first.fit"), "light"),
                new(new FrameMetadata { Path = second, Kind = FrameKind.Dark, IsMaster = true }, Path.Combine("Dark", "second.fit"), "dark")
            ], new WbppRecipe([], ["qa"]));
            using var cancellation = new CancellationTokenSource();
            var progress = new CallbackProgress<ExportProgress>(value => { if (value.Completed == 1) cancellation.Cancel(); });
            await AssertThrowsAsync<OperationCanceledException>(() => ProjectExporter.ExecuteAsync(plan, progress, cancellation.Token), "L'interruzione export non è stata osservata.");
            var stagingFile = Path.Combine(root, ".Interrupted.astroforge-staging", "Light", "first.fit");
            Assert(File.Exists(stagingFile) && !File.Exists(plan.ProjectRoot), "Lo staging verificato deve restare disponibile dopo l'interruzione.");
            var resumeReport = await ProjectExportPreflight.AnalyzeAsync(plan, new(0, 0, 100));
            Assert(resumeReport.IsReady && resumeReport.ResumeFileCount == 1 && resumeReport.ResumeBytes == new FileInfo(first).Length,
                "Il preflight non ha riconosciuto la copia verificata disponibile per la ripresa.");
            var result = await ProjectExporter.ExecuteAsync(plan);
            Assert(File.Exists(Path.Combine(result, "Light", "first.fit")) && File.Exists(Path.Combine(result, "Dark", "second.fit")), "La ripresa export non ha completato tutti i file.");
            var controlRoot = Path.Combine(result, "_AstroForge");
            Assert(File.Exists(Path.Combine(controlRoot, "export-preflight.json")), "Il report preflight non è stato incluso nel progetto finale.");
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(controlRoot, "manifest.json")));
            Assert(manifest.RootElement.GetProperty("schema").GetInt32() == 2 && manifest.RootElement.TryGetProperty("preflight", out _),
                "Il manifest export non espone lo schema antifragile e il riepilogo preflight.");
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task ExportPreflightSafetyAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AstroForge-QA-Preflight-{Guid.NewGuid():N}");
        var sourceRoot = Path.Combine(root, "source");
        var destinationRoot = Path.Combine(root, "destination");
        Directory.CreateDirectory(sourceRoot);
        try
        {
            var source = Path.Combine(sourceRoot, "luce_星.fit");
            await File.WriteAllBytesAsync(source, Enumerable.Repeat((byte)0x5A, 64 * 1024).ToArray());
            ProjectPlan Plan(string name, string destination, string relative = "Light/luce_星.fit", string? input = null) =>
                new(name, destination,
                [new(new FrameMetadata { Path = input ?? source, Kind = FrameKind.Light }, relative, "light")],
                new WbppRecipe([], ["qa"]));
            var safeOptions = new ExportPreflightOptions(0, 0, 100, [sourceRoot]);

            var cleanPlan = Plan("Clean", destinationRoot);
            var clean = await ProjectExportPreflight.AnalyzeAsync(cleanPlan, safeOptions);
            Assert(clean.IsReady && clean.TotalFiles == 1 && clean.BytesToCopy == new FileInfo(source).Length,
                "Il preflight ha bloccato un piano valido.");
            Assert(!Directory.Exists(clean.ProjectRoot) && !Directory.Exists(clean.StagingRoot),
                "Il dry-run ha scritto nella destinazione.");

            var overlap = await ProjectExportPreflight.AnalyzeAsync(Plan("Nested", Path.Combine(sourceRoot, "output")), safeOptions);
            Assert(overlap.Findings.Any(item => item.Code == "destination.overlap" && item.Severity == ExportPreflightSeverity.Error),
                "La sovrapposizione tra sorgente e destinazione non è stata bloccata.");

            var missing = await ProjectExportPreflight.AnalyzeAsync(Plan("Missing", destinationRoot, input: Path.Combine(sourceRoot, "missing.fit")), safeOptions);
            Assert(missing.Findings.Any(item => item.Code == "source.missing"), "Una sorgente mancante non ha bloccato l'export.");

            var traversal = await ProjectExportPreflight.AnalyzeAsync(Plan("Traversal", destinationRoot, "../escape.fit"), safeOptions);
            Assert(traversal.Findings.Any(item => item.Code == "path.traversal"), "Un percorso in uscita dallo staging non è stato bloccato.");

            Directory.CreateDirectory(cleanPlan.ProjectRoot);
            var existing = await ProjectExportPreflight.AnalyzeAsync(cleanPlan, safeOptions);
            Assert(existing.Findings.Any(item => item.Code == "destination.exists"), "Un progetto esistente non è stato protetto dalla sovrascrittura.");
            Directory.Delete(cleanPlan.ProjectRoot, true);

            var stagingFile = Path.Combine(clean.StagingRoot, "Light", "luce_星.fit");
            Directory.CreateDirectory(Path.GetDirectoryName(stagingFile)!);
            await File.WriteAllBytesAsync(stagingFile, Enumerable.Repeat((byte)0x00, 64 * 1024).ToArray());
            var tampered = await ProjectExportPreflight.AnalyzeAsync(cleanPlan, safeOptions);
            Assert(tampered.Findings.Any(item => item.Code == "resume.hash_mismatch"), "Uno staging alterato non è stato rifiutato.");
            Directory.Delete(clean.StagingRoot, true);

            var free = new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace;
            var insufficient = await ProjectExportPreflight.AnalyzeAsync(cleanPlan, safeOptions with { MinimumReserveBytes = free });
            Assert(insufficient.Findings.Any(item => item.Code == "space.insufficient"), "Lo spazio insufficiente non ha bloccato l'export.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static async Task ExportExecutionControlAsync()
    {
        var control = new ExportExecutionControl();
        control.Pause();
        var wait = control.WaitIfPausedAsync(CancellationToken.None).AsTask();
        await Task.Delay(25);
        Assert(!wait.IsCompleted && control.IsPaused, "La pausa export non ha sospeso il lavoro.");
        control.Resume();
        await wait.WaitAsync(TimeSpan.FromSeconds(1));
        Assert(!control.IsPaused, "La ripresa export non ha riaperto il gate.");

        control.Pause();
        using var cancellation = new CancellationTokenSource();
        var cancelledWait = control.WaitIfPausedAsync(cancellation.Token).AsTask();
        cancellation.Cancel();
        await AssertThrowsAsync<OperationCanceledException>(() => cancelledWait, "Annulla non interrompe un export in pausa.");
    }

    private static async Task MustCompleteAsync(Func<Task<Dictionary<string, object?>>> action, string label)
    {
        try { _ = await action().WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or IOException or System.Xml.XmlException or DecoderFallbackException) { }
        catch (TimeoutException) { throw new InvalidOperationException($"{label}: parser bloccato."); }
    }

    private static void WriteFits(string path, params (string Key, string Value)[] cards)
    {
        static string Card(string key, string value = "") => (value.Length == 0 ? key : $"{key.PadRight(8)}= {value}").PadRight(80)[..80];
        var text = Card("SIMPLE", "T") + Card("BITPIX", "16") + Card("NAXIS", "0") + string.Concat(cards.Select(item => Card(item.Key, item.Value))) + Card("END");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes(text.PadRight((int)Math.Ceiling(text.Length / 2880d) * 2880)));
    }

    private static void WriteXisf(string path, string xml)
    {
        var body = Encoding.UTF8.GetBytes(xml); var bytes = new byte[16 + body.Length];
        "XISF0100"u8.CopyTo(bytes); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), (uint)body.Length); body.CopyTo(bytes, 16);
        File.WriteAllBytes(path, bytes);
    }

    private static void WriteStarFieldFits(string path, double sigma)
    {
        const int width = 160, height = 120;
        static string Card(string key, string value = "") => (value.Length == 0 ? key : $"{key.PadRight(8)}= {value}").PadRight(80)[..80];
        var header = Card("SIMPLE", "T") + Card("BITPIX", "16") + Card("NAXIS", "2") + Card("NAXIS1", width.ToString()) + Card("NAXIS2", height.ToString()) + Card("BZERO", "32768") + Card("BSCALE", "1") + Card("END");
        var headerBytes = Encoding.ASCII.GetBytes(header.PadRight((int)Math.Ceiling(header.Length / 2880d) * 2880));
        var physical = Enumerable.Repeat(1000d, width * height).ToArray();
        var random = new Random(4421);
        for (var index = 0; index < physical.Length; index++) physical[index] += random.NextDouble() * 18 - 9;
        for (var star = 0; star < 24; star++)
        {
            var cx = 10 + (star * 31) % (width - 20); var cy = 10 + (star * 47) % (height - 20); var amplitude = 12000 - star * 180;
            for (var y = cy - 7; y <= cy + 7; y++) for (var x = cx - 7; x <= cx + 7; x++)
                physical[y * width + x] += amplitude * Math.Exp(-((x - cx) * (x - cx) + (y - cy) * (y - cy)) / (2 * sigma * sigma));
        }
        var data = new byte[width * height * 2];
        for (var index = 0; index < physical.Length; index++) BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(index * 2, 2), (short)Math.Clamp((int)Math.Round(physical[index] - 32768), short.MinValue, short.MaxValue));
        using var output = File.Create(path); output.Write(headerBytes); output.Write(data); output.Write(new byte[(2880 - data.Length % 2880) % 2880]);
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void AssertThrows<T>(Action action, string message) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException(message); }
    private static async Task AssertThrowsAsync<T>(Func<Task> action, string message) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException(message); }

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T> { public void Report(T value) => callback(value); }
    private sealed record VendorFixture(string Vendor, Dictionary<string, JsonElement> Headers);
    private static object? JsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
