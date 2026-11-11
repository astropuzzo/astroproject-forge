using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AstroForge.Core.Analysis;
using AstroForge.Core.Models;
using AstroForge.Core.IO;
using AstroForge.Core.Wbpp;

namespace AstroForge.Core.Export;

/// <param name="Keywords">The WBPP grouping values decided with the plan. They travel with the file because an update leaves files where an earlier export put them, so the folder names can be out of date.</param>
public sealed record PlannedFile(FrameMetadata Frame, string RelativePath, string Role, string? GroupId = null, IReadOnlyDictionary<string, string>? Keywords = null);
public sealed record ProjectPlan(string ProjectName, string DestinationRoot, IReadOnlyList<PlannedFile> Files, WbppRecipe Recipe, ProjectStatistics? Statistics = null, string? PixInsightOutputFolderName = null)
{
    public long RequiredBytes => Files.Sum(file => new FileInfo(file.Frame.Path).Length);
    public string ProjectRoot => Path.Combine(DestinationRoot, ProjectName);
}
public sealed record ExportProgress(int Completed, int Total, string CurrentFile, long BytesCopied, long TotalBytes, double MiBPerSecond = 0, TimeSpan? EstimatedRemaining = null, bool Resumed = false);

public sealed class ExportExecutionControl
{
    private readonly object _sync = new();
    private TaskCompletionSource? _resume;
    public bool IsPaused { get { lock (_sync) return _resume is not null; } }
    public void Pause() { lock (_sync) _resume ??= new(TaskCreationOptions.RunContinuationsAsynchronously); }
    public void Resume() { TaskCompletionSource? gate; lock (_sync) { gate = _resume; _resume = null; } gate?.TrySetResult(); }
    public async ValueTask WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        Task? wait;
        lock (_sync) wait = _resume?.Task;
        if (wait is not null) await wait.WaitAsync(cancellationToken);
    }
}

public static class ProjectExporter
{
    public static ProjectPlan BuildPlan(string projectName, string destinationRoot, ProjectAnalysis analysis, IReadOnlySet<string>? excludedQualityPaths = null, string? pixInsightOutputFolderName = null)
    {
        // Lights set aside in the Quality Lab are filed apart: they need no calibration, are not integration time
        // and must not hold the export back.
        var kept = analysis.Without(excludedQualityPaths);
        if (!kept.Ready) throw new InvalidOperationException("Il progetto contiene calibrazioni irrisolte.");
        var recipe = WbppRecipeEngine.Recommend(kept);
        var files = new List<PlannedFile>();
        var included = new HashSet<string>(PathIdentity.Comparer);
        foreach (var item in analysis.Lights.Where(item => excludedQualityPaths?.Contains(item.Light.Path) == true))
        {
            var excludedFilter = Token(item.Light.FilterName.Value ?? "UNKNOWN");
            var excludedNight = Token(item.Light.SessionId.Value ?? "UNKNOWN");
            Add(files, included, new(item.Light, Combine(["Excluded", "Quality", $"FILTER_{excludedFilter}", $"NIGHT_{excludedNight}", item.Light.FileName]), "excluded-light", "quality"));
        }
        foreach (var item in kept.Lights)
        {
            if (item.FlatGroup is null || item.Dark.Selected is null || item.Bias.Selected is null)
                throw new InvalidOperationException($"Calibrazioni incomplete per {item.Light.FileName}.");
            var light = item.Light;
            var filter = Token(light.FilterName.Value ?? "UNKNOWN");
            var session = Token(light.SessionId.Value ?? "UNKNOWN");
            var flatSet = item.FlatGroup.Id;
            var darkSet = DarkSet(item.Dark.Selected.Frame);
            // With Dark Flats standing in for the Bias there is no Bias to name or export.
            var biasSet = item.Bias.RequestedKind == FrameKind.Bias ? BiasSet(item.Bias.Selected.Frame) : null;
            var parts = new List<string> { "Light", $"FILTER_{filter}" };
            if (recipe.Contains("FLATSET")) parts.Add($"FLATSET_{flatSet}");
            if (recipe.Contains("DARKSET")) parts.Add($"DARKSET_{darkSet}");
            if (recipe.Contains("BIASSET") && biasSet is not null) parts.Add($"BIASSET_{biasSet}");
            var target = WbppValue(light.ObjectName.Value ?? "UNKNOWN");
            if (recipe.Contains("TARGET")) parts.Add($"TARGET_{target}");
            parts.Add($"NIGHT_{session}");
            parts.Add(light.FileName);
            var lightKeywords = new Dictionary<string, string> { ["FLATSET"] = flatSet, ["DARKSET"] = darkSet, ["TARGET"] = target };
            if (biasSet is not null) lightKeywords["BIASSET"] = biasSet;
            Add(files, included, new(light, Combine(parts), "light", flatSet, lightKeywords));

            foreach (var flat in item.FlatGroup.Frames)
            {
                var flatParts = new List<string> { "Flat", $"FILTER_{filter}" };
                if (recipe.Contains("FLATSET")) flatParts.Add($"FLATSET_{flatSet}");
                if (recipe.Contains("BIASSET") && biasSet is not null) flatParts.Add($"BIASSET_{biasSet}");
                flatParts.Add(flat.FileName);
                var flatKeywords = new Dictionary<string, string> { ["FLATSET"] = flatSet };
                if (biasSet is not null) flatKeywords["BIASSET"] = biasSet;
                Add(files, included, new(flat, Combine(flatParts), "flat", flatSet, flatKeywords));
            }
            AddCalibration(files, included, item.Dark.Selected.Frame, item.DarkSet, "Dark", "DARKSET", darkSet, "dark");
            if (biasSet is not null) AddCalibration(files, included, item.Bias.Selected.Frame, item.BiasSet, "Bias", "BIASSET", biasSet, "bias");
            // WBPP takes Dark Flats as Darks and pairs them with the Flats by exposure.
            if (item.DarkFlat?.Selected is { } darkFlat)
                AddCalibration(files, included, darkFlat.Frame, item.DarkFlatSet, "Dark", "DARKFLAT", DarkFlatSet(darkFlat.Frame), "dark");
        }
        ResolveCollisions(files);
        EnsureNoCollisions(files);
        return new(SafeName(projectName), Path.GetFullPath(destinationRoot), files, recipe, ProjectStatisticsCalculator.Calculate(kept),
            string.IsNullOrWhiteSpace(pixInsightOutputFolderName) ? null : SafeName(pixInsightOutputFolderName));
    }

    /// <summary>The folder (and manifest) name a project name becomes: spaces and characters a file system refuses turn into underscores.</summary>
    public static string FolderName(string projectName) => SafeName(projectName);

    private static void AddCalibration(List<PlannedFile> files, HashSet<string> included, FrameMetadata selected, CalibrationGroup? set, string folder, string keyword, string setName, string role)
    {
        var keywords = new Dictionary<string, string> { [keyword] = setName };
        // A set of raw subs goes out whole, under its own names, for WBPP to integrate. A Master keeps the Master layout.
        if (set is { Frames.Count: > 1 })
        {
            foreach (var frame in set.Frames)
                Add(files, included, new(frame, Combine([folder, $"{keyword}_{setName}", frame.FileName]), role, setName, keywords));
            return;
        }
        Add(files, included, new(selected, Combine([folder, "Masters", $"{keyword}_{setName}", MasterName(selected)]), role, setName, keywords));
    }

    public static async Task<string> ExecuteAsync(ProjectPlan plan, IProgress<ExportProgress>? progress = null, CancellationToken cancellationToken = default,
        ExportExecutionControl? control = null, ExportPreflightOptions? preflightOptions = null)
    {
        var preflight = await ProjectExportPreflight.AnalyzeAsync(plan, preflightOptions, cancellationToken);
        if (!preflight.IsReady) throw new ExportPreflightException(preflight);
        var projectRoot = plan.ProjectRoot;
        var staging = Path.Combine(plan.DestinationRoot, $".{plan.ProjectName}.astroforge-staging");
        var workingRoot = preflight.IsIncremental ? projectRoot : staging;
        if (!preflight.IsIncremental) PruneStaging(staging, plan);
        Directory.CreateDirectory(workingRoot);
        var totalBytes = preflight.TotalBytes;
        long copiedBytes = 0;
        long transferredThisRun = 0;
        var watch = Stopwatch.StartNew();
        var records = preflight.IsIncremental ? ExistingManifestRecords(projectRoot) : new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var addedFiles = new List<PlannedFile>();
        var reuseMap = (preflight.ReuseMatches ?? []).ToDictionary(item => item.PlannedRelativePath, item => item.ExistingRelativePath, StringComparer.OrdinalIgnoreCase);
        try
        {
            for (var index = 0; index < plan.Files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (control is not null) await control.WaitIfPausedAsync(cancellationToken);
                var item = plan.Files[index];
                var effectiveRelativePath = reuseMap.TryGetValue(item.RelativePath, out var existingRelativePath) ? existingRelativePath : item.RelativePath;
                var destination = Path.Combine(workingRoot, effectiveRelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var partial = destination + ".partial";
                byte[] hash;
                var resumed = false;
                if (File.Exists(destination))
                {
                    if (records.TryGetValue(effectiveRelativePath.Replace('\\', '/'), out var record)
                        && record.TryGetProperty("sha256", out var savedHash)
                        && savedHash.GetString() is { Length: 64 } hexadecimal)
                        hash = Convert.FromHexString(hexadecimal);
                    else
                        hash = await HashAsync(destination, cancellationToken, control);
                    resumed = true;
                }
                else
                {
                    if (File.Exists(partial)) File.Delete(partial);
                    hash = await CopyWithHashAsync(item.Frame.Path, partial, control, cancellationToken);
                    File.Move(partial, destination);
                    File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(item.Frame.Path));
                    transferredThisRun += new FileInfo(destination).Length;
                    addedFiles.Add(item);
                }
                copiedBytes += new FileInfo(destination).Length;
                records[effectiveRelativePath.Replace('\\', '/')] = JsonSerializer.SerializeToElement(new
                {
                    source = Path.GetFullPath(item.Frame.Path),
                    destination = effectiveRelativePath.Replace('\\', '/'),
                    role = item.Role,
                    group_id = item.GroupId,
                    bytes = new FileInfo(destination).Length,
                    source_last_write_utc_ticks = File.GetLastWriteTimeUtc(item.Frame.Path).Ticks,
                    copy_last_write_utc_ticks = File.GetLastWriteTimeUtc(destination).Ticks,
                    sha256 = Convert.ToHexString(hash).ToLowerInvariant(),
                    metadata = Snapshot(item.Frame)
                });
                var speed = watch.Elapsed.TotalSeconds <= 0 ? 0 : transferredThisRun / 1024d / 1024d / watch.Elapsed.TotalSeconds;
                TimeSpan? remaining = speed <= 0 ? null : TimeSpan.FromSeconds(Math.Max(0, totalBytes - copiedBytes) / 1024d / 1024d / speed);
                progress?.Report(new(index + 1, plan.Files.Count, item.Frame.FileName, copiedBytes, totalBytes, speed, remaining, resumed));
            }
            var controlDirectory = Path.Combine(workingRoot, "_AstroForge");
            Directory.CreateDirectory(controlDirectory);
            if (!string.IsNullOrWhiteSpace(plan.PixInsightOutputFolderName))
                Directory.CreateDirectory(Path.Combine(workingRoot, plan.PixInsightOutputFolderName));
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            await WriteAtomicAsync(Path.Combine(controlDirectory, "manifest.json"), JsonSerializer.Serialize(new { schema = 3, application = "AstroProject Forge", mode = preflight.IsIncremental ? "incremental-update" : "verified-copy", project_name = plan.ProjectName, updated_at = DateTimeOffset.UtcNow, preflight = new { preflight.DestinationKind, preflight.TotalBytes, preflight.ResumeBytes, preflight.BytesToCopy, preflight.AvailableFreeBytes, preflight.RequiredFreeBytes, preflight.IsIncremental, preflight.NewFileCount }, files = records.Values }, jsonOptions), cancellationToken);
            await WriteAtomicAsync(Path.Combine(controlDirectory, "wbpp-recipe.json"), JsonSerializer.Serialize(new { schema = 1, grouping_keywords = plan.Recipe.Keywords.Select(keyword => new { keyword = keyword.Keyword, pre = keyword.Pre, post = keyword.Post, reason = keyword.Reason }), notes = plan.Recipe.Notes }, jsonOptions), cancellationToken);
            await WriteAtomicAsync(Path.Combine(controlDirectory, "export-preflight.json"), JsonSerializer.Serialize(preflight, jsonOptions), cancellationToken);
            if (plan.Statistics is not null)
            {
                await WriteAtomicAsync(Path.Combine(controlDirectory, "project-statistics.json"), JsonSerializer.Serialize(plan.Statistics, jsonOptions), cancellationToken);
                await WriteAtomicAsync(Path.Combine(controlDirectory, "project-statistics.csv"), StatisticsCsv(plan.Statistics), cancellationToken, new UTF8Encoding(true));
            }
            await WriteAtomicAsync(Path.Combine(controlDirectory, "wbpp-guide.md"), Guide(plan), cancellationToken);
            await WriteAtomicAsync(Path.Combine(controlDirectory, "validation-report.html"), ValidationReport(plan), cancellationToken);
            var historyEntries = new List<ExportHistoryEntry>();
            if (preflight.IsIncremental && ExportHistoryStore.Read(projectRoot).Entries.Count == 0 && preflight.ResumeFileCount > 0)
            {
                var addedPaths = addedFiles.Select(item => item.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var baseline = plan.Files.Where(item => !addedPaths.Contains(item.RelativePath)).ToArray();
                historyEntries.Add(ExportHistoryStore.CreateEntry(plan, baseline, 0, "baseline"));
            }
            if (!preflight.IsIncremental || addedFiles.Count > 0)
                historyEntries.Add(ExportHistoryStore.CreateEntry(plan, preflight.IsIncremental ? addedFiles : plan.Files, preflight.ResumeFileCount, preflight.IsIncremental ? "update" : "initial"));
            await ExportHistoryStore.AppendAsync(workingRoot, plan.ProjectName, historyEntries.ToArray());
            if (!preflight.IsIncremental) Directory.Move(staging, projectRoot);
            return projectRoot;
        }
        catch
        {
            // Staging remains available for diagnosis. Original files are never modified.
            throw;
        }
    }

    /// <summary>
    /// A first export that stopped half way leaves its copies in the staging folder to be resumed. If the plan changed in
    /// between (another Flat Set, a Light set aside), the copies of the old plan must not ride along into the project.
    /// </summary>
    private static void PruneStaging(string staging, ProjectPlan plan)
    {
        if (!Directory.Exists(staging)) return;
        var planned = plan.Files.Select(file => Path.GetFullPath(Path.Combine(staging, file.RelativePath))).ToHashSet(PathIdentity.Comparer);
        foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            if (!planned.Contains(Path.GetFullPath(file))) File.Delete(file);
        foreach (var directory in Directory.EnumerateDirectories(staging, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }

    private static Dictionary<string, JsonElement> ExistingManifestRecords(string projectRoot)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(projectRoot, "_AstroForge", "manifest.json");
        if (!File.Exists(path)) return result;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array) return result;
            foreach (var item in files.EnumerateArray())
                if (item.TryGetProperty("destination", out var destination) && destination.GetString() is { Length: > 0 } key)
                {
                    var existing = Path.GetFullPath(Path.Combine(projectRoot, key));
                    if (PathIdentity.IsWithin(existing, projectRoot) && File.Exists(existing)) result[key] = item.Clone();
                }
        }
        catch (JsonException) { }
        return result;
    }

    private static async Task<byte[]> CopyWithHashAsync(string source, string destination, ExportExecutionControl? control, CancellationToken cancellationToken)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 4 * 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4 * 1024 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        var buffer = new byte[4 * 1024 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (control is not null) await control.WaitIfPausedAsync(cancellationToken);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            hasher.AppendData(buffer, 0, read);
        }
        await output.FlushAsync(cancellationToken);
        return hasher.GetHashAndReset();
    }

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken cancellationToken, Encoding? encoding = null)
    {
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, content, encoding ?? new UTF8Encoding(false), cancellationToken);
        File.Move(temporary, path, true);
    }

    private static async Task<byte[]> HashAsync(string path, CancellationToken cancellationToken, ExportExecutionControl? control = null)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4 * 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[4 * 1024 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (control is not null) await control.WaitIfPausedAsync(cancellationToken);
            hasher.AppendData(buffer, 0, read);
        }
        return hasher.GetHashAndReset();
    }

    private static object Snapshot(FrameMetadata frame) => new
    {
        kind = frame.Kind.ToString().ToLowerInvariant(), frame.IsMaster,
        @object = frame.ObjectName.Value, filter = frame.FilterName.Value, flat_set = frame.FlatSetId.Value, session = frame.SessionId.Value,
        exposure_s = frame.ExposureSeconds.Value, gain = frame.Gain.Value, e_gain = frame.ElectronGain.Value,
        offset_value = frame.Offset.Value, temperature_c = frame.EffectiveTemperatureC, camera = frame.Camera.Value,
        binning = new[] { frame.XBin.Value, frame.YBin.Value }, geometry = new[] { frame.Width.Value, frame.Height.Value },
        readout_mode = frame.ReadoutMode.Value, bayer_pattern = frame.BayerPattern.Value,
        focal_length_mm = frame.FocalLengthMm.Value, rotator_angle_deg = frame.RotatorAngleDeg.Value,
        provenance = new { gain = frame.Gain.Source.ToString(), offset_value = frame.Offset.Source.ToString(), temperature = frame.SetTemperatureC.Source.ToString(), filter = frame.FilterName.Source.ToString(), flat_set = frame.FlatSetId.Source.ToString(), session = frame.SessionId.Source.ToString() }
    };

    private static string Guide(ProjectPlan plan)
    {
        var builder = new StringBuilder("# Configurazione PixInsight WBPP 3.x\n\n");
        builder.AppendLine("Caricare ricorsivamente `Light`, `Flat`, `Dark` e `Bias`.\n");
        builder.AppendLine("## Grouping Keywords\n");
        if (plan.Recipe.Keywords.Count == 0) builder.AppendLine("Lasciare vuota la tabella **Grouping Keywords**.\n");
        foreach (var keyword in plan.Recipe.Keywords) builder.AppendLine($"- `{keyword.Keyword}` — Pre: {(keyword.Pre ? "ON" : "OFF")}, Post: {(keyword.Post ? "ON" : "OFF")} — {keyword.Reason}");
        builder.AppendLine("\n`FILTER`, binning ed esposizione sono gestiti nativamente. Non aggiungere `DATE-OBS`. Smart naming override non è necessario per keyword personalizzate nel percorso.");
        builder.AppendLine("\nPrima di Run, verificare nella scheda Calibration che ogni Light mostri Dark, Flat e Bias previsti.");
        return builder.ToString();
    }

    private static string ValidationReport(ProjectPlan plan)
    {
        static string E(object? value) => System.Net.WebUtility.HtmlEncode(value?.ToString() ?? "");
        var rows = string.Concat(plan.Files.Select(file => $"<tr><td>{E(file.Role)}</td><td>{E(file.RelativePath)}</td><td>{E(file.Frame.FilterName.Value)}</td><td>{E(file.Frame.SessionId.Value)}</td><td>{E(file.Frame.Gain.Value)}</td><td>{E(file.Frame.EffectiveTemperatureC)}</td></tr>"));
        return "<!doctype html><html lang=\"it\"><head><meta charset=\"utf-8\"><title>" + E(plan.ProjectName) + "</title>" +
               "<style>body{font:14px Segoe UI;background:#080b11;color:#eef3f8;margin:36px}h1,.ok{color:#41d8c7}table{border-collapse:collapse;width:100%}td,th{padding:9px;border-bottom:1px solid #243043;text-align:left}</style></head>" +
               $"<body><p class=\"ok\">COPIA VERIFICATA</p><h1>{E(plan.ProjectName)}</h1><p>{plan.Files.Count} file organizzati. Gli originali non sono stati modificati.</p><table><tr><th>Ruolo</th><th>Destinazione</th><th>Filtro</th><th>Sessione</th><th>Gain</th><th>Temp.</th></tr>{rows}</table></body></html>";
    }

    private static string StatisticsCsv(ProjectStatistics statistics)
    {
        static string C(object? value)
        {
            var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
            return text.Contains(';') || text.Contains('"') || text.Contains('\n') ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
        }
        var builder = new StringBuilder("SEZIONE;FILTRO;SESSIONE/NOTTE;ORE;LIGHT;NOTTI;FLAT;STATO\n");
        foreach (var item in statistics.Filters)
            builder.AppendLine(string.Join(';', "FILTRO", C(item.Filter), "", C(Math.Round(item.ExposureHours, 3)), C(item.LightCount), C(item.NightCount), C(item.FlatFrameCount), C(item.Ready ? "Pronto" : $"{item.UnresolvedCalibrationCount} irrisolte")));
        foreach (var item in statistics.Sessions)
            builder.AppendLine(string.Join(';', "SESSIONE", C(item.Filter), C(item.Session), C(Math.Round(item.ExposureHours, 3)), C(item.LightCount), C(item.NightCount), C(item.FlatFrameCount), C(item.Ready ? "Pronta" : $"{item.UnresolvedCalibrationCount} irrisolte")));
        foreach (var item in statistics.Nights)
            builder.AppendLine(string.Join(';', "NOTTE", C(item.Filter), C(item.Night), C(Math.Round(item.ExposureHours, 3)), C(item.LightCount), "1", "", C(item.IssueCount == 0 ? "OK" : $"{item.IssueCount} avvisi")));
        return builder.ToString();
    }

    private static void Add(List<PlannedFile> files, HashSet<string> included, PlannedFile item)
    {
        var key = $"{Path.GetFullPath(item.Frame.Path)}|{item.RelativePath}";
        if (included.Add(key)) files.Add(item);
    }
    private static void EnsureNoCollisions(IEnumerable<PlannedFile> files)
    {
        var destinations = new Dictionary<string, string>(PathIdentity.Comparer);
        foreach (var file in files)
        {
            if (destinations.TryGetValue(file.RelativePath, out var existing) && !PathIdentity.Equals(existing, file.Frame.Path))
                throw new IOException($"Collisione destinazione: {file.RelativePath}");
            destinations[file.RelativePath] = file.Frame.Path;
        }
    }
    private static void ResolveCollisions(List<PlannedFile> files)
    {
        var occupied = new HashSet<string>(PathIdentity.Comparer);
        for (var index = 0; index < files.Count; index++)
        {
            var item = files[index];
            if (occupied.Add(item.RelativePath)) continue;
            var directory = Path.GetDirectoryName(item.RelativePath) ?? "";
            var stem = Path.GetFileNameWithoutExtension(item.RelativePath);
            var extension = Path.GetExtension(item.RelativePath);
            var suffix = 2;
            string candidate;
            do candidate = Path.Combine(directory, $"{stem}__{suffix++:00}{extension}");
            while (!occupied.Add(candidate));
            files[index] = item with { RelativePath = candidate };
        }
    }
    private static string MasterName(FrameMetadata frame)
    {
        var geometry = $"{frame.Width.Value ?? 0}x{frame.Height.Value ?? 0}";
        return frame.Kind switch
        {
            FrameKind.Dark => $"MasterDark_G{Number(frame.Gain.Value)}_T{Number(frame.EffectiveTemperatureC)}_O{Number(frame.Offset.Value)}_E{Number(frame.ExposureSeconds.Value)}s_{geometry}{Path.GetExtension(frame.Path).ToLowerInvariant()}",
            FrameKind.Bias => $"MasterBias_G{Number(frame.Gain.Value)}_O{Number(frame.Offset.Value)}_{geometry}{Path.GetExtension(frame.Path).ToLowerInvariant()}",
            FrameKind.DarkFlat => $"MasterDarkFlat_G{Number(frame.Gain.Value)}_E{Number(frame.ExposureSeconds.Value)}s_{geometry}{Path.GetExtension(frame.Path).ToLowerInvariant()}",
            _ => frame.FileName
        };
    }
    // A raw sub reports the sensor's reading (-9.8 °C): its set is named after the whole degree it was cooled to.
    private static string DarkSet(FrameMetadata frame) =>
        $"G{Number(frame.Gain.Value)}-T{Number(frame.IsMaster ? frame.EffectiveTemperatureC : frame.EffectiveTemperatureC is { } reading ? Math.Round(reading) : null)}-O{Number(frame.Offset.Value)}";
    private static string DarkFlatSet(FrameMetadata frame) => $"G{Number(frame.Gain.Value)}-E{Number(frame.ExposureSeconds.Value)}s";
    private static string BiasSet(FrameMetadata frame) => $"G{Number(frame.Gain.Value)}-O{Number(frame.Offset.Value)}";
    private static string Number(double? value) => value?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? throw new InvalidOperationException("Metadato numerico richiesto assente.");
    private static string Combine(IEnumerable<string> parts) => Path.Combine(parts.ToArray());
    // The same set on every system: a project made on macOS or Linux must still open on Windows and on exFAT drives,
    // where these characters and a trailing dot are refused.
    private static readonly char[] UnsafeNameCharacters = [.. "<>:\"/\\|?* ", .. Enumerable.Range(0, 32).Select(code => (char)code)];
    private static string Token(string value) => string.Join('_', value.Split(UnsafeNameCharacters, StringSplitOptions.RemoveEmptyEntries)).Trim('.');
    private static string WbppValue(string value) => new string(value.Select(character => char.IsLetterOrDigit(character) || "-(). ".Contains(character) ? character : '-').ToArray()).Replace(" ", "-").Trim('.', '-');
    private static string SafeName(string value)
    {
        var result = Token(value).Trim('.', ' ');
        if (result.Length == 0) throw new ArgumentException("Nome progetto non valido.");
        return result.Length > 120 ? result[..120] : result;
    }
}
