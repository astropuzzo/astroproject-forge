using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AstroForge.Core.Filters;
using AstroForge.Core.Models;

namespace AstroForge.Core.Demo;

public enum DemoCaptureSoftware { Asiair, Nina, Library }

/// <summary>One file of the demo dataset and what it is meant to show.</summary>
public sealed record DemoFrame(string RelativePath, FrameKind Kind, DemoCaptureSoftware Software, string? FilterName, string? SessionId, bool Cloudy = false);

public sealed record DemoDataset(string Root, IReadOnlyList<DemoFrame> Frames, IReadOnlyDictionary<string, string> WheelProfile)
{
    public string PathOf(DemoFrame frame) => Path.Combine(Root, frame.RelativePath);
}

/// <summary>
/// Writes a small, deterministic Cygnus Loop (NGC 6992) project: two nights with a ZWO ASI2600MM Pro on an Askar FRA400
/// with its 0.7× reducer. The first night comes from a ZWO ASIAIR with its filter wheel (Hα, OIII), the second from
/// N.I.N.A. whose wheel slot for the SII filter is named "Filtro 3". Lights and Flats are raw frames; Dark and Bias
/// are Masters from a calibration library, as WBPP expects. Images are the real sensor downscaled 1/16 so the whole
/// set stays around 6 MB; the file names, folders and headers follow each program's conventions.
/// </summary>
public static class DemoDatasetGenerator
{
    public const string Target = "Cygnus Loop";
    public const string Camera = "ZWO ASI2600MM Pro";
    public const string Telescope = "Askar FRA400";
    public const double FocalLengthMm = 280;
    public const int Width = 384;
    public const int Height = 256;
    public const double LightExposureSeconds = 300;
    public const int Gain = 100;
    public const int Offset = 50;
    public const double SetTemperatureC = -10;
    /// <summary>The N.I.N.A. wheel slot name the user never renamed: the SII filter.</summary>
    public const string CustomFilterName = "Filtro 3";
    public const string CustomFilterCatalogId = "ant3-SII";
    public const string AsiairNight = "2026-08-14";
    public const string NinaNight = "2026-08-20";

    private const double Pedestal = 500;
    private static readonly TimeSpan SiteOffset = TimeSpan.FromHours(2);

    public static async Task<DemoDataset> GenerateAsync(string root, CancellationToken cancellationToken = default)
    {
        root = Path.GetFullPath(root);
        Directory.CreateDirectory(root);
        var sky = new Sky(20260814);
        var frames = new List<DemoFrame>();

        // Night 1 · ASIAIR Autorun: Hα then OIII, flats at dawn with the same focus and rotation.
        var asiairStart = new DateTimeOffset(2026, 8, 14, 22, 31, 4, SiteOffset);
        var number = 1;
        foreach (var filter in new[] { "Ha", "OIII" })
        {
            for (var index = 0; index < 4; index++, number++)
            {
                var local = asiairStart.AddSeconds((number - 1) * (LightExposureSeconds + 7));
                var name = string.Create(CultureInfo.InvariantCulture, $"Light_{Target}_{LightExposureSeconds:0.0}s_Bin1_{filter}_gain{Gain}_{Stamp(local)}_{-10.0 + Jitter(number):0.0}C_{number:0000}.fit");
                var frame = new DemoFrame(Path.Combine("ASIAIR", "Autorun", "Light", Target, name), FrameKind.Light, DemoCaptureSoftware.Asiair, filter, AsiairNight);
                var pixels = sky.Light(filter, dither: number, fwhm: 2.6, transparency: 1, dust: 0);
                await WriteFitsAsync(Path.Combine(root, frame.RelativePath), AsiairHeaders("Light", filter, local, LightExposureSeconds, number), pixels, cancellationToken);
                frames.Add(frame);
            }
        }
        var flatStart = new DateTimeOffset(2026, 8, 15, 5, 48, 12, SiteOffset);
        number = 1;
        foreach (var (filter, exposure) in new[] { ("Ha", 3.0), ("OIII", 2.0) })
        {
            for (var index = 0; index < 3; index++, number++)
            {
                var local = flatStart.AddSeconds(number * 9);
                var name = string.Create(CultureInfo.InvariantCulture, $"Flat_{exposure:0.0}s_Bin1_{filter}_gain{Gain}_{Stamp(local)}_{-10.0 + Jitter(number + 40):0.0}C_{number:0000}.fit");
                var frame = new DemoFrame(Path.Combine("ASIAIR", "Autorun", "Flat", name), FrameKind.Flat, DemoCaptureSoftware.Asiair, filter, AsiairNight);
                await WriteFitsAsync(Path.Combine(root, frame.RelativePath), AsiairHeaders("Flat", filter, local, exposure, number + 40), sky.Flat(number, dust: 0), cancellationToken);
                frames.Add(frame);
            }
        }

        // Night 2 · N.I.N.A.: Hα and the slot called "Filtro 3"; a passing cloud spoils one SII frame. The camera
        // was taken off and put back between the nights, so the dust shadows moved and old flats no longer fit.
        var ninaStart = new DateTimeOffset(2026, 8, 20, 22, 18, 40, SiteOffset);
        number = 0;
        foreach (var filter in new[] { "Ha", CustomFilterName })
        {
            for (var index = 0; index < 4; index++, number++)
            {
                var local = ninaStart.AddSeconds(number * (LightExposureSeconds + 11));
                var cloudy = filter == CustomFilterName && index == 2;
                var name = string.Create(CultureInfo.InvariantCulture, $"{local:yyyy-MM-dd_HH-mm-ss}_{filter}_{-10.0 + Jitter(number + 80):0.00}_{LightExposureSeconds:0.00}s_{number:0000}.fits");
                var frame = new DemoFrame(Path.Combine("NINA", Target, NinaNight, "LIGHT", name), FrameKind.Light, DemoCaptureSoftware.Nina, filter, NinaNight, cloudy);
                var pixels = sky.Light(filter == CustomFilterName ? "SII" : filter, dither: 20 + number, fwhm: cloudy ? 4.4 : 3.0, transparency: cloudy ? 0.4 : 1, dust: 1);
                await WriteFitsAsync(Path.Combine(root, frame.RelativePath), NinaHeaders("LIGHT", filter, local, LightExposureSeconds, number + 80), pixels, cancellationToken);
                frames.Add(frame);
            }
        }
        var ninaFlatStart = new DateTimeOffset(2026, 8, 21, 5, 40, 2, SiteOffset);
        number = 0;
        foreach (var (filter, exposure) in new[] { ("Ha", 2.5), (CustomFilterName, 4.0) })
        {
            for (var index = 0; index < 3; index++, number++)
            {
                var local = ninaFlatStart.AddSeconds(number * 8);
                var name = string.Create(CultureInfo.InvariantCulture, $"{local:yyyy-MM-dd_HH-mm-ss}_{filter}_{-10.0 + Jitter(number + 120):0.00}_{exposure:0.00}s_{number:0000}.fits");
                var frame = new DemoFrame(Path.Combine("NINA", Target, NinaNight, "FLAT", name), FrameKind.Flat, DemoCaptureSoftware.Nina, filter, NinaNight);
                await WriteFitsAsync(Path.Combine(root, frame.RelativePath), NinaHeaders("FLAT", filter, local, exposure, number + 120), sky.Flat(20 + number, dust: 1), cancellationToken);
                frames.Add(frame);
            }
        }

        // Calibration library: WBPP-style Masters at the same gain, offset and temperature.
        var darkName = string.Create(CultureInfo.InvariantCulture, $"masterDark_BIN-1_{Width}x{Height}_EXPOSURE-{LightExposureSeconds:0.00}s.fits");
        var dark = new DemoFrame(Path.Combine("Libreria Master", "ASI2600MM_G100_O50_-10C", darkName), FrameKind.Dark, DemoCaptureSoftware.Library, null, null);
        await WriteFitsAsync(Path.Combine(root, dark.RelativePath), MasterHeaders("Master Dark", LightExposureSeconds, 25), sky.MasterDark(LightExposureSeconds), cancellationToken);
        frames.Add(dark);
        var biasName = $"masterBias_BIN-1_{Width}x{Height}.fits";
        var bias = new DemoFrame(Path.Combine("Libreria Master", "ASI2600MM_G100_O50_-10C", biasName), FrameKind.Bias, DemoCaptureSoftware.Library, null, null);
        await WriteFitsAsync(Path.Combine(root, bias.RelativePath), MasterHeaders("Master Bias", 0, 50), sky.MasterBias(), cancellationToken);
        frames.Add(bias);

        var profile = new Dictionary<string, string> { [FilterRecognizer.Normalize(CustomFilterName)] = CustomFilterCatalogId };
        var dataset = new DemoDataset(root, frames, profile);
        await WriteManifestAsync(dataset, cancellationToken);
        return dataset;
    }

    private static string Stamp(DateTimeOffset local) => local.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
    // Cooled sensors settle within a tenth of a degree of the set point.
    private static double Jitter(int seed) => ((seed * 37) % 3 - 1) * 0.1;

    private static List<(string Key, object Value, string? Comment)> AsiairHeaders(string type, string filter, DateTimeOffset local, double exposure, int seed) =>
    [
        ("IMAGETYP", type, "Type of exposure"),
        ("EXPOSURE", exposure, "Exposure time in seconds"),
        ("EXPTIME", exposure, "Exposure time in seconds"),
        ("DATE-OBS", local.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.ffffff", CultureInfo.InvariantCulture), "Exposure start time (UTC)"),
        ("XBINNING", 1, "X axis binning factor"),
        ("YBINNING", 1, "Y axis binning factor"),
        ("GAIN", Gain, "Sensor gain"),
        ("OFFSET", Offset, "Sensor offset"),
        ("CCD-TEMP", SetTemperatureC + Jitter(seed), "Sensor temperature (C)"),
        ("XPIXSZ", 3.76 * 16, "X pixel size in microns (demo: downscaled 1/16)"),
        ("YPIXSZ", 3.76 * 16, "Y pixel size in microns (demo: downscaled 1/16)"),
        ("INSTRUME", Camera, "Camera model"),
        ("FILTER", filter, "Filter name"),
        ("FOCALLEN", FocalLengthMm, "Focal length in mm"),
        ("RA", 314.28, "Telescope RA (degrees)"),
        ("DEC", 31.72, "Telescope DEC (degrees)")
    ];

    private static List<(string Key, object Value, string? Comment)> NinaHeaders(string type, string filter, DateTimeOffset local, double exposure, int seed) =>
    [
        ("IMAGETYP", type, "Type of exposure"),
        ("EXPOSURE", exposure, "[s] Exposure duration"),
        ("EXPTIME", exposure, "[s] Exposure duration"),
        ("DATE-LOC", local.DateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture), "Time of observation (local)"),
        ("DATE-OBS", local.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture), "Time of observation (UTC)"),
        ("XBINNING", 1, "X axis binning factor"),
        ("YBINNING", 1, "Y axis binning factor"),
        ("GAIN", Gain, "Sensor gain"),
        ("OFFSET", Offset, "Sensor gain offset"),
        ("EGAIN", 0.25, "[e-/ADU] Electrons per A/D unit"),
        ("XPIXSZ", 3.76 * 16, "[um] Pixel X axis size (demo: downscaled 1/16)"),
        ("YPIXSZ", 3.76 * 16, "[um] Pixel Y axis size (demo: downscaled 1/16)"),
        ("INSTRUME", Camera, "Imaging instrument name"),
        ("SET-TEMP", SetTemperatureC, "[degC] CCD temperature setpoint"),
        ("CCD-TEMP", SetTemperatureC + Jitter(seed), "[degC] CCD temperature"),
        ("TELESCOP", Telescope, "Name of telescope"),
        ("FOCALLEN", FocalLengthMm, "[mm] Telescope focal length"),
        ("FOCRATIO", 3.9, "Telescope focal ratio"),
        ("FWHEEL", "ZWO Filter Wheel", "Filter Wheel name"),
        ("FILTER", filter, "Active filter name"),
        ("OBJECT", Target, "Name of the object of interest"),
        ("OBJCTRA", "20 56 24", "[H M S] RA of imaged object"),
        ("OBJCTDEC", "+31 43 00", "[D M S] Declination of imaged object"),
        ("SITELAT", 43.7696, "[deg] Observation site latitude"),
        ("SITELONG", 11.2558, "[deg] Observation site longitude"),
        ("ROWORDER", "TOP-DOWN", "FITS Image Orientation"),
        ("SWCREATE", "N.I.N.A. 3.1.2.9001", "Software that created this file")
    ];

    private static List<(string Key, object Value, string? Comment)> MasterHeaders(string type, double exposure, int count) =>
    [
        ("IMAGETYP", type, "Type of image"),
        ("EXPTIME", exposure, "Exposure time in seconds"),
        ("XBINNING", 1, "Binning factor, X-axis"),
        ("YBINNING", 1, "Binning factor, Y-axis"),
        ("GAIN", Gain, "Sensor gain"),
        ("OFFSET", Offset, "Sensor offset"),
        ("SET-TEMP", SetTemperatureC, "CCD temperature setpoint (C)"),
        ("INSTRUME", Camera, "Camera model"),
        ("NCOMBINE", count, "Number of combined frames"),
        ("HISTORY", "ImageIntegration.numberOfImages: " + count, null)
    ];

    private static async Task WriteFitsAsync(string path, List<(string Key, object Value, string? Comment)> headers, Plane plane, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var cards = new StringBuilder();
        cards.Append(Card("SIMPLE", true, "file does conform to FITS standard"));
        cards.Append(Card("BITPIX", plane.Normalized ? -32 : 16, "number of bits per data pixel"));
        cards.Append(Card("NAXIS", 2, "number of data axes"));
        cards.Append(Card("NAXIS1", Width, "length of data axis 1"));
        cards.Append(Card("NAXIS2", Height, "length of data axis 2"));
        cards.Append(Card("EXTEND", true, "FITS dataset may contain extensions"));
        if (!plane.Normalized)
        {
            cards.Append(Card("BZERO", 32768, "offset data range to that of unsigned short"));
            cards.Append(Card("BSCALE", 1, "default scaling factor"));
        }
        foreach (var (key, value, comment) in headers) cards.Append(key == "HISTORY" ? $"HISTORY {value}".PadRight(80)[..80] : Card(key, value, comment));
        cards.Append("END".PadRight(80));
        var header = cards.ToString();
        var headerBytes = Encoding.ASCII.GetBytes(header.PadRight((header.Length + 2879) / 2880 * 2880));

        var dataLength = plane.Pixels.Length * (plane.Normalized ? 4 : 2);
        var data = new byte[(dataLength + 2879) / 2880 * 2880];
        for (var index = 0; index < plane.Pixels.Length; index++)
        {
            if (plane.Normalized) BinaryPrimitives.WriteSingleBigEndian(data.AsSpan(index * 4), (float)Math.Clamp(plane.Pixels[index], 0, 1));
            else BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(index * 2), (short)(Math.Clamp(Math.Round(plane.Pixels[index]), 0, 65535) - 32768));
        }
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous);
        await stream.WriteAsync(headerBytes, cancellationToken);
        await stream.WriteAsync(data, cancellationToken);
    }

    private static string Card(string key, object value, string? comment)
    {
        var text = value switch
        {
            bool flag => (flag ? "T" : "F").PadLeft(20),
            string word => $"'{word.Replace("'", "''").PadRight(8)}'".PadRight(20),
            int integer => integer.ToString(CultureInfo.InvariantCulture).PadLeft(20),
            double real => Real(real).PadLeft(20),
            _ => throw new ArgumentException($"Valore FITS non supportato per {key}.")
        };
        var card = $"{key,-8}= {text}" + (comment is null ? "" : $" / {comment}");
        return card.PadRight(80)[..80];
    }

    private static string Real(double value) => value.ToString("0.0#####", CultureInfo.InvariantCulture);

    private static async Task WriteManifestAsync(DemoDataset dataset, CancellationToken cancellationToken)
    {
        var manifest = new
        {
            schema = "astroforge-demo/1",
            target = Target,
            camera = Camera,
            telescope = $"{Telescope} + riduttore 0,7×",
            focalLengthMm = FocalLengthMm,
            note = "Dataset sintetico generato da AstroProject Forge per tutorial e test. Immagini ridotte 1/16 rispetto al sensore reale.",
            wheelProfile = dataset.WheelProfile,
            frames = dataset.Frames.Select(frame => new
            {
                path = frame.RelativePath.Replace('\\', '/'),
                kind = frame.Kind.ToString(),
                software = frame.Software.ToString(),
                filter = frame.FilterName,
                night = frame.SessionId,
                cloudy = frame.Cloudy
            })
        };
        await File.WriteAllTextAsync(Path.Combine(dataset.Root, "astroforge-demo.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), cancellationToken);
    }

    private sealed record Plane(double[] Pixels, bool Normalized);

    /// <summary>Deterministic star field, Veil filaments, vignetting, dust shadows and sensor noise.</summary>
    private sealed class Sky
    {
        private readonly (double X, double Y, double Flux)[] _stars;
        private readonly (int Index, double Rate)[] _hotPixels;
        private readonly int _seed;

        public Sky(int seed)
        {
            _seed = seed;
            var random = new Random(seed);
            _stars = Enumerable.Range(0, 140).Select(_ =>
                (random.NextDouble() * (Width + 40) - 20, random.NextDouble() * (Height + 40) - 20, 900 * Math.Pow(random.NextDouble(), 3.2) + 120)).ToArray();
            _hotPixels = Enumerable.Range(0, 36).Select(_ => (random.Next(Width * Height), 2 + random.NextDouble() * 10)).ToArray();
        }

        public Plane Light(string line, int dither, double fwhm, double transparency, int dust)
        {
            var random = new Random(_seed + dither * 7919);
            var shiftX = random.NextDouble() * 10 - 5;
            var shiftY = random.NextDouble() * 10 - 5;
            var (skyLevel, nebulaGain, starGain) = line switch
            {
                "Ha" => (180.0, 1.0, 1.0),
                "OIII" => (240.0, 0.8, 0.8),
                _ => (130.0, 0.35, 0.65)
            };
            var background = skyLevel * (transparency < 1 ? 4 : 1);
            var sigma = fwhm / 2.3548;
            var signal = new double[Width * Height];
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
                signal[y * Width + x] = background + nebulaGain * transparency * Nebula(line, x - shiftX, y - shiftY);
            foreach (var (starX, starY, flux) in _stars)
                Splat(signal, starX + shiftX, starY + shiftY, flux * starGain * transparency, sigma);

            var pixels = new double[signal.Length];
            for (var index = 0; index < pixels.Length; index++)
            {
                var (x, y) = (index % Width, index / Width);
                var electrons = signal[index] * Illumination(x, y, dust);
                pixels[index] = Pedestal + electrons + Gaussian(random) * Math.Sqrt(9 + electrons * 0.9);
            }
            foreach (var (index, rate) in _hotPixels) pixels[index] += rate * LightExposureSeconds;
            return new(pixels, false);
        }

        public Plane Flat(int sequence, int dust)
        {
            var random = new Random(_seed + 100_000 + sequence * 104729);
            var pixels = new double[Width * Height];
            for (var index = 0; index < pixels.Length; index++)
            {
                var level = 26_000 * Illumination(index % Width, index / Width, dust);
                pixels[index] = Pedestal + level + Gaussian(random) * Math.Sqrt(9 + level * 0.9);
            }
            return new(pixels, false);
        }

        public Plane MasterDark(double exposure)
        {
            var random = new Random(_seed + 200_000);
            var pixels = new double[Width * Height];
            for (var index = 0; index < pixels.Length; index++)
            {
                var (x, y) = (index % Width, index / Width);
                // A faint amp glow in the right corner, as the IMX571 shows on long exposures.
                var glow = 18 * Math.Exp(-(Math.Pow(Width - x, 2) + Math.Pow(y - Height * 0.1, 2)) / 900);
                pixels[index] = (Pedestal + 0.002 * exposure + glow + Gaussian(random) * 0.6) / 65535;
            }
            foreach (var (index, rate) in _hotPixels) pixels[index] += rate * exposure / 65535;
            return new(pixels, true);
        }

        public Plane MasterBias()
        {
            var random = new Random(_seed + 300_000);
            var pixels = new double[Width * Height];
            for (var index = 0; index < pixels.Length; index++)
                pixels[index] = (Pedestal + 0.4 * Math.Sin(index % Width * 0.21) + Gaussian(random) * 0.4) / 65535;
            return new(pixels, true);
        }

        private static double Nebula(string line, double x, double y)
        {
            // Eastern Veil: long braided arcs of a shell centred off the left edge. OIII sits on the outer edge
            // of the shock front, Hα and SII a little behind it.
            var cx = -Width * 0.9;
            var cy = Height * 0.62;
            var radius = Math.Sqrt(Math.Pow(x - cx, 2) + Math.Pow(y - cy, 2));
            var angle = Math.Atan2(y - cy, x - cx);
            var shell = Width * 1.28 + (line == "OIII" ? 7 : 0);
            var value = 0.0;
            for (var strand = 0; strand < 4; strand++)
            {
                var offset = shell - strand * 13 + 6 * Math.Sin(angle * (9 + strand * 3) + strand);
                var width = 2.2 + strand * 1.1;
                var brightness = strand == 0 ? 420 : 240 / strand;
                value += brightness * Math.Exp(-Math.Pow((radius - offset) / width, 2)) * (0.55 + 0.45 * Math.Sin(angle * 23 + strand * 2.1));
            }
            // Diffuse glow inside the shell.
            value += 40 * Math.Exp(-Math.Pow((radius - shell + 30) / 28, 2));
            return Math.Max(0, value);
        }

        private static double Illumination(int x, int y, int dust)
        {
            var dx = (x - Width / 2.0) / (Width / 2.0);
            var dy = (y - Height / 2.0) / (Width / 2.0);
            var vignetting = 1 - 0.32 * (dx * dx + dy * dy);
            var (donutX, donutY) = dust == 0 ? (Width * 0.30, Height * 0.35) : (Width * 0.62, Height * 0.70);
            var distance = Math.Sqrt(Math.Pow(x - donutX, 2) + Math.Pow(y - donutY, 2));
            var donut = 1 - 0.07 * Math.Exp(-Math.Pow((distance - 9) / 3.0, 2));
            return vignetting * donut;
        }

        private static void Splat(double[] signal, double centerX, double centerY, double flux, double sigma)
        {
            var reach = (int)Math.Ceiling(sigma * 4);
            var peak = flux / (2 * Math.PI * sigma * sigma) * 8;
            for (var y = Math.Max(0, (int)centerY - reach); y <= Math.Min(Height - 1, (int)centerY + reach); y++)
            for (var x = Math.Max(0, (int)centerX - reach); x <= Math.Min(Width - 1, (int)centerX + reach); x++)
                signal[y * Width + x] += peak * Math.Exp(-(Math.Pow(x - centerX, 2) + Math.Pow(y - centerY, 2)) / (2 * sigma * sigma));
        }

        private static double Gaussian(Random random)
        {
            var u1 = 1 - random.NextDouble();
            var u2 = random.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
    }
}
