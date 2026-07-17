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

internal static class RegressionQa
{
    public static async Task RunAsync()
    {
        VendorHeaderMatrix();
        AstronomicalNightProperties();
        SettingsMigrations();
        LargeSyntheticDataset();
        await ParserFuzzAndLongPathsAsync();
        await UpdateIntegrityAsync();
        await InterruptedExportAsync();
        Console.WriteLine("PASS QA: 5 vendor, 1.000 confini notte, 10.000 frame, fuzz FITS/XISF, migrazione, update SHA-256/Authenticode ed export interrotto/ripreso.");
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
            var result = await ProjectExporter.ExecuteAsync(plan);
            Assert(File.Exists(Path.Combine(result, "Light", "first.fit")) && File.Exists(Path.Combine(result, "Dark", "second.fit")), "La ripresa export non ha completato tutti i file.");
        }
        finally { Directory.Delete(root, true); }
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
