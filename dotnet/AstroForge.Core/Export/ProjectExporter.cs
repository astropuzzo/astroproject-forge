using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AstroForge.Core.Analysis;
using AstroForge.Core.Models;
using AstroForge.Core.Wbpp;

namespace AstroForge.Core.Export;

public sealed record PlannedFile(FrameMetadata Frame, string RelativePath, string Role, string? GroupId = null);
public sealed record ProjectPlan(string ProjectName, string DestinationRoot, IReadOnlyList<PlannedFile> Files, WbppRecipe Recipe, ProjectStatistics? Statistics = null)
{
    public long RequiredBytes => Files.Sum(file => new FileInfo(file.Frame.Path).Length);
    public string ProjectRoot => Path.Combine(DestinationRoot, ProjectName);
}
public sealed record ExportProgress(int Completed, int Total, string CurrentFile, long BytesCopied, long TotalBytes);

public static class ProjectExporter
{
    public static ProjectPlan BuildPlan(string projectName, string destinationRoot, ProjectAnalysis analysis)
    {
        if (!analysis.Ready) throw new InvalidOperationException("Il progetto contiene calibrazioni irrisolte.");
        var recipe = WbppRecipeEngine.Recommend(analysis);
        var files = new List<PlannedFile>();
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in analysis.Lights)
        {
            if (item.FlatGroup is null || item.Dark.Selected is null || item.Bias.Selected is null)
                throw new InvalidOperationException($"Calibrazioni incomplete per {item.Light.FileName}.");
            var light = item.Light;
            var filter = Token(light.FilterName.Value ?? "UNKNOWN");
            var session = Token(light.SessionId.Value ?? "UNKNOWN");
            var flatSet = item.FlatGroup.Id;
            var darkSet = DarkSet(item.Dark.Selected.Frame);
            var biasSet = BiasSet(item.Bias.Selected.Frame);
            var parts = new List<string> { "Light", $"FILTER_{filter}" };
            if (recipe.Contains("FLATSET")) parts.Add($"FLATSET_{flatSet}");
            if (recipe.Contains("DARKSET")) parts.Add($"DARKSET_{darkSet}");
            if (recipe.Contains("BIASSET")) parts.Add($"BIASSET_{biasSet}");
            if (recipe.Contains("TARGET")) parts.Add($"TARGET_{WbppValue(light.ObjectName.Value ?? "UNKNOWN")}");
            parts.Add($"NIGHT_{session}");
            parts.Add(light.FileName);
            Add(files, included, new(light, Combine(parts), "light", flatSet));

            foreach (var flat in item.FlatGroup.Frames)
            {
                var flatParts = new List<string> { "Flat", $"FILTER_{filter}" };
                if (recipe.Contains("FLATSET")) flatParts.Add($"FLATSET_{flatSet}");
                if (recipe.Contains("BIASSET")) flatParts.Add($"BIASSET_{biasSet}");
                flatParts.Add(flat.FileName);
                Add(files, included, new(flat, Combine(flatParts), "flat", flatSet));
            }
            var dark = item.Dark.Selected.Frame;
            var bias = item.Bias.Selected.Frame;
            Add(files, included, new(dark, Combine(["Dark", "Masters", $"DARKSET_{darkSet}", MasterName(dark)]), "dark", darkSet));
            Add(files, included, new(bias, Combine(["Bias", "Masters", $"BIASSET_{biasSet}", MasterName(bias)]), "bias", biasSet));
        }
        ResolveCollisions(files);
        EnsureNoCollisions(files);
        return new(SafeName(projectName), Path.GetFullPath(destinationRoot), files, recipe, ProjectStatisticsCalculator.Calculate(analysis));
    }

    public static async Task<string> ExecuteAsync(ProjectPlan plan, IProgress<ExportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var projectRoot = plan.ProjectRoot;
        var staging = Path.Combine(plan.DestinationRoot, $".{plan.ProjectName}.astroforge-staging");
        if (Directory.Exists(projectRoot)) throw new IOException($"La destinazione esiste già: {projectRoot}");
        Directory.CreateDirectory(staging);
        var totalBytes = plan.RequiredBytes;
        long copiedBytes = 0;
        var records = new List<object>();
        try
        {
            for (var index = 0; index < plan.Files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = plan.Files[index];
                var destination = Path.Combine(staging, item.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var partial = destination + ".partial";
                byte[] hash;
                if (File.Exists(destination))
                {
                    hash = await HashAsync(item.Frame.Path, cancellationToken);
                    var existingHash = await HashAsync(destination, cancellationToken);
                    if (!hash.SequenceEqual(existingHash)) throw new IOException($"Il file di ripresa non coincide con l'originale: {destination}");
                }
                else
                {
                    if (File.Exists(partial)) File.Delete(partial);
                    hash = await CopyWithHashAsync(item.Frame.Path, partial, cancellationToken);
                    var verification = await HashAsync(partial, cancellationToken);
                    if (!hash.SequenceEqual(verification)) throw new IOException($"Verifica SHA-256 fallita: {item.Frame.Path}");
                    File.Move(partial, destination);
                    File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(item.Frame.Path));
                }
                copiedBytes += new FileInfo(destination).Length;
                records.Add(new
                {
                    source = Path.GetFullPath(item.Frame.Path),
                    destination = item.RelativePath.Replace('\\', '/'),
                    role = item.Role,
                    group_id = item.GroupId,
                    bytes = new FileInfo(destination).Length,
                    sha256 = Convert.ToHexString(hash).ToLowerInvariant(),
                    metadata = Snapshot(item.Frame)
                });
                progress?.Report(new(index + 1, plan.Files.Count, item.Frame.FileName, copiedBytes, totalBytes));
            }
            var control = Path.Combine(staging, "_AstroForge");
            Directory.CreateDirectory(control);
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(Path.Combine(control, "manifest.json"), JsonSerializer.Serialize(new { schema = 1, application = "AstroProject Forge", mode = "verified-copy", project_name = plan.ProjectName, created_at = DateTimeOffset.UtcNow, files = records }, jsonOptions), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(control, "wbpp-recipe.json"), JsonSerializer.Serialize(new { schema = 1, grouping_keywords = plan.Recipe.Keywords.Select(keyword => new { keyword = keyword.Keyword, pre = keyword.Pre, post = keyword.Post, reason = keyword.Reason }), notes = plan.Recipe.Notes }, jsonOptions), cancellationToken);
            if (plan.Statistics is not null)
            {
                await File.WriteAllTextAsync(Path.Combine(control, "project-statistics.json"), JsonSerializer.Serialize(plan.Statistics, jsonOptions), cancellationToken);
                await File.WriteAllTextAsync(Path.Combine(control, "project-statistics.csv"), StatisticsCsv(plan.Statistics), new UTF8Encoding(true), cancellationToken);
            }
            await File.WriteAllTextAsync(Path.Combine(control, "wbpp-guide.md"), Guide(plan), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(control, "validation-report.html"), ValidationReport(plan), cancellationToken);
            Directory.Move(staging, projectRoot);
            return projectRoot;
        }
        catch
        {
            // Staging remains available for diagnosis. Original files are never modified.
            throw;
        }
    }

    private static async Task<byte[]> CopyWithHashAsync(string source, string destination, CancellationToken cancellationToken)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 4 * 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4 * 1024 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        var buffer = new byte[4 * 1024 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            hasher.AppendData(buffer, 0, read);
        }
        await output.FlushAsync(cancellationToken);
        return hasher.GetHashAndReset();
    }

    private static async Task<byte[]> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4 * 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await SHA256.HashDataAsync(stream, cancellationToken);
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
            builder.AppendLine(string.Join(';', "FILTRO", C(item.Filter), "", C(item.ExposureHours), C(item.LightCount), C(item.NightCount), C(item.FlatFrameCount), C(item.Ready ? "Pronto" : $"{item.UnresolvedCalibrationCount} irrisolte")));
        foreach (var item in statistics.Sessions)
            builder.AppendLine(string.Join(';', "SESSIONE", C(item.Filter), C(item.Session), C(item.ExposureHours), C(item.LightCount), C(item.NightCount), C(item.FlatFrameCount), C(item.Ready ? "Pronta" : $"{item.UnresolvedCalibrationCount} irrisolte")));
        foreach (var item in statistics.Nights)
            builder.AppendLine(string.Join(';', "NOTTE", C(item.Filter), C(item.Night), C(item.ExposureHours), C(item.LightCount), "1", "", C(item.IssueCount == 0 ? "OK" : $"{item.IssueCount} avvisi")));
        return builder.ToString();
    }

    private static void Add(List<PlannedFile> files, HashSet<string> included, PlannedFile item)
    {
        var key = $"{Path.GetFullPath(item.Frame.Path)}|{item.RelativePath}";
        if (included.Add(key)) files.Add(item);
    }
    private static void EnsureNoCollisions(IEnumerable<PlannedFile> files)
    {
        var destinations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (destinations.TryGetValue(file.RelativePath, out var existing) && !existing.Equals(file.Frame.Path, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Collisione destinazione: {file.RelativePath}");
            destinations[file.RelativePath] = file.Frame.Path;
        }
    }
    private static void ResolveCollisions(List<PlannedFile> files)
    {
        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            _ => frame.FileName
        };
    }
    private static string DarkSet(FrameMetadata frame) => $"G{Number(frame.Gain.Value)}-T{Number(frame.EffectiveTemperatureC)}-O{Number(frame.Offset.Value)}";
    private static string BiasSet(FrameMetadata frame) => $"G{Number(frame.Gain.Value)}-O{Number(frame.Offset.Value)}";
    private static string Number(double? value) => value?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? throw new InvalidOperationException("Metadato numerico richiesto assente.");
    private static string Combine(IEnumerable<string> parts) => Path.Combine(parts.ToArray());
    private static string Token(string value) => string.Join('_', value.Split(Path.GetInvalidFileNameChars().Concat([' ']).ToArray(), StringSplitOptions.RemoveEmptyEntries));
    private static string WbppValue(string value) => new string(value.Select(character => char.IsLetterOrDigit(character) || "-():. ".Contains(character) ? character : '-').ToArray()).Replace(" ", "-");
    private static string SafeName(string value)
    {
        var result = Token(value).Trim('.', ' ');
        if (result.Length == 0) throw new ArgumentException("Nome progetto non valido.");
        return result.Length > 120 ? result[..120] : result;
    }
}
