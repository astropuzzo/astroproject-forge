using System.Buffers.Binary;
using System.Globalization;

namespace AstroForge.Core.Quality;

public sealed record QualityMetrics(
    string Path, double Background, double Noise, double Signal, double Snr,
    double FwhmPixels, double Eccentricity, int StarCount,
    int CoverageZones, int PreviewWidth, int PreviewHeight, byte[] PreviewPixels);

public sealed record QualityAnalysisProgress(int Completed, int Total, string CurrentFile);

public sealed record QualityPreview(int Width, int Height, bool IsColor, byte[] Pixels);

public static class FitsQualityAnalyzer
{
    private const int AnalysisTileSize = 384;
    private const int PreviewLimit = 640;

    public static async Task<QualityMetrics> AnalyzeAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!new[] { ".fit", ".fits", ".fts" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new NotSupportedException("L’analisi pixel v1 supporta i file FITS; XISF verrà aggiunto nel decoder successivo.");

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        var header = await ReadHeaderAsync(stream, cancellationToken);
        var width = Integer(header.Values, "NAXIS1");
        var height = Integer(header.Values, "NAXIS2");
        var bitpix = Integer(header.Values, "BITPIX");
        if (width <= 0 || height <= 0) throw new InvalidDataException("Geometria FITS non valida.");
        var bytesPerPixel = Math.Abs(bitpix) / 8;
        if (bytesPerPixel is not (1 or 2 or 4 or 8) || bitpix is not (8 or 16 or 32 or -32 or -64))
            throw new NotSupportedException($"BITPIX {bitpix} non supportato dal Quality Lab.");
        var bscale = Number(header.Values, "BSCALE", 1);
        var bzero = Number(header.Values, "BZERO", 0);

        var tiles = await ReadAnalysisTilesAsync(stream, header.DataOffset, width, height, bitpix, bytesPerPixel, bscale, bzero, cancellationToken);
        var bayerPattern = NormalizeBayer(HeaderText(header.Values, "BAYERPAT", "COLORTYP"));
        var backgrounds = new List<double>(tiles.Count);
        var noises = new List<double>(tiles.Count);
        var signals = new List<double>(tiles.Count);
        var stars = new List<(double Fwhm, double Eccentricity, double PeakSnr)>();
        foreach (var rawTile in tiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tile = bayerPattern is null ? rawTile : CollapseBayer(rawTile);
            var spatialScale = ReferenceEquals(tile, rawTile) ? 1d : 2d;
            var sampleStep = Math.Max(1, tile.Pixels.Length / 60_000);
            var finiteSamples = new List<float>((tile.Pixels.Length + sampleStep - 1) / sampleStep);
            for (var source = 0; source < tile.Pixels.Length; source += sampleStep)
                if (float.IsFinite(tile.Pixels[source])) finiteSamples.Add(tile.Pixels[source]);
            if (finiteSamples.Count < 64) continue;
            var sample = finiteSamples.ToArray();
            Array.Sort(sample);
            var localBackground = Percentile(sample, 0.5);
            var deviations = new float[sample.Length];
            for (var index = 0; index < sample.Length; index++) deviations[index] = (float)Math.Abs(sample[index] - localBackground);
            Array.Sort(deviations);
            var localNoise = Math.Max(1e-9, Percentile(deviations, 0.5) * 1.4826);
            var localHigh = Percentile(sample, 0.995);
            backgrounds.Add(localBackground);
            noises.Add(localNoise);
            signals.Add(Math.Max(0, localHigh - localBackground));
            foreach (var star in DetectStars(tile.Pixels, tile.Width, tile.Height, localBackground, localNoise, Percentile(sample, 0.999)))
                stars.Add((star.Fwhm * spatialScale, star.Eccentricity, star.PeakSnr));
        }
        if (backgrounds.Count == 0) throw new InvalidDataException("Il FITS non contiene abbastanza pixel numerici validi.");
        var background = Median(backgrounds.ToArray());
        var noise = Math.Max(1e-9, Median(noises.ToArray()));
        var signal = Median(signals.ToArray());
        // Stars beyond the fitted quota of a tile are counted but carry no shape.
        var shapes = stars.Where(item => double.IsFinite(item.Fwhm)).ToArray();
        var fwhm = shapes.Length == 0 ? 0 : Median(shapes.Select(item => item.Fwhm).ToArray());
        var eccentricity = shapes.Length == 0 ? 0 : Median(shapes.Select(item => item.Eccentricity).ToArray());
        var snr = stars.Count == 0 ? 0 : Median(stars.Select(item => item.PeakSnr).ToArray());
        var preview = await ReadPreviewAsync(stream, header.DataOffset, width, height, bitpix, bytesPerPixel, bscale, bzero, background, background + Math.Max(signal, noise), cancellationToken);
        return new(path, background, noise, signal, snr, fwhm, eccentricity, stars.Count, backgrounds.Count, preview.Width, preview.Height, preview.Pixels);
    }

    public static async Task<QualityPreview> RenderPreviewAsync(
        string path, string? bayerPattern, bool debayer, double stretchStrength,
        CancellationToken cancellationToken = default, int maxDimension = 960)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        var header = await ReadHeaderAsync(stream, cancellationToken);
        var width = Integer(header.Values, "NAXIS1");
        var height = Integer(header.Values, "NAXIS2");
        var bitpix = Integer(header.Values, "BITPIX");
        if (width <= 1 || height <= 1) throw new InvalidDataException("Geometria FITS non valida.");
        var bytesPerPixel = Math.Abs(bitpix) / 8;
        if (bytesPerPixel is not (1 or 2 or 4 or 8) || bitpix is not (8 or 16 or 32 or -32 or -64))
            throw new NotSupportedException($"BITPIX {bitpix} non supportato dal renderer.");
        var bscale = Number(header.Values, "BSCALE", 1);
        var bzero = Number(header.Values, "BZERO", 0);
        var pattern = NormalizeBayer(bayerPattern);
        var color = debayer && pattern is not null;
        var scale = maxDimension <= 0 ? 1d : Math.Max(1d, Math.Max(width, height) / (double)maxDimension);
        var outputWidth = Math.Max(1, (int)Math.Round(width / scale));
        var outputHeight = Math.Max(1, (int)Math.Round(height / scale));
        var channels = color ? 3 : 1;
        var values = new float[outputWidth * outputHeight * channels];
        var rowABytes = new byte[width * bytesPerPixel];
        var rowBBytes = new byte[width * bytesPerPixel];
        var rowA = new float[width];
        var rowB = new float[width];

        for (var y = 0; y < outputHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceY = Math.Min(height - (color ? 2 : 1), (int)(y * scale));
            if (color) sourceY &= ~1;
            stream.Position = checked(header.DataOffset + (long)sourceY * width * bytesPerPixel);
            await stream.ReadExactlyAsync(rowABytes, cancellationToken);
            DecodeRow(rowABytes, rowA, bitpix, bscale, bzero);
            if (color)
            {
                await stream.ReadExactlyAsync(rowBBytes, cancellationToken);
                DecodeRow(rowBBytes, rowB, bitpix, bscale, bzero);
            }
            for (var x = 0; x < outputWidth; x++)
            {
                var sourceX = Math.Min(width - (color ? 2 : 1), (int)(x * scale));
                if (!color) { values[y * outputWidth + x] = rowA[sourceX]; continue; }
                sourceX &= ~1;
                var a = rowA[sourceX]; var b = rowA[sourceX + 1];
                var c = rowB[sourceX]; var d = rowB[sourceX + 1];
                var target = (y * outputWidth + x) * 3;
                (values[target], values[target + 1], values[target + 2]) = pattern switch
                {
                    "RGGB" => (a, (b + c) / 2, d),
                    "BGGR" => (d, (b + c) / 2, a),
                    "GRBG" => (b, (a + d) / 2, c),
                    "GBRG" => (c, (a + d) / 2, b),
                    _ => (a, (b + c) / 2, d)
                };
            }
        }

        var strength = Math.Clamp(stretchStrength, 0, 12);
        var pixels = new byte[values.Length];
        if (color)
        {
            // Un fondo Bayer non è neutro: ogni canale ha offset e risposta differenti.
            // Lo stretch per-canale neutralizza il fondo della preview senza alterare il FITS.
            var channelStats = Enumerable.Range(0, 3).Select(channel => PreviewStats(values, 3, channel)).ToArray();
            for (var index = 0; index < values.Length; index++)
            {
                var stats = channelStats[index % 3];
                pixels[index] = Stretch(values[index], stats.Black, stats.White, strength);
            }
        }
        else
        {
            var stats = PreviewStats(values, 1, 0);
            for (var index = 0; index < values.Length; index++) pixels[index] = Stretch(values[index], stats.Black, stats.White, strength);
        }
        return new(outputWidth, outputHeight, color, pixels);
    }

    private static (double Black, double White) PreviewStats(float[] values, int stride, int channel)
    {
        var samples = new float[(values.Length + stride - 1) / stride];
        for (int source = channel, target = 0; source < values.Length; source += stride) samples[target++] = values[source];
        Array.Sort(samples);
        var median = Percentile(samples, 0.5);
        var deviations = samples.Select(value => (float)Math.Abs(value - median)).ToArray();
        Array.Sort(deviations);
        var noise = Math.Max(1e-9, Percentile(deviations, 0.5) * 1.4826);
        var black = median - 2.5 * noise;
        return (black, Math.Max(black + noise, Percentile(samples, 0.997)));
    }

    private static string? NormalizeBayer(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var compact = new string(value.ToUpperInvariant().Where(char.IsLetter).ToArray());
        return new[] { "RGGB", "BGGR", "GRBG", "GBRG" }.FirstOrDefault(compact.Contains);
    }

    private static byte Stretch(double value, double black, double white, double strength)
    {
        var normalized = Math.Clamp((value - black) / Math.Max(1e-9, white - black), 0, 1);
        var stretched = strength <= 0.01 ? normalized : Math.Asinh(normalized * strength) / Math.Asinh(strength);
        return (byte)Math.Round(stretched * 255);
    }

    private static List<(double Fwhm, double Eccentricity, double PeakSnr)> DetectStars(float[] pixels, int width, int height, double background, double noise, double high)
    {
        var threshold = background + Math.Max(6 * noise, (high - background) * 0.04);
        var candidates = new List<(float Peak, int X, int Y)>();
        for (var y = 7; y < height - 7; y++)
        for (var x = 7; x < width - 7; x++)
        {
            var value = pixels[y * width + x];
            if (!float.IsFinite(value)) continue;
            if (value < threshold) continue;
            var isMaximum = true;
            for (var dy = -1; dy <= 1 && isMaximum; dy++)
            for (var dx = -1; dx <= 1; dx++)
                if ((dx != 0 || dy != 0) && value <= pixels[(y + dy) * width + x + dx]) { isMaximum = false; break; }
            if (!isMaximum) continue;
            candidates.Add((value, x, y));
        }
        var selected = new List<(double Fwhm, double Eccentricity, double PeakSnr)>();
        var measured = new List<(int X, int Y, double Reach)>();
        var fitted = 0;
        foreach (var candidate in candidates.OrderByDescending(item => item.Peak).Take(3000))
        {
            if (measured.Any(star => (star.X - candidate.X) * (star.X - candidate.X) + (star.Y - candidate.Y) * (star.Y - candidate.Y) < star.Reach * star.Reach)) continue;
            // The star sets its own scale. Fixed windows (a 5 to 7 px sky ring, an 11 px box) sat inside the wings of
            // anything broader than about 4 px FWHM, so soft frames all read the same and hazy ones read sharper.
            if (SkyRing(pixels, width, height, candidate.X, candidate.Y, 5, 7) is not { } near) continue;
            if (HalfWidth(pixels, width, height, candidate.X, candidate.Y, near.Median + (candidate.Peak - near.Median) * 0.5) is not { } half) continue;
            var inner = Math.Max(5, (int)Math.Ceiling(4 * half));
            if (SkyRing(pixels, width, height, candidate.X, candidate.Y, inner, inner + 2) is not { } ring) continue;
            // The local sky: gradients and vignetting must not alter the stellar profile.
            var localBackground = ring.Median;
            var localNoise = Math.Max(noise, ring.Noise);
            var amplitude = candidate.Peak - localBackground;
            var peakSnr = amplitude / Math.Max(localNoise, 1e-9);
            if (peakSnr < 7) continue;

            // One bright pixel is a hot pixel or a cosmic ray; a flat top is a saturated star with no trustworthy width.
            int aboveHalfPeak = 0, flatTop = 0;
            for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
            {
                var signal = pixels[(candidate.Y + dy) * width + candidate.X + dx] - localBackground;
                if (signal > amplitude * 0.5) aboveHalfPeak++;
                if ((dx != 0 || dy != 0) && signal > amplitude * 0.995) flatTop++;
            }
            if (aboveHalfPeak < 2 || flatTop >= 3) continue;

            // The brightest stars of a tile carry the width; fainter ones only count, as they always did.
            if (fitted >= MaximumFittedStarsPerTile)
            {
                measured.Add((candidate.X, candidate.Y, 6));
                selected.Add((double.NaN, double.NaN, peakSnr));
                continue;
            }
            var sigma = Math.Max(0.7, half / 1.1774);
            var reach = Math.Max(3, (int)Math.Ceiling(2.5 * sigma));
            if (candidate.X < reach || candidate.Y < reach || candidate.X >= width - reach || candidate.Y >= height - reach) continue;
            if (FitGaussian(pixels, width, candidate.X, candidate.Y, reach, localBackground, amplitude, sigma) is not { } fit) continue;
            if (Math.Abs(fit.CentreX) > 1.5 || Math.Abs(fit.CentreY) > 1.5) continue;
            var fwhm = 2.35482 * Math.Sqrt((fit.Major + fit.Minor) / 2);
            if (fwhm is < 1.35 or > MaximumFwhm || fit.Minor / fit.Major < 0.05) continue;
            fitted++;
            selected.Add((fwhm, Math.Sqrt(Math.Clamp(1 - fit.Minor / fit.Major, 0, 1)), peakSnr));
            measured.Add((candidate.X, candidate.Y, Math.Max(6, 1.5 * fwhm)));
        }
        return selected;
    }

    private const int MaximumFittedStarsPerTile = 300;
    private const double MaximumFwhm = 16;

    /// <summary>Median and noise (MAD × 1.4826) of the pixels between two radii around a star; null near the tile's edge.</summary>
    private static (double Median, double Noise)? SkyRing(float[] pixels, int width, int height, int x, int y, int inner, int outer)
    {
        if (x < outer || y < outer || x >= width - outer || y >= height - outer) return null;
        var ring = new List<float>(8 * outer);
        for (var dy = -outer; dy <= outer; dy++) for (var dx = -outer; dx <= outer; dx++)
        {
            var radius2 = dx * dx + dy * dy;
            if (radius2 < inner * inner || radius2 > outer * outer) continue;
            var value = pixels[(y + dy) * width + x + dx];
            if (float.IsFinite(value)) ring.Add(value);
        }
        if (ring.Count < 24) return null;
        ring.Sort();
        var median = ring[ring.Count / 2];
        var deviations = ring.Select(value => Math.Abs(value - median)).OrderBy(value => value).ToArray();
        return (median, deviations[deviations.Length / 2] * 1.4826);
    }

    /// <summary>Mean distance from the peak, along the four axes, at which the profile crosses <paramref name="level"/>; null for blobs too broad to be a star.</summary>
    private static double? HalfWidth(float[] pixels, int width, int height, int x, int y, double level)
    {
        const int limit = 10;
        double total = 0;
        foreach (var (stepX, stepY) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            double previous = pixels[y * width + x];
            var crossed = false;
            for (var step = 1; step <= limit; step++)
            {
                int px = x + stepX * step, py = y + stepY * step;
                if (px < 0 || py < 0 || px >= width || py >= height) return null;
                double value = pixels[py * width + px];
                if (!double.IsFinite(value)) return null;
                if (value < level)
                {
                    total += step - 1 + (previous - level) / Math.Max(previous - value, 1e-9);
                    crossed = true;
                    break;
                }
                previous = value;
            }
            if (!crossed) return null;
        }
        return total / 4;
    }

    /// <summary>
    /// Least-squares fit of an elliptical Gaussian to the background-subtracted pixels within <paramref name="reach"/>
    /// (Levenberg-Marquardt). Unlike image moments above a threshold, a fit neither shrinks with a window that cuts the
    /// wings nor with a fainter star, so the width does not depend on brightness.
    /// </summary>
    private static (double CentreX, double CentreY, double Major, double Minor)? FitGaussian(
        float[] pixels, int width, int x, int y, int reach, double localBackground, double amplitude, double sigma)
    {
        var points = new List<(double X, double Y, double Signal)>((2 * reach + 1) * (2 * reach + 1));
        for (var dy = -reach; dy <= reach; dy++) for (var dx = -reach; dx <= reach; dx++)
        {
            if (dx * dx + dy * dy > reach * reach) continue;
            var value = pixels[(y + dy) * width + x + dx];
            if (float.IsFinite(value)) points.Add((dx, dy, value - localBackground));
        }
        if (points.Count < 9) return null;

        // Parameters: amplitude, centre x and y, then the inverse covariance [[a, b], [b, c]].
        Span<double> parameters = [amplitude, 0, 0, 1 / (sigma * sigma), 0, 1 / (sigma * sigma)];
        Span<double> trial = stackalloc double[6];
        Span<double> step = stackalloc double[6];
        Span<double> jacobian = stackalloc double[6];
        var normal = new double[6, 7];
        var error = Residual(points, parameters);
        var damping = 1e-3;
        for (var iteration = 0; iteration < 30; iteration++)
        {
            Array.Clear(normal);
            foreach (var (px, py, signal) in points)
            {
                double u = px - parameters[1], v = py - parameters[2];
                var shape = Math.Exp(-0.5 * (parameters[3] * u * u + 2 * parameters[4] * u * v + parameters[5] * v * v));
                var model = parameters[0] * shape;
                jacobian[0] = shape;
                jacobian[1] = model * (parameters[3] * u + parameters[4] * v);
                jacobian[2] = model * (parameters[4] * u + parameters[5] * v);
                jacobian[3] = -0.5 * model * u * u;
                jacobian[4] = -model * u * v;
                jacobian[5] = -0.5 * model * v * v;
                for (var row = 0; row < 6; row++)
                {
                    for (var column = 0; column < 6; column++) normal[row, column] += jacobian[row] * jacobian[column];
                    normal[row, 6] += jacobian[row] * (signal - model);
                }
            }
            var improved = false;
            for (var attempt = 0; attempt < 8 && !improved; attempt++)
            {
                if (!Solve(normal, damping, step)) { damping *= 10; continue; }
                for (var index = 0; index < 6; index++) trial[index] = parameters[index] + step[index];
                var positive = trial[0] > 0 && trial[3] > 0 && trial[5] > 0 && trial[3] * trial[5] - trial[4] * trial[4] > 0;
                var trialError = positive ? Residual(points, trial) : double.PositiveInfinity;
                if (trialError < error)
                {
                    var gain = (error - trialError) / Math.Max(error, 1e-30);
                    trial.CopyTo(parameters);
                    error = trialError;
                    damping = Math.Max(damping / 5, 1e-9);
                    improved = true;
                    if (gain < 1e-6) iteration = int.MaxValue - 1;
                }
                else damping *= 10;
            }
            if (!improved) break;
        }

        double a = parameters[3], b = parameters[4], c = parameters[5];
        var determinant = a * c - b * b;
        if (!(determinant > 0) || !double.IsFinite(determinant)) return null;
        double xx = c / determinant, yy = a / determinant, xy = -b / determinant;
        var spread = Math.Sqrt(Math.Max(0, (xx - yy) * (xx - yy) + 4 * xy * xy));
        var major = (xx + yy + spread) / 2;
        var minor = (xx + yy - spread) / 2;
        return major > 0 && minor > 0 ? (parameters[1], parameters[2], major, minor) : null;
    }

    private static double Residual(List<(double X, double Y, double Signal)> points, ReadOnlySpan<double> parameters)
    {
        double total = 0;
        foreach (var (px, py, signal) in points)
        {
            double u = px - parameters[1], v = py - parameters[2];
            var difference = signal - parameters[0] * Math.Exp(-0.5 * (parameters[3] * u * u + 2 * parameters[4] * u * v + parameters[5] * v * v));
            total += difference * difference;
        }
        return total;
    }

    /// <summary>Solves the damped normal equations (the last column holds the right-hand side) by Gaussian elimination with pivoting.</summary>
    private static bool Solve(double[,] normal, double damping, Span<double> solution)
    {
        Span<double> work = stackalloc double[6 * 7];
        for (var row = 0; row < 6; row++)
            for (var column = 0; column < 7; column++)
                work[row * 7 + column] = row == column ? normal[row, column] * (1 + damping) : normal[row, column];
        for (var pivot = 0; pivot < 6; pivot++)
        {
            var best = pivot;
            for (var row = pivot + 1; row < 6; row++)
                if (Math.Abs(work[row * 7 + pivot]) > Math.Abs(work[best * 7 + pivot])) best = row;
            if (Math.Abs(work[best * 7 + pivot]) < 1e-300) return false;
            if (best != pivot)
                for (var column = 0; column < 7; column++)
                    (work[pivot * 7 + column], work[best * 7 + column]) = (work[best * 7 + column], work[pivot * 7 + column]);
            for (var row = pivot + 1; row < 6; row++)
            {
                var factor = work[row * 7 + pivot] / work[pivot * 7 + pivot];
                for (var column = pivot; column < 7; column++) work[row * 7 + column] -= factor * work[pivot * 7 + column];
            }
        }
        for (var row = 5; row >= 0; row--)
        {
            var value = work[row * 7 + 6];
            for (var column = row + 1; column < 6; column++) value -= work[row * 7 + column] * solution[column];
            solution[row] = value / work[row * 7 + row];
            if (!double.IsFinite(solution[row])) return false;
        }
        return true;
    }

    private sealed record AnalysisTile(float[] Pixels, int Width, int Height);

    private static AnalysisTile CollapseBayer(AnalysisTile source)
    {
        if (source.Width < 4 || source.Height < 4) return source;
        var width = source.Width / 2;
        var height = source.Height / 2;
        var pixels = new float[width * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = y * 2 * source.Width + x * 2;
            var values = new[] { source.Pixels[offset], source.Pixels[offset + 1], source.Pixels[offset + source.Width], source.Pixels[offset + source.Width + 1] };
            var finite = values.Where(float.IsFinite).ToArray();
            pixels[y * width + x] = finite.Length == 0 ? float.NaN : finite.Average();
        }
        return new(pixels, width, height);
    }

    private static async Task<List<AnalysisTile>> ReadAnalysisTilesAsync(
        FileStream stream, long dataOffset, int width, int height, int bitpix, int bytesPerPixel,
        double bscale, double bzero, CancellationToken cancellationToken)
    {
        var tileWidth = Math.Min(AnalysisTileSize, width);
        var tileHeight = Math.Min(AnalysisTileSize, height);
        var xStarts = TileStarts(width, tileWidth);
        var yStarts = TileStarts(height, tileHeight);
        var tiles = new List<AnalysisTile>(xStarts.Length * yStarts.Length);
        foreach (var yStart in yStarts)
        foreach (var xStart in xStarts)
        {
            var pixels = new float[tileWidth * tileHeight];
            var rowBytes = new byte[tileWidth * bytesPerPixel];
            for (var y = 0; y < tileHeight; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stream.Position = checked(dataOffset + ((long)(yStart + y) * width + xStart) * bytesPerPixel);
                await stream.ReadExactlyAsync(rowBytes, cancellationToken);
                DecodeRow(rowBytes, pixels.AsSpan(y * tileWidth, tileWidth), bitpix, bscale, bzero);
            }
            tiles.Add(new(pixels, tileWidth, tileHeight));
        }
        return tiles;
    }

    private static int[] TileStarts(int length, int tileLength)
    {
        if (length <= tileLength * 2) return [(length - tileLength) / 2];
        return [0, (length - tileLength) / 2, length - tileLength];
    }

    private static async Task<(int Width, int Height, byte[] Pixels)> ReadPreviewAsync(FileStream stream, long dataOffset, int width, int height, int bitpix, int bytesPerPixel, double bscale, double bzero, double low, double high, CancellationToken cancellationToken)
    {
        var scale = Math.Max(1d, Math.Max(width, height) / (double)PreviewLimit);
        var outputWidth = Math.Max(1, (int)Math.Round(width / scale));
        var outputHeight = Math.Max(1, (int)Math.Round(height / scale));
        var pixels = new byte[outputWidth * outputHeight];
        var sourceRow = new byte[width * bytesPerPixel];
        var decoded = new float[width];
        var range = Math.Max(1e-9, high - low);
        for (var y = 0; y < outputHeight; y++)
        {
            var sourceY = Math.Min(height - 1, (int)(y * scale));
            stream.Position = checked(dataOffset + (long)sourceY * width * bytesPerPixel);
            await stream.ReadExactlyAsync(sourceRow, cancellationToken);
            DecodeRow(sourceRow, decoded, bitpix, bscale, bzero);
            for (var x = 0; x < outputWidth; x++)
            {
                var value = Math.Clamp((decoded[Math.Min(width - 1, (int)(x * scale))] - low) / range, 0, 1);
                pixels[y * outputWidth + x] = (byte)Math.Round(Math.Sqrt(value) * 255);
            }
        }
        return (outputWidth, outputHeight, pixels);
    }

    private static void DecodeRow(ReadOnlySpan<byte> source, Span<float> destination, int bitpix, double bscale, double bzero)
    {
        var size = Math.Abs(bitpix) / 8;
        for (var index = 0; index < destination.Length; index++)
        {
            var bytes = source.Slice(index * size, size);
            double raw = bitpix switch
            {
                8 => bytes[0],
                16 => BinaryPrimitives.ReadInt16BigEndian(bytes),
                32 => BinaryPrimitives.ReadInt32BigEndian(bytes),
                -32 => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes)),
                -64 => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(bytes)),
                _ => throw new NotSupportedException()
            };
            destination[index] = (float)(raw * bscale + bzero);
        }
    }

    private static async Task<(Dictionary<string, string> Values, long DataOffset)> ReadHeaderAsync(FileStream stream, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var block = new byte[2880]; long bytes = 0;
        while (true)
        {
            await stream.ReadExactlyAsync(block, cancellationToken); bytes += block.Length;
            for (var offset = 0; offset < block.Length; offset += 80)
            {
                var card = System.Text.Encoding.ASCII.GetString(block, offset, 80);
                var key = card[..8].Trim();
                if (key == "END") return (values, bytes);
                if (card.Length > 10 && card[8] == '=') values[key] = card[10..].Split('/')[0].Trim().Trim('\'');
            }
        }
    }

    private static int Integer(Dictionary<string, string> values, string key) => values.TryGetValue(key, out var text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    private static double Number(Dictionary<string, string> values, string key, double fallback) => values.TryGetValue(key, out var text) && double.TryParse(text.Replace('D', 'E'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    private static string? HeaderText(Dictionary<string, string> values, params string[] keys) =>
        keys.Select(key => values.TryGetValue(key, out var value) ? value : null).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    private static double Percentile(float[] sorted, double percentile) => sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Round((sorted.Length - 1) * percentile), 0, sorted.Length - 1)];
    private static double Median(double[] values) { Array.Sort(values); return values.Length == 0 ? 0 : values[values.Length / 2]; }
}
