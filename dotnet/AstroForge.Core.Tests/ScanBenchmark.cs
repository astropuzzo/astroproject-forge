using System.Diagnostics;
using System.Globalization;
using System.Text;
using AstroForge.Core.Analysis;
using AstroForge.Core.Models;
using AstroForge.Core.Scanning;
using AstroForge.Core.Sessions;

/// <summary>
/// Folder analysis benchmark: writes a realistic project of FITS files (ASIAIR and N.I.N.A. names, night and filter
/// folders, raw flats per filter and a master library) and times the scan reading every header, the scan served by the header cache and the
/// calibration analysis. The regression suite runs a small instance to check the results; <c>--benchmark</c> runs the
/// full one and prints the table.
/// </summary>
internal static class ScanBenchmark
{
    public sealed record Scale(int Lights, int FlatsPerFilter, int Rounds);

    public static readonly Scale Smoke = new(240, 10, 1);
    public static readonly Scale Full = new(4000, 30, 9);

    // A master library of the usual shape: dark per gain, setpoint and exposure, bias per gain.
    private static readonly int[] LibraryGains = [0, 100, 200];
    private static readonly int[] LibraryTemperatures = [-20, -10, 0];
    private static readonly int[] LibraryExposures = [120, 300];

    private static readonly string[] Filters = ["Ha", "OIII", "SII", "L"];

    public static async Task RunAsync(Scale scale, bool print)
    {
        var root = Path.Combine(Path.GetTempPath(), $"AstroForge-Bench-{Guid.NewGuid():N}");
        try
        {
            var generated = Generate(root, scale);
            var settings = new SessionSettings(TimeZoneInfo.Utc, new TimeOnly(12, 0));
            var scanner = new ProjectScanner();
            var cold = new List<double>();
            var warm = new List<double>();
            var analyze = new List<double>();
            IReadOnlyList<FrameMetadata> frames = [];
            ProjectAnalysis? analysis = null;
            // Round 0 warms up JIT and the OS file cache and is not measured.
            for (var round = 0; round <= scale.Rounds; round++)
            {
                if (round == 1) { cold.Clear(); warm.Clear(); analyze.Clear(); }
                var watch = Stopwatch.StartNew();
                frames = await scanner.ScanAsync([root], settings);
                cold.Add(watch.Elapsed.TotalMilliseconds);
                Check(scanner.LastParsedFiles == generated && frames.Count == generated, $"Scansione a freddo incompleta: {frames.Count}/{generated}.");

                var cache = new MemoryHeaderCache();
                await scanner.ScanAsync([root], settings, cache: cache);
                watch.Restart();
                frames = await scanner.ScanAsync([root], settings, cache: cache);
                warm.Add(watch.Elapsed.TotalMilliseconds);
                Check(scanner.LastCacheHits == generated, $"Scansione da cache incompleta: {scanner.LastCacheHits}/{generated}.");

                watch.Restart();
                analysis = ProjectAnalyzer.Analyze(frames);
                analyze.Add(watch.Elapsed.TotalMilliseconds);
            }

            Check(frames.All(frame => frame.Kind != FrameKind.Unknown && !frame.Issues.Any(issue => issue.Severity == IssueSeverity.Error)), "Il benchmark ha prodotto frame non classificati o illeggibili.");
            Check(analysis is { Ready: true } && analysis.Lights.Count == scale.Lights, $"Analisi benchmark non pronta: {analysis?.UnresolvedCount} casi irrisolti.");
            Check(analysis!.FlatGroups.Count == Filters.Length, $"Attesi {Filters.Length} Flat Set, trovati {analysis.FlatGroups.Count}.");
            Check(analysis.Lights.All(item => item.Flat.Selected?.Frame.FilterName.Value == item.Light.FilterName.Value), "Flat assegnato a un filtro diverso.");
            Check(analysis.Lights.All(item => item.Dark.Selected?.Frame is { IsMaster: true } dark && dark.ExposureSeconds.Value == item.Light.ExposureSeconds.Value
                && dark.Gain.Value == 100 && dark.SetTemperatureC.Value == -10), "Master Dark assegnato con esposizione, gain o temperatura diversi.");
            Check(analysis.Lights.All(item => item.Bias.Selected?.Frame.Gain.Value == 100), "Master Bias assegnato con gain diverso.");

            if (!print) return;
            Console.WriteLine($"BENCHMARK analisi cartelle: {generated} file FITS ({scale.Lights} light, {scale.FlatsPerFilter * Filters.Length} flat, {generated - scale.Lights - scale.FlatsPerFilter * Filters.Length} master), {scale.Rounds} giri, {Environment.ProcessorCount} core");
            Console.WriteLine($"  scansione, lettura header  {Summary(cold)}");
            Console.WriteLine($"  scansione, cache header    {Summary(warm)}");
            Console.WriteLine($"  analisi calibrazioni       {Summary(analyze)}");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static string Summary(List<double> samples)
    {
        var ordered = samples.Order().ToArray();
        return $"mediana {ordered[ordered.Length / 2],8:0.0} ms · min {ordered[0],8:0.0} ms";
    }

    private static int Generate(string root, Scale scale)
    {
        var count = 0;
        var start = new DateTimeOffset(2026, 6, 1, 21, 0, 0, TimeSpan.Zero);
        const int nights = 8;
        for (var index = 0; index < scale.Lights; index++)
        {
            var night = index % nights;
            var filter = Filters[index / nights % Filters.Length];
            var exposure = filter == "L" ? 120 : 300;
            var captured = start.AddDays(night).AddSeconds(index / nights * 37);
            // Half the nights come from an ASIAIR, half from N.I.N.A.: both naming schemes go through the file name parser.
            var name = night % 2 == 0
                ? $"Light_M31_{exposure}.0s_Bin1_{filter}_gain100_{captured:yyyyMMdd-HHmmss}_-10.0C_{index:0000}.fit"
                : $"{captured:yyyy-MM-dd_HH-mm-ss}_{filter}_-10.00_{exposure}.00s_{index:0000}.fits";
            Write(Path.Combine(root, "Lights", $"{captured.AddHours(-12):yyyy-MM-dd}", filter, name), "Light", filter, exposure, captured);
            count++;
        }
        for (var f = 0; f < Filters.Length; f++)
        {
            for (var index = 0; index < scale.FlatsPerFilter; index++)
            {
                var captured = start.AddDays(nights).AddHours(f).AddSeconds(index * 5);
                Write(Path.Combine(root, "Flats", Filters[f], $"Flat_{Filters[f]}_{index:0000}.fits"), "Flat", Filters[f], 2.5, captured);
                count++;
            }
        }
        foreach (var gain in LibraryGains)
        {
            foreach (var temperature in LibraryTemperatures)
                foreach (var exposure in LibraryExposures)
                {
                    Write(Path.Combine(root, "Library", $"GAIN_{gain}", $"{temperature}", $"MasterDark_{exposure}s.fits"), "Master Dark", null, exposure, start.AddDays(-30), gain, temperature);
                    count++;
                }
            Write(Path.Combine(root, "Library", $"GAIN_{gain}", $"MasterBias_gain{gain}.fits"), "Master Bias", null, 0.0001, start.AddDays(-30), gain, -10);
            count++;
        }
        return count;
    }

    private static void Write(string path, string type, string? filter, double exposure, DateTimeOffset captured, int gain = 100, int temperature = -10)
    {
        var header = new StringBuilder();
        void Card(string key, string value, string comment = "")
        {
            var card = $"{key,-8}= {value,20}" + (comment.Length > 0 ? $" / {comment}" : "");
            header.Append(card.PadRight(80)[..80]);
        }
        void Text(string key, string value, string comment = "") => Card(key, $"'{value.Replace("'", "''"),-8}'".PadRight(20), comment);
        // A capture-software header of realistic size: three FITS blocks with comments, HISTORY and vendor keywords.
        Card("SIMPLE", "T", "C# FITS");
        Card("BITPIX", "16", "Image data type");
        Card("NAXIS", "2", "Number of axes");
        Card("NAXIS1", "6248", "Length of axis 1");
        Card("NAXIS2", "4176", "Length of axis 2");
        Card("BZERO", "32768", "offset data range to that of unsigned short");
        Card("BSCALE", "1", "default scaling factor");
        Text("IMAGETYP", type, "Type of exposure");
        Card("EXPOSURE", exposure.ToString("0.0###", CultureInfo.InvariantCulture), "[s] Exposure duration");
        Card("EXPTIME", exposure.ToString("0.0###", CultureInfo.InvariantCulture), "[s] Exposure duration");
        Text("DATE-LOC", captured.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture), "Time of observation (local)");
        Text("DATE-OBS", captured.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture), "Time of observation (UTC)");
        Card("XBINNING", "1", "X axis binning factor");
        Card("YBINNING", "1", "Y axis binning factor");
        Card("GAIN", gain.ToString(CultureInfo.InvariantCulture), "Sensor gain");
        Card("OFFSET", "50", "Sensor gain offset");
        Card("EGAIN", "0.25", "[e-/ADU] Electrons per A/D unit");
        Card("XPIXSZ", "3.76", "[um] Pixel X axis size");
        Card("YPIXSZ", "3.76", "[um] Pixel Y axis size");
        Text("INSTRUME", "ZWO ASI2600MM Pro", "Imaging instrument name");
        Card("SET-TEMP", temperature.ToString(CultureInfo.InvariantCulture), "[degC] CCD temperature setpoint");
        Card("CCD-TEMP", (temperature + 0.2).ToString(CultureInfo.InvariantCulture), "[degC] CCD temperature");
        Text("READOUTM", "Normal", "Sensor readout mode");
        Card("USBLIMIT", "40", "Camera-specific USB setting");
        Text("TELESCOP", "Askar FRA400", "Name of telescope");
        Card("FOCALLEN", "400", "[mm] Focal length");
        Card("FOCRATIO", "5.6", "Focal ratio");
        Card("RA", "10.684708", "[deg] RA of telescope");
        Card("DEC", "41.268750", "[deg] Declination of telescope");
        Text("OBJCTRA", "00 42 44", "[H M S] RA of imaging target");
        Text("OBJCTDEC", "+41 16 07", "[D M S] Declination of imaging target");
        Card("ROTATOR", "12.5", "[deg] Mechanical rotator angle");
        Card("ROTATANG", "12.5", "[deg] Mechanical rotator angle");
        Text("OBJECT", "M31", "Name of the object of interest");
        if (filter is not null) Text("FILTER", filter, "Active filter name");
        Text("FWHEEL", "ZWO EFW", "Filter Wheel name");
        Card("FOCPOS", "18234", "[step] Focuser position");
        Card("FOCTEMP", "11.4", "[degC] Focuser temperature");
        Card("AIRMASS", "1.2034", "Airmass at frame center");
        Card("SITELAT", "45.46", "[deg] Observation site latitude");
        Card("SITELONG", "9.19", "[deg] Observation site longitude");
        Card("SITEELEV", "120", "[m] Observation site elevation");
        Text("SWCREATE", "N.I.N.A. 3.1.2.9001", "Software that created this file");
        header.Append("COMMENT FITS (Flexible Image Transport System) format is defined in 'Astronomy".PadRight(80));
        header.Append("COMMENT and Astrophysics', volume 376, page 359; bibcode: 2001A&A...376..359H".PadRight(80));
        for (var line = 0; line < 30; line++) header.Append($"HISTORY Calibration step {line}: automated capture sequence".PadRight(80));
        header.Append("END".PadRight(80));
        var blocks = (header.Length + 2879) / 2880;
        var bytes = new byte[(blocks + 1) * 2880];
        Encoding.ASCII.GetBytes(header.ToString().PadRight(blocks * 2880), bytes);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
