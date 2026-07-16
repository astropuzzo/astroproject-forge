using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace AstroForge.Core.Diagnostics;

public sealed record SupportIssueSummary(string Code, string Severity, int Count);
public sealed record SupportBundleRequest(
    string OutputPath,
    string ApplicationVersion,
    IReadOnlyDictionary<string, object?> Settings,
    IReadOnlyDictionary<string, object?> Diagnostics,
    IReadOnlyList<SupportIssueSummary> Issues,
    IReadOnlyList<string> LogFiles);
public sealed record SupportBundleResult(string Path, IReadOnlyList<string> Entries);

public static class SupportBundleBuilder
{
    public static readonly IReadOnlyList<string> PreviewEntries =
    [
        "README.txt — contenuto e limiti privacy",
        "system.json — versione app, Windows e runtime",
        "settings.json — sole impostazioni tecniche non sensibili",
        "diagnostics.json — conteggi, mai nomi o percorsi dei file",
        "issues.json — codici, severità e quantità",
        "logs/*.jsonl — eventi applicativi strutturati senza dati astronomici"
    ];

    public static async Task<SupportBundleResult> BuildAsync(SupportBundleRequest request, CancellationToken cancellationToken = default)
    {
        var output = Path.GetFullPath(request.OutputPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? request.OutputPath : request.OutputPath + ".zip");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var temporary = output + ".tmp";
        if (File.Exists(temporary)) File.Delete(temporary);
        var entries = new List<string>();
        try
        {
            await using var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
            await AddTextAsync(archive, "README.txt", "Pacchetto diagnostico AstroProject Forge\n\nCreato localmente. Non contiene FITS, XISF, pixel astronomici, nomi target, coordinate celesti, nomi file, percorsi sorgente o Master Library, né header grezzi. L'invio a terzi non è automatico.", entries, cancellationToken);
            await AddJsonAsync(archive, "system.json", new { request.ApplicationVersion, OperatingSystem = Environment.OSVersion.VersionString, Runtime = Environment.Version.ToString(), Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), GeneratedAtUtc = DateTimeOffset.UtcNow }, entries, cancellationToken);
            await AddJsonAsync(archive, "settings.json", request.Settings, entries, cancellationToken);
            await AddJsonAsync(archive, "diagnostics.json", request.Diagnostics, entries, cancellationToken);
            await AddJsonAsync(archive, "issues.json", request.Issues, entries, cancellationToken);
            var logIndex = 0;
            foreach (var path in request.LogFiles.Where(File.Exists).TakeLast(5))
            {
                var content = await File.ReadAllTextAsync(path, cancellationToken);
                await AddTextAsync(archive, $"logs/events-{++logIndex:00}.jsonl", content, entries, cancellationToken);
            }
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
        File.Move(temporary, output, true);
        return new(output, entries);
    }

    private static async Task AddJsonAsync(ZipArchive archive, string name, object value, List<string> entries, CancellationToken token) =>
        await AddTextAsync(archive, name, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }), entries, token);

    private static async Task AddTextAsync(ZipArchive archive, string name, string value, List<string> entries, CancellationToken token)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var output = entry.Open();
        await using var writer = new StreamWriter(output, new UTF8Encoding(false), leaveOpen: false);
        await writer.WriteAsync(value.AsMemory(), token);
        entries.Add(name);
    }
}
