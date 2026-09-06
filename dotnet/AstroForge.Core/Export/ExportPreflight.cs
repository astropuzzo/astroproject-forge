using System.Security.Cryptography;
using AstroForge.Core.IO;

namespace AstroForge.Core.Export;

public enum ExportPreflightSeverity { Information, Warning, Error }
public enum ExportDestinationKind { Fixed, Removable, Network, Unknown }

public sealed record ExportPreflightOptions(
    double FreeSpaceMarginPercent = 10,
    long MinimumReserveBytes = 1L * 1024 * 1024 * 1024,
    double EstimatedThroughputMiBPerSecond = 100,
    IReadOnlyList<string>? SourceRoots = null);

public sealed record ExportPreflightFinding(
    string Code,
    ExportPreflightSeverity Severity,
    string Title,
    string Detail,
    string? Path = null);

public sealed record ExportReuseMatch(string PlannedRelativePath, string ExistingRelativePath);

public sealed record ExportPreflightReport(
    DateTimeOffset CreatedAtUtc,
    string ProjectRoot,
    string StagingRoot,
    ExportDestinationKind DestinationKind,
    int TotalFiles,
    long TotalBytes,
    int ResumeFileCount,
    long ResumeBytes,
    long BytesToCopy,
    long? AvailableFreeBytes,
    long RequiredFreeBytes,
    TimeSpan EstimatedDuration,
    IReadOnlyList<ExportPreflightFinding> Findings,
    bool IsIncremental = false,
    int NewFileCount = 0,
    IReadOnlyList<ExportReuseMatch>? ReuseMatches = null)
{
    public int ErrorCount => Findings.Count(item => item.Severity == ExportPreflightSeverity.Error);
    public int WarningCount => Findings.Count(item => item.Severity == ExportPreflightSeverity.Warning);
    public bool IsReady => ErrorCount == 0;
}

public sealed class ExportPreflightException(ExportPreflightReport report)
    : IOException($"Preflight export bloccato: {report.ErrorCount} errori e {report.WarningCount} avvisi.")
{
    public ExportPreflightReport Report { get; } = report;
}

public static class ProjectExportPreflight
{
    public static async Task<ExportPreflightReport> AnalyzeAsync(
        ProjectPlan plan,
        ExportPreflightOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new();
        if (options.FreeSpaceMarginPercent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(options.FreeSpaceMarginPercent));
        if (options.MinimumReserveBytes < 0) throw new ArgumentOutOfRangeException(nameof(options.MinimumReserveBytes));
        if (options.EstimatedThroughputMiBPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(options.EstimatedThroughputMiBPerSecond));

        var findings = new List<ExportPreflightFinding>();
        var destinationRoot = Path.GetFullPath(plan.DestinationRoot);
        var projectRoot = Path.GetFullPath(plan.ProjectRoot);
        var stagingRoot = Path.GetFullPath(Path.Combine(destinationRoot, $".{plan.ProjectName}.astroforge-staging"));
        var incremental = Directory.Exists(projectRoot);
        var copyRoot = incremental ? projectRoot : stagingRoot;
        var comparer = PathIdentity.Comparer;
        var destinations = new HashSet<string>(comparer);
        var totalBytes = 0L;
        var resumeBytes = 0L;
        var resumeFiles = 0;
        var longPaths = 0;
        var partialFiles = 0;
        var reuseMatches = new List<ExportReuseMatch>();
        var manifestRecords = Array.Empty<ManifestFile>();

        if (incremental)
        {
            var manifest = Path.Combine(projectRoot, "_AstroForge", "manifest.json");
            if (!IsManagedProject(manifest, plan.ProjectName))
                findings.Add(Error("destination.unmanaged", "La cartella esistente non è un progetto aggiornabile", "Per sicurezza l’app aggiorna soltanto cartelle create da AstroProject Forge e dotate di un manifest valido.", projectRoot));
            else
            {
                manifestRecords = ReadManifestFiles(manifest);
                findings.Add(Info("destination.update", "Aggiornamento incrementale", "I file identici saranno riutilizzati; verranno copiati soltanto quelli nuovi.", projectRoot));
            }
        }

        foreach (var root in NormalizeRoots(options.SourceRoots))
        {
            if (IsWithin(projectRoot, root) || IsWithin(stagingRoot, root) || IsWithin(root, projectRoot) || IsWithin(root, stagingRoot))
                findings.Add(Error("destination.overlap", "Sorgente e destinazione si sovrappongono", "La cartella progetto o lo staging ricadono dentro una sorgente, oppure la contengono. Scegli una destinazione separata.", root));
        }

        foreach (var item in plan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = Path.GetFullPath(item.Frame.Path);
            if (IsWithin(source, projectRoot) || IsWithin(source, stagingRoot))
                findings.Add(Error("source.inside_destination", "Sorgente dentro la destinazione", "Un file sorgente ricade nel progetto finale o nello staging. Scegli una destinazione completamente separata.", source));
            if (!File.Exists(source))
            {
                findings.Add(Error("source.missing", "Sorgente non disponibile", "Il file è stato spostato, rinominato o il volume non è collegato.", source));
                continue;
            }

            long length;
            try
            {
                var info = new FileInfo(source);
                length = info.Length;
                totalBytes = checked(totalBytes + length);
                using var probe = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    findings.Add(Warning("source.reparse", "Sorgente tramite link filesystem", "Il file è raggiunto attraverso un reparse point: verifica che il volume resti disponibile durante l’export.", source));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                findings.Add(Error("source.unreadable", "Sorgente non leggibile", exception.Message, source));
                continue;
            }

            if (Path.IsPathRooted(item.RelativePath))
            {
                findings.Add(Error("path.rooted", "Destinazione relativa non valida", "Il piano contiene un percorso assoluto.", item.RelativePath));
                continue;
            }

            var staged = Path.GetFullPath(Path.Combine(copyRoot, item.RelativePath));
            if (!IsWithin(staged, copyRoot))
            {
                findings.Add(Error("path.traversal", "Percorso esterno allo staging", "Il piano tenterebbe di uscire dalla cartella controllata.", item.RelativePath));
                continue;
            }
            if (!destinations.Add(staged))
                findings.Add(Error("destination.duplicate", "Destinazione duplicata", "Più file produrrebbero lo stesso percorso.", item.RelativePath));
            if (staged.Length > 240) longPaths++;

            var partial = staged + ".partial";
            if (File.Exists(partial)) partialFiles++;
            if (!File.Exists(staged))
            {
                if (incremental && manifestRecords.Length > 0)
                {
                    var sourceHash = Convert.ToHexString(await HashAsync(source, cancellationToken)).ToLowerInvariant();
                    var sourceMatches = manifestRecords.Where(record => PathIdentity.Equals(record.Source, source)).ToArray();
                    var candidates = sourceMatches.Concat(manifestRecords.Where(record => record.Sha256.Equals(sourceHash, StringComparison.OrdinalIgnoreCase)))
                        .DistinctBy(record => record.Destination, comparer);
                    var reused = false;
                    foreach (var candidate in candidates)
                    {
                        var existing = Path.GetFullPath(Path.Combine(projectRoot, candidate.Destination));
                        if (!IsWithin(existing, projectRoot) || !File.Exists(existing)) continue;
                        if (new FileInfo(existing).Length != length || !Convert.ToHexString(await HashAsync(existing, cancellationToken)).Equals(sourceHash, StringComparison.OrdinalIgnoreCase))
                        {
                            if (sourceMatches.Contains(candidate))
                                findings.Add(Error("update.manifest_conflict", "File già registrato ma modificato", "Il file associato alla stessa sorgente non corrisponde più al contenuto registrato. Nessuna copia verrà eseguita.", existing));
                            continue;
                        }
                        reuseMatches.Add(new(item.RelativePath, candidate.Destination));
                        resumeFiles++;
                        resumeBytes = checked(resumeBytes + length);
                        reused = true;
                        break;
                    }
                    if (reused) continue;
                }
                continue;
            }
            try
            {
                var stagedInfo = new FileInfo(staged);
                if (stagedInfo.Length != length)
                {
                    findings.Add(Error(incremental ? "update.size_conflict" : "resume.size_mismatch", incremental ? "File esistente diverso" : "Copia di ripresa non coerente", incremental ? "Il percorso è già occupato da un file con dimensione diversa. Nessun dato verrà sovrascritto." : "La dimensione del file nello staging non coincide con la sorgente.", staged));
                    continue;
                }
                var sourceHash = await HashAsync(source, cancellationToken);
                var stagedHash = await HashAsync(staged, cancellationToken);
                if (!sourceHash.SequenceEqual(stagedHash))
                {
                    findings.Add(Error(incremental ? "update.hash_conflict" : "resume.hash_mismatch", incremental ? "File esistente diverso" : "Copia di ripresa alterata", incremental ? "Il nome coincide ma il contenuto è diverso. Nessun dato verrà sovrascritto." : "Lo SHA-256 nello staging non coincide con la sorgente.", staged));
                    continue;
                }
                resumeFiles++;
                resumeBytes = checked(resumeBytes + length);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                findings.Add(Error("resume.unreadable", "Staging non leggibile", exception.Message, staged));
            }
        }

        if (longPaths > 0)
            findings.Add(Warning("path.long", "Percorsi lunghi", OperatingSystem.IsWindows()
                ? $"{longPaths} destinazioni superano 240 caratteri. Windows moderno le supporta, ma strumenti esterni potrebbero non farlo."
                : $"{longPaths} destinazioni superano 240 caratteri. Il filesystem può supportarle, ma PixInsight o supporti condivisi potrebbero imporre limiti inferiori."));
        if (partialFiles > 0)
            findings.Add(Warning("resume.partial", "Copie parziali rilevate", $"{partialFiles} file .partial verranno ricreati; le copie già verificate restano riutilizzabili."));
        if (resumeFiles > 0)
            findings.Add(Info(incremental ? "update.unchanged" : "resume.ready", incremental ? "File invariati" : "Ripresa disponibile", $"{resumeFiles} file già verificati non verranno ricopiati."));

        var bytesToCopy = Math.Max(0, totalBytes - resumeBytes);
        var reserve = Math.Max(options.MinimumReserveBytes, (long)Math.Ceiling(totalBytes * options.FreeSpaceMarginPercent / 100d));
        var requiredFree = checked(bytesToCopy + reserve);
        var (kind, freeBytes) = Destination(destinationRoot, findings);
        if (freeBytes is { } available && available < requiredFree)
            findings.Add(Error("space.insufficient", "Spazio libero insufficiente", $"Servono {HumanSize(requiredFree)} includendo il margine; disponibili {HumanSize(available)}.", destinationRoot));
        else if (freeBytes is null)
            findings.Add(Warning("space.unknown", "Spazio libero non verificabile", "La destinazione di rete o il provider filesystem non espongono lo spazio disponibile. Verificalo prima dell’export.", destinationRoot));

        CheckDestinationReparsePoints(destinationRoot, findings);
        findings.Add(Info("preflight.read_only", "Controlli completati", "La verifica non ha creato, modificato o eliminato file nella destinazione."));
        var seconds = bytesToCopy / (options.EstimatedThroughputMiBPerSecond * 1024d * 1024d);
        return new(DateTimeOffset.UtcNow, projectRoot, stagingRoot, kind, plan.Files.Count, totalBytes, resumeFiles, resumeBytes,
            bytesToCopy, freeBytes, requiredFree, TimeSpan.FromSeconds(seconds), findings, incremental, Math.Max(0, plan.Files.Count - resumeFiles), reuseMatches);
    }

    private sealed record ManifestFile(string Source, string Destination, string Sha256);

    private static ManifestFile[] ReadManifestFiles(string manifestPath)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!document.RootElement.TryGetProperty("files", out var files) || files.ValueKind != System.Text.Json.JsonValueKind.Array) return [];
            return files.EnumerateArray().Select(item => new ManifestFile(
                    item.TryGetProperty("source", out var source) ? source.GetString() ?? "" : "",
                    item.TryGetProperty("destination", out var destination) ? destination.GetString() ?? "" : "",
                    item.TryGetProperty("sha256", out var sha) ? sha.GetString() ?? "" : ""))
                .Where(item => item.Source.Length > 0 && item.Destination.Length > 0 && item.Sha256.Length == 64).ToArray();
        }
        catch (System.Text.Json.JsonException) { return []; }
    }

    private static bool IsManagedProject(string manifestPath, string projectName)
    {
        if (!File.Exists(manifestPath)) return false;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            return root.TryGetProperty("application", out var application)
                && application.GetString() == "AstroProject Forge"
                && root.TryGetProperty("project_name", out var name)
                && string.Equals(name.GetString(), projectName, StringComparison.OrdinalIgnoreCase);
        }
        catch (System.Text.Json.JsonException) { return false; }
    }

    private static IEnumerable<string> NormalizeRoots(IReadOnlyList<string>? roots)
    {
        if (roots is null) yield break;
        foreach (var value in roots.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            string full;
            try { full = Path.GetFullPath(value); }
            catch { continue; }
            yield return File.Exists(full) ? Path.GetDirectoryName(full)! : full;
        }
    }

    private static (ExportDestinationKind Kind, long? FreeBytes) Destination(string destinationRoot, List<ExportPreflightFinding> findings)
    {
        if (destinationRoot.StartsWith(@"\\", StringComparison.Ordinal))
        {
            findings.Add(Warning("destination.network", "Destinazione di rete", "Mantieni la connessione stabile: lo staging permette di riprendere una copia interrotta.", destinationRoot));
            return (ExportDestinationKind.Network, null);
        }
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(destinationRoot)!);
            var kind = drive.DriveType switch
            {
                DriveType.Fixed => ExportDestinationKind.Fixed,
                DriveType.Removable => ExportDestinationKind.Removable,
                DriveType.Network => ExportDestinationKind.Network,
                _ => ExportDestinationKind.Unknown
            };
            if (kind == ExportDestinationKind.Removable)
                findings.Add(Warning("destination.removable", "Unità rimovibile", "Non scollegare il volume fino al completamento della verifica SHA-256.", destinationRoot));
            return (kind, drive.IsReady ? drive.AvailableFreeSpace : null);
        }
        catch { return (ExportDestinationKind.Unknown, null); }
    }

    private static void CheckDestinationReparsePoints(string destinationRoot, List<ExportPreflightFinding> findings)
    {
        try
        {
            var current = new DirectoryInfo(destinationRoot);
            while (current is not null && !current.Exists) current = current.Parent;
            var selectedAnchor = current?.FullName;
            for (; current is not null; current = current.Parent)
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    findings.Add(PathIdentity.Equals(current.FullName, selectedAnchor)
                        ? Error("destination.reparse", "Destinazione tramite junction o symlink", "Per evitare deviazioni inattese, scegli un percorso fisico o di rete esplicito.", current.FullName)
                        : Warning("destination.ancestor_link", "Percorso con collegamento di sistema", "Un antenato della destinazione è un junction o symlink. La destinazione selezionata è fisica, ma il percorso canonico può differire.", current.FullName));
                    return;
                }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            findings.Add(Warning("destination.inspect", "Destinazione non completamente ispezionabile", exception.Message, destinationRoot));
        }
    }

    private static bool IsWithin(string candidate, string parent)
    {
        return PathIdentity.IsWithin(candidate, parent);
    }

    private static async Task<byte[]> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4 * 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await SHA256.HashDataAsync(stream, cancellationToken);
    }

    private static ExportPreflightFinding Info(string code, string title, string detail, string? path = null) => new(code, ExportPreflightSeverity.Information, title, detail, path);
    private static ExportPreflightFinding Warning(string code, string title, string detail, string? path = null) => new(code, ExportPreflightSeverity.Warning, title, detail, path);
    private static ExportPreflightFinding Error(string code, string title, string detail, string? path = null) => new(code, ExportPreflightSeverity.Error, title, detail, path);
    private static string HumanSize(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        var value = (double)Math.Max(0, bytes); var index = 0;
        while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
        return $"{value:0.##} {units[index]}";
    }
}
