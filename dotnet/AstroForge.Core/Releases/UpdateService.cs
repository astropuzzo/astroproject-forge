using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace AstroForge.Core.Releases;

public enum ReleaseChannel { Stable, Beta }

public sealed record ReleaseArtifact(string Url, string Sha256, long SizeBytes, string FileName);

public sealed record ReleaseManifest(
    int Schema,
    string Product,
    string Channel,
    string Version,
    DateTimeOffset PublishedAtUtc,
    ReleaseArtifact Installer,
    string? ReleaseNotesUrl = null,
    string? MinimumWindowsVersion = null,
    bool Signed = false);

public sealed record UpdateDecision(bool IsAvailable, ReleaseManifest Manifest, string Reason);

public sealed partial class UpdateService
{
    private readonly HttpClient _http;
    private readonly Func<string, bool> _authenticodeVerifier;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public UpdateService(HttpClient? httpClient = null, Func<string, bool>? authenticodeVerifier = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _authenticodeVerifier = authenticodeVerifier ?? AuthenticodeVerifier.Verify;
    }

    public static Uri FeedUri(ReleaseChannel channel) => new(
        $"https://github.com/astropuzzo/astroproject-forge/releases/download/channel-{channel.ToString().ToLowerInvariant()}/{channel.ToString().ToLowerInvariant()}.json");

    public async Task<UpdateDecision> CheckAsync(Uri feed, string currentVersion, ReleaseChannel expectedChannel, CancellationToken cancellationToken = default)
    {
        RequireHttps(feed, "feed");
        using var response = await _http.GetAsync(feed, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var manifest = await response.Content.ReadFromJsonAsync<ReleaseManifest>(Json, cancellationToken)
            ?? throw new InvalidDataException("Manifest aggiornamento vuoto.");
        Validate(manifest, expectedChannel);
        var comparison = CompareVersions(manifest.Version, currentVersion);
        return comparison > 0
            ? new(true, manifest, $"È disponibile AstroProject Forge {manifest.Version}.")
            : new(false, manifest, comparison == 0 ? "La versione installata è aggiornata." : "La versione installata è più recente del canale selezionato.");
    }

    public async Task<string> DownloadVerifiedAsync(ReleaseArtifact artifact, string destinationPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var uri = new Uri(artifact.Url);
        RequireHttps(uri, "installer");
        ValidateArtifact(artifact);
        var fullDestination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
        var partial = fullDestination + ".partial";
        if (File.Exists(partial)) File.Delete(partial);
        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            long total = 0;
            string hash;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[1024 * 1024];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hasher.AppendData(buffer, 0, read);
                    total += read;
                    if (artifact.SizeBytes > 0) progress?.Report(Math.Min(100, total * 100d / artifact.SizeBytes));
                }
                await output.FlushAsync(cancellationToken);
                hash = Convert.ToHexString(hasher.GetHashAndReset());
            }
            if (artifact.SizeBytes > 0 && total != artifact.SizeBytes) throw new InvalidDataException($"Dimensione non valida: attesi {artifact.SizeBytes} byte, ricevuti {total}.");
            if (!hash.Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase)) throw new CryptographicException("SHA-256 dell'aggiornamento non valido.");
            if (!_authenticodeVerifier(partial)) throw new CryptographicException("Firma Authenticode dell'aggiornamento assente o non valida.");
            File.Move(partial, fullDestination, true);
            progress?.Report(100);
            return fullDestination;
        }
        catch
        {
            if (File.Exists(partial)) File.Delete(partial);
            throw;
        }
    }

    public static void Validate(ReleaseManifest manifest, ReleaseChannel expectedChannel)
    {
        if (manifest.Schema != 1) throw new InvalidDataException($"Schema update non supportato: {manifest.Schema}.");
        if (!manifest.Product.Equals("AstroProject Forge", StringComparison.Ordinal)) throw new InvalidDataException("Prodotto del manifest non valido.");
        if (!Enum.TryParse<ReleaseChannel>(manifest.Channel, true, out var channel) || channel != expectedChannel) throw new InvalidDataException("Canale del manifest inatteso.");
        _ = ParseVersion(manifest.Version);
        if (!manifest.Signed) throw new InvalidDataException("Il canale ha pubblicato un aggiornamento non firmato.");
        ValidateArtifact(manifest.Installer);
        if (manifest.ReleaseNotesUrl is { Length: > 0 }) RequireHttps(new Uri(manifest.ReleaseNotesUrl), "release notes");
    }

    public static int CompareVersions(string left, string right)
    {
        var a = ParseVersion(left); var b = ParseVersion(right);
        for (var i = 0; i < 3; i++) { var value = a.Numbers[i].CompareTo(b.Numbers[i]); if (value != 0) return value; }
        if (a.PreRelease is null && b.PreRelease is not null) return 1;
        if (a.PreRelease is not null && b.PreRelease is null) return -1;
        return ComparePreRelease(a.PreRelease, b.PreRelease);
    }

    private static int ComparePreRelease(string? left, string? right)
    {
        if (left is null || right is null) return 0;
        var a = left.Split('.'); var b = right.Split('.');
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            if (i == a.Length) return -1; if (i == b.Length) return 1;
            var an = int.TryParse(a[i], out var ai); var bn = int.TryParse(b[i], out var bi);
            var value = an && bn ? ai.CompareTo(bi) : an ? -1 : bn ? 1 : string.CompareOrdinal(a[i], b[i]);
            if (value != 0) return value;
        }
        return 0;
    }

    private static (int[] Numbers, string? PreRelease) ParseVersion(string value)
    {
        var match = SemVerRegex().Match(value);
        if (!match.Success) throw new InvalidDataException($"Versione SemVer non valida: {value}.");
        return ([int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value)], match.Groups[4].Success ? match.Groups[4].Value : null);
    }

    private static void ValidateArtifact(ReleaseArtifact artifact)
    {
        RequireHttps(new Uri(artifact.Url), "installer");
        if (!ShaRegex().IsMatch(artifact.Sha256)) throw new InvalidDataException("SHA-256 dell'artefatto non valido.");
        if (artifact.SizeBytes < 0) throw new InvalidDataException("Dimensione artefatto non valida.");
        if (string.IsNullOrWhiteSpace(artifact.FileName) || Path.GetFileName(artifact.FileName) != artifact.FileName) throw new InvalidDataException("Nome artefatto non valido.");
    }

    private static void RequireHttps(Uri uri, string role)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException($"L'URL {role} deve usare HTTPS.");
    }

    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z.-]+)?$")]
    private static partial Regex SemVerRegex();
    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
    private static partial Regex ShaRegex();
}

internal static class AuthenticodeVerifier
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public static bool Verify(string path)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path)) return false;
        var filePath = Marshal.StringToCoTaskMemUni(Path.GetFullPath(path));
        var fileInfo = new WinTrustFileInfo { Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(), FilePath = filePath };
        var fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        var dataPointer = IntPtr.Zero;
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
            var data = new WinTrustData
            {
                Size = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = 2,
                RevocationChecks = 0,
                UnionChoice = 1,
                FileInfo = fileInfoPointer,
                StateAction = 0,
                ProviderFlags = 0,
                UiContext = 0
            };
            dataPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(data, dataPointer, false);
            return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, dataPointer) == 0;
        }
        finally
        {
            if (dataPointer != IntPtr.Zero) Marshal.FreeHGlobal(dataPointer);
            Marshal.FreeHGlobal(fileInfoPointer);
            Marshal.FreeCoTaskMem(filePath);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(IntPtr window, [MarshalAs(UnmanagedType.LPStruct)] Guid action, IntPtr data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint Size;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }
}
