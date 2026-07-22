using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using AstroForge.Core.Models;
using AstroForge.Core.IO;

namespace AstroForge.Core.Export;

public sealed record MasterOrganizationMetadata(string Camera, double Gain, double Offset, double? TemperatureC, double? ExposureSeconds, string ReadoutMode);
public sealed record MasterOrganizationRequest(FrameMetadata Source, MasterOrganizationMetadata Metadata);
public sealed record MasterOrganizationResult(string SourcePath, string DestinationPath, string SourceSha256, string DestinationSha256, bool HeaderStamped);
public enum MasterOrganizationPlanStatus { Ready, DuplicateDestination, ExistingFile }
public sealed record MasterOrganizationPlanItem(MasterOrganizationRequest Request, string DestinationPath, MasterOrganizationPlanStatus Status, string Message);

public static class MasterLibraryOrganizer
{
    public static IReadOnlyList<string> Missing(FrameMetadata frame)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(frame.Camera.Value)) missing.Add("Camera");
        if (frame.Gain.Value is null) missing.Add("Gain");
        if (frame.Offset.Value is null) missing.Add("Offset");
        if (frame.Kind == FrameKind.Dark && frame.SetTemperatureC.Value is null) missing.Add("Temperatura");
        if (frame.Kind == FrameKind.Dark && frame.ExposureSeconds.Value is null) missing.Add("Esposizione");
        return missing;
    }

    public static string RelativePath(FrameMetadata frame, MasterOrganizationMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.Camera))
            throw new InvalidOperationException("La camera è obbligatoria: ogni libreria Master deve iniziare dalla camera di acquisizione.");
        var role = frame.Kind switch { FrameKind.Dark => "Dark", FrameKind.Bias => "Bias", FrameKind.DarkFlat => "DarkFlat", _ => "Other" };
        var temperature = metadata.TemperatureC is null ? "Temp-NA" : $"Temp-{Token(metadata.TemperatureC.Value)}C";
        var exposure = metadata.ExposureSeconds is null ? "" : $"_{Token(metadata.ExposureSeconds.Value)}s";
        var extension = Path.GetExtension(frame.Path).ToLowerInvariant();
        var filename = $"master{role}_G{Token(metadata.Gain)}_O{Token(metadata.Offset)}_{temperature}{exposure}{extension}";
        return Path.Combine($"Camera-{Safe(metadata.Camera)}", $"Gain-{Token(metadata.Gain)}", $"Offset-{Token(metadata.Offset)}", temperature, role, filename);
    }

    public static Task<IReadOnlyList<MasterOrganizationPlanItem>> PlanAsync(IEnumerable<MasterOrganizationRequest> requests, string destinationRoot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = SafeRoot(destinationRoot);
        var values = requests.Select(request =>
        {
            var source = Path.GetFullPath(request.Source.Path);
            var destination = Path.GetFullPath(Path.Combine(root, RelativePath(request.Source, request.Metadata)));
            if (!PathIdentity.IsWithin(destination, root)) throw new InvalidOperationException("Percorso Master non sicuro.");
            if (PathIdentity.IsWithin(source, root)) throw new InvalidOperationException("La destinazione non può contenere i Master sorgente.");
            return (Request: request, Destination: destination);
        }).ToArray();
        var duplicates = values.GroupBy(value => value.Destination, PathIdentity.Comparer).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(PathIdentity.Comparer);
        IReadOnlyList<MasterOrganizationPlanItem> plan = values.Select(value =>
            duplicates.Contains(value.Destination)
                ? new MasterOrganizationPlanItem(value.Request, value.Destination, MasterOrganizationPlanStatus.DuplicateDestination, "Più Master generano la stessa destinazione")
                : File.Exists(value.Destination)
                    ? new MasterOrganizationPlanItem(value.Request, value.Destination, MasterOrganizationPlanStatus.ExistingFile, "Esiste già un file nella destinazione")
                    : new MasterOrganizationPlanItem(value.Request, value.Destination, MasterOrganizationPlanStatus.Ready, "Pronto per la copia")).ToArray();
        return Task.FromResult(plan);
    }

    public static async Task<IReadOnlyList<MasterOrganizationResult>> ExecuteAsync(IEnumerable<MasterOrganizationRequest> requests, string destinationRoot, CancellationToken cancellationToken = default)
    {
        var plan = await PlanAsync(requests, destinationRoot, cancellationToken);
        var conflicts = plan.Where(item => item.Status != MasterOrganizationPlanStatus.Ready).ToArray();
        if (conflicts.Length > 0) throw new IOException($"Preflight non superato: {conflicts.Length} conflitti. {conflicts[0].Request.Source.FileName}: {conflicts[0].Message}.");
        var results = new List<MasterOrganizationResult>();
        var created = new List<string>();
        Directory.CreateDirectory(destinationRoot);
        try
        {
            foreach (var item in plan)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = Path.GetFullPath(item.Request.Source.Path);
                var destination = item.DestinationPath;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var sourceHash = await HashAsync(source, cancellationToken);
                await CopyAsync(source, destination, cancellationToken);
                created.Add(destination);
                var stamped = await MasterHeaderStamper.StampAsync(destination, item.Request.Source.Kind, item.Request.Metadata, cancellationToken);
                if (!stamped) throw new InvalidDataException($"Header non aggiornabile in sicurezza: {item.Request.Source.FileName}");
                var destinationHash = await HashAsync(destination, cancellationToken);
                if (!sourceHash.Equals(await HashAsync(source, cancellationToken), StringComparison.OrdinalIgnoreCase)) throw new IOException("Il Master originale è cambiato durante l'organizzazione.");
                results.Add(new(source, destination, sourceHash, destinationHash, stamped));
            }
        }
        catch
        {
            foreach (var path in created.Where(File.Exists)) File.Delete(path);
            DeleteEmptyDirectories(destinationRoot);
            throw;
        }
        var manifest = Path.Combine(destinationRoot, "astroforge-master-library.json");
        var temporaryManifest = manifest + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryManifest, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            File.Move(temporaryManifest, manifest, true);
        }
        catch
        {
            if (File.Exists(temporaryManifest)) File.Delete(temporaryManifest);
            foreach (var path in created.Where(File.Exists)) File.Delete(path);
            DeleteEmptyDirectories(destinationRoot);
            throw;
        }
        return results;
    }

    public static async Task<int> RollbackAsync(string destinationRoot, CancellationToken cancellationToken = default)
    {
        var root = SafeRoot(destinationRoot);
        var manifest = Path.Combine(root, "astroforge-master-library.json");
        if (!File.Exists(manifest)) throw new FileNotFoundException("Nessun batch AstroProject Forge da annullare.", manifest);
        var results = JsonSerializer.Deserialize<List<MasterOrganizationResult>>(await File.ReadAllTextAsync(manifest, cancellationToken)) ?? [];
        foreach (var result in results)
        {
            var destination = Path.GetFullPath(result.DestinationPath);
            if (!PathIdentity.IsWithin(destination, root)) throw new InvalidDataException("Il manifest contiene un percorso esterno alla libreria.");
            if (!File.Exists(destination)) throw new IOException($"Rollback interrotto: file mancante {destination}");
            if (!result.DestinationSha256.Equals(await HashAsync(destination, cancellationToken), StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Rollback interrotto: il file è stato modificato dopo il batch: {destination}");
        }
        foreach (var result in results) File.Delete(result.DestinationPath);
        File.Delete(manifest);
        DeleteEmptyDirectories(destinationRoot);
        return results.Count;
    }

    private static string SafeRoot(string destinationRoot) => Path.GetFullPath(destinationRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    private static void DeleteEmptyDirectories(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }

    private static async Task CopyAsync(string source, string destination, CancellationToken token)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, token); await output.FlushAsync(token);
    }
    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
    }
    private static string Safe(string value) => string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
    private static string Token(double value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture).Replace('-', 'm').Replace('.', 'p');
}

internal static class MasterHeaderStamper
{
    public static async Task<bool> StampAsync(string path, FrameKind kind, MasterOrganizationMetadata metadata, CancellationToken token)
    {
        if (path.EndsWith(".xisf", StringComparison.OrdinalIgnoreCase)) return await StampXisfAsync(path, kind, metadata, token);
        if (new[] { ".fit", ".fits", ".fts" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) return await StampFitsAsync(path, kind, metadata, token);
        return false;
    }

    private static Dictionary<string, string> Values(FrameKind kind, MasterOrganizationMetadata value) => new()
    {
        ["IMAGETYP"] = $"'Master {kind}'", ["INSTRUME"] = $"'{value.Camera.Replace("'", "''")}'", ["GAIN"] = Number(value.Gain), ["OFFSET"] = Number(value.Offset),
        ["SET-TEMP"] = value.TemperatureC is null ? "" : Number(value.TemperatureC.Value), ["EXPTIME"] = value.ExposureSeconds is null ? "" : Number(value.ExposureSeconds.Value), ["READOUTM"] = $"'{value.ReadoutMode.Replace("'", "''")}'"
    };
    private static string Number(double value) => value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<bool> StampFitsAsync(string path, FrameKind kind, MasterOrganizationMetadata metadata, CancellationToken token)
    {
        var block = new byte[2880];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 2880, FileOptions.Asynchronous);
        for (var blockIndex = 0; blockIndex < 128; blockIndex++)
        {
            await stream.ReadExactlyAsync(block, token);
            var cards = Enumerable.Range(0, 36).Select(index => Encoding.ASCII.GetString(block, index * 80, 80)).ToArray();
            var end = Array.FindIndex(cards, card => card[..8].Trim() == "END");
            if (end < 0) continue;
            var values = Values(kind, metadata).Where(pair => pair.Value.Length > 0).ToArray();
            foreach (var pair in values)
            {
                var existing = Array.FindIndex(cards, card => card[..8].Trim().Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0) cards[existing] = Card(pair.Key, pair.Value);
                else { if (end >= cards.Length - 1) return false; cards[end++] = Card(pair.Key, pair.Value); cards[end] = "END".PadRight(80); }
            }
            for (var index = 0; index < cards.Length; index++) Encoding.ASCII.GetBytes(cards[index].PadRight(80)[..80]).CopyTo(block, index * 80);
            stream.Position -= 2880; await stream.WriteAsync(block, token); await stream.FlushAsync(token); return true;
        }
        return false;
    }
    private static string Card(string key, string value) => $"{key.PadRight(8)}= {value}".PadRight(80)[..80];

    private static async Task<bool> StampXisfAsync(string path, FrameKind kind, MasterOrganizationMetadata metadata, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous);
        var preamble = new byte[16]; await stream.ReadExactlyAsync(preamble, token);
        if (!preamble.AsSpan(0, 8).SequenceEqual("XISF0100"u8)) return false;
        var length = BinaryPrimitives.ReadUInt32LittleEndian(preamble.AsSpan(8, 4));
        var original = new byte[length]; await stream.ReadExactlyAsync(original, token);
        var document = XDocument.Parse(Encoding.UTF8.GetString(original).TrimEnd('\0', ' ', '\r', '\n'));
        var image = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Image"); if (image is null) return false;
        foreach (var pair in Values(kind, metadata).Where(pair => pair.Value.Length > 0))
        {
            var existing = image.Descendants().FirstOrDefault(element => element.Name.LocalName == "FITSKeyword" && string.Equals((string?)element.Attribute("name"), pair.Key, StringComparison.OrdinalIgnoreCase));
            if (existing is null) image.Add(new XElement(image.Name.Namespace + "FITSKeyword", new XAttribute("name", pair.Key), new XAttribute("value", pair.Value)));
            else existing.SetAttributeValue("value", pair.Value);
        }
        var encoded = Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting)); if (encoded.Length > length) return false;
        var output = Enumerable.Repeat((byte)' ', (int)length).ToArray(); encoded.CopyTo(output, 0); stream.Position = 16; await stream.WriteAsync(output, token); await stream.FlushAsync(token); return true;
    }
}
