using System.Globalization;
using AstroForge.Core.IO;

namespace AstroForge.Core.Analysis;

/// <summary>A picture of the sky centred on (RaDeg, DecDeg), square, north up and east left, FovDeg across.</summary>
public sealed record SkyImage(byte[] Bytes, double RaDeg, double DecDeg, double FovDeg);

/// <summary>
/// Fetches the real sky around a field from the CDS (Strasbourg) as a DSS2 colour picture, so the sensor's footprint can be drawn where the data
/// were really taken. Only the position and the size of the field leave the computer; nothing about the project, the files or the person.
/// Pictures are kept on disk, so a field already seen opens offline and instantly.
/// </summary>
public sealed class SkyImageClient
{
    public const string Survey = "CDS/P/DSS2/color";
    public const string Credit = "DSS2 · CDS";
    public const string OfflineVariable = "ASTROFORGE_OFFLINE";
    private const string Endpoint = "https://alasky.cds.unistra.fr/hips-image-services/hips2fits";
    private const int KeptPictures = 60;

    private readonly HttpClient _http;
    private readonly string _cacheFolder;
    private readonly bool? _offline;

    /// <param name="offline">Forces the answer to "stay off the network" for this client; by default the environment decides (<see cref="Offline"/>).</param>
    public SkyImageClient(HttpClient? http = null, string? cacheFolder = null, bool? offline = null)
    {
        _offline = offline;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _cacheFolder = cacheFolder ?? AppDataPaths.Combine("sky-cache");
    }

    /// <summary>True when the environment says to stay off the network (tests, screenshots, a closed network): only pictures already on disk are used.</summary>
    public static bool Offline => Environment.GetEnvironmentVariable(OfflineVariable) is "1" or "true" or "TRUE" or "True";

    /// <summary>
    /// The centre and size actually asked for: the centre snapped to 1.2′ and the size up to the next quarter degree (never beyond 60°), so a field seen again
    /// by a few arc-seconds of difference finds the same picture, and a bigger picture always covers the smaller one that was wanted.
    /// </summary>
    public static (double RaDeg, double DecDeg, double FovDeg) Snap(double raDeg, double decDeg, double fovDeg) =>
        (Math.Round(((raDeg % 360) + 360) % 360 / 0.02) * 0.02, Math.Clamp(Math.Round(decDeg / 0.02) * 0.02, -90, 90), Math.Clamp(Math.Ceiling(fovDeg * 4) / 4, 0.25, 60));

    public static string BuildUrl(double raDeg, double decDeg, double fovDeg, int sizePx) => string.Create(CultureInfo.InvariantCulture,
        $"{Endpoint}?hips={Uri.EscapeDataString(Survey)}&width={sizePx}&height={sizePx}&fov={fovDeg:0.##}&projection=TAN&coordsys=icrs&rotation_angle=0.0&ra={raDeg:0.####}&dec={decDeg:0.####}&format=jpg");

    public async Task<SkyImage?> GetAsync(double raDeg, double decDeg, double fovDeg, int sizePx = 1280, CancellationToken cancellationToken = default)
    {
        var (ra, dec, fov) = Snap(raDeg, decDeg, fovDeg);
        var file = Path.Combine(_cacheFolder, string.Create(CultureInfo.InvariantCulture, $"dss2-{ra:0.00}_{dec:0.00}_{fov:0.00}_{sizePx}.jpg"));
        try
        {
            if (File.Exists(file) && await File.ReadAllBytesAsync(file, cancellationToken) is { Length: > 0 } cached && LooksLikeImage(cached))
            {
                File.SetLastWriteTimeUtc(file, DateTime.UtcNow);
                return new SkyImage(cached, ra, dec, fov);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { /* an unreadable cache is a cache miss */ }

        if (_offline ?? Offline) return null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl(ra, dec, fov, sizePx));
            request.Headers.UserAgent.ParseAdd("AstroProjectForge");
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (!LooksLikeImage(bytes)) return null;
            Save(file, bytes);
            return new SkyImage(bytes, ra, dec, fov);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            return null;
        }
    }

    private void Save(string file, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(_cacheFolder);
            File.WriteAllBytes(file, bytes);
            foreach (var stale in new DirectoryInfo(_cacheFolder).GetFiles("dss2-*.jpg").OrderByDescending(item => item.LastWriteTimeUtc).Skip(KeptPictures))
                stale.Delete();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { /* the picture is shown anyway, it just is not kept */ }
    }

    // JPEG or PNG; an error page is text, not a picture: never show one, never keep one.
    private static bool LooksLikeImage(byte[] bytes) => bytes.Length > 1000 && (bytes[0] == 0xFF && bytes[1] == 0xD8 || bytes[0] == 0x89 && bytes[1] == 0x50);
}
