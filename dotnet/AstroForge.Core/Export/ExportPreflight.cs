using System.Security.Cryptography;

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
    IReadOnlyList<ExportPreflightFinding> Findings)
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
        var comparer = StringComparer.OrdinalIgnoreCase;
        var destinations = new HashSet<string>(comparer);
        var totalBytes = 0L;
        var resumeBytes = 0L;
        var resumeFiles = 0;
        var longPaths = 0;
        var partialFiles = 0;

        if (Directory.Exists(projectRoot))
            findings.Add(Error("destination.exists", "Il progetto esiste già", "Scegli un altro nome o un’altra destinazione. Nessun file esistente verrà sovrascritto.", projectRoot));

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

            var staged = Path.GetFullPath(Path.Combine(stagingRoot, item.RelativePath));
            if (!IsWithin(staged, stagingRoot))
            {
                findings.Add(Error("path.traversal", "Percorso esterno allo staging", "Il piano tenterebbe di uscire dalla cartella controllata.", item.RelativePath));
                continue;
            }
            if (!destinations.Add(staged))
                findings.Add(Error("destination.duplicate", "Destinazione duplicata", "Più file produrrebbero lo stesso percorso.", item.RelativePath));
            if (staged.Length > 240) longPaths++;

            var partial = staged + ".partial";
            if (File.Exists(partial)) partialFiles++;
            if (!File.Exists(staged)) continue;
            try
            {
                var stagedInfo = new FileInfo(staged);
                if (stagedInfo.Length != length)
                {
                    findings.Add(Error("resume.size_mismatch", "Copia di ripresa non coerente", "La dimensione del file nello staging non coincide con la sorgente.", staged));
                    continue;
                }
                var sourceHash = await HashAsync(source, cancellationToken);
                var stagedHash = await HashAsync(staged, cancellationToken);
                if (!sourceHash.SequenceEqual(stagedHash))
                {
                    findings.Add(Error("resume.hash_mismatch", "Copia di ripresa alterata", "Lo SHA-256 nello staging non coincide con la sorgente.", staged));
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
            findings.Add(Warning("path.long", "Percorsi lunghi", $"{longPaths} destinazioni superano 240 caratteri. Windows moderno le supporta, ma strumenti esterni potrebbero non farlo."));
        if (partialFiles > 0)
            findings.Add(Warning("resume.partial", "Copie parziali rilevate", $"{partialFiles} file .partial verranno ricreati; le copie già verificate restano riutilizzabili."));
        if (resumeFiles > 0)
            findings.Add(Info("resume.ready", "Ripresa disponibile", $"{resumeFiles} file già verificati non verranno ricopiati."));

        var bytesToCopy = Math.Max(0, totalBytes - resumeBytes);
        var reserve = Math.Max(options.MinimumReserveBytes, (long)Math.Ceiling(totalBytes * options.FreeSpaceMarginPercent / 100d));
        var requiredFree = checked(bytesToCopy + reserve);
        var (kind, freeBytes) = Destination(destinationRoot, findings);
        if (freeBytes is { } available && available < requiredFree)
            findings.Add(Error("space.insufficient", "Spazio libero insufficiente", $"Servono {HumanSize(requiredFree)} includendo il margine; disponibili {HumanSize(available)}.", destinationRoot));
        else if (freeBytes is null)
            findings.Add(Warning("space.unknown", "Spazio libero non verificabile", "La destinazione di rete o il provider filesystem non espongono lo spazio disponibile. Verificalo prima dell’export.", destinationRoot));

        CheckDestinationReparsePoints(destinationRoot, findings);
        findings.Add(Info("dry_run.clean", "Dry-run completato", "Il preflight non ha creato, modificato o eliminato file nella destinazione."));
        var seconds = bytesToCopy / (options.EstimatedThroughputMiBPerSecond * 1024d * 1024d);
        return new(DateTimeOffset.UtcNow, projectRoot, stagingRoot, kind, plan.Files.Count, totalBytes, resumeFiles, resumeBytes,
            bytesToCopy, freeBytes, requiredFree, TimeSpan.FromSeconds(seconds), findings);
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
            for (; current is not null; current = current.Parent)
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    findings.Add(Error("destination.reparse", "Destinazione tramite junction o symlink", "Per evitare deviazioni inattese, scegli un percorso fisico o di rete esplicito.", current.FullName));
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
        candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        return candidate.Equals(parent, StringComparison.OrdinalIgnoreCase) || candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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
