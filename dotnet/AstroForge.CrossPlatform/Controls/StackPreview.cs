using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>
/// A simulated integration of the project's target: a procedural emission nebula whose noise shrinks as
/// nights are added (σ ∝ 1/SNR), read in the HOO or SHO palette with a dissolve between the two.
/// The picture is illustrative; the numbers next to it are the project's own.
/// </summary>
public sealed class StackPreview : AmbientControl
{
    public static readonly StyledProperty<double> SnrProperty = AvaloniaProperty.Register<StackPreview, double>(nameof(Snr), 1);
    public static readonly StyledProperty<int> PaletteProperty = AvaloniaProperty.Register<StackPreview, int>(nameof(Palette));
    public static readonly StyledProperty<string?> SeedProperty = AvaloniaProperty.Register<StackPreview, string?>(nameof(Seed));

    private const int W = 560, H = 340, NoiseFrames = 4;
    private static readonly Dictionary<int, Fields> Cache = [];
    private Fields? _fields;
    private WriteableBitmap? _bitmap;
    private readonly int[] _pixels = new int[W * H];
    private double _mix;
    private int _frame = -1;
    private int _building;

    static StackPreview() => AffectsRender<StackPreview>(SnrProperty);

    public double Snr { get => GetValue(SnrProperty); set => SetValue(SnrProperty, value); }
    public int Palette { get => GetValue(PaletteProperty); set => SetValue(PaletteProperty, value); }
    public string? Seed { get => GetValue(SeedProperty); set => SetValue(SeedProperty, value); }

    // Grain only moves while the stack is still noisy enough to see it.
    protected override bool WantsAmbient => _fields is not null && Snr < 3.5;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _ = BuildAsync();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SeedProperty) { _ = BuildAsync(); return; }
        if (change.Property == SnrProperty) { _frame = -1; InvalidateVisual(); return; }
        if (change.Property != PaletteProperty) return;
        var from = _mix;
        var to = Math.Clamp(Palette, 0, 1);
        _ = Motion.Tween(Avalonia.Controls.TopLevel.GetTopLevel(this), TimeSpan.FromMilliseconds(900), Motion.EaseInOutCubic, t =>
        {
            _mix = from + (to - from) * t;
            _frame = -1;
            InvalidateVisual();
        });
    }

    private async Task BuildAsync()
    {
        var seed = StableHash(Seed ?? "");
        lock (Cache)
            if (Cache.TryGetValue(seed, out var cached)) { _fields = cached; _frame = -1; InvalidateVisual(); return; }
        if (Interlocked.Exchange(ref _building, 1) == 1) return;
        try
        {
            var fields = await Task.Run(() => Fields.Build(seed));
            lock (Cache) Cache[seed] = fields;
            _fields = fields;
            _frame = -1;
            InvalidateVisual();
        }
        finally { _building = 0; }
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || _fields is null) return;
        var frame = (int)(Clock * 11);
        if (frame != _frame || _bitmap is null)
        {
            _frame = frame;
            Paint(_fields, frame);
        }

        // Cover the card like background-size: cover, anchored to the centre.
        var scale = Math.Max(bounds.Width / W, bounds.Height / H);
        var size = new Size(W * scale, H * scale);
        var destination = new Rect(new Point((bounds.Width - size.Width) / 2, (bounds.Height - size.Height) / 2), size);
        context.DrawImage(_bitmap!, new Rect(0, 0, W, H), destination);
    }

    private void Paint(Fields fields, int frame)
    {
        var sigma = 0.34 / Math.Max(1, Snr);
        var signal = 0.55 + 0.45 * Math.Min(1, (Snr * Snr) / 6);
        var noise = fields.Noise[frame % NoiseFrames];
        var mix = _mix;
        for (var p = 0; p < W * H; p++)
        {
            var e = noise[p] * sigma;
            var chroma = noise[(p * 7) % noise.Length] * sigma * 0.5;
            // HOO: Hα red, OIII teal. SHO (Hubble): SII red, Hα green, OIII blue.
            double bg = fields.Bg[p], ha = fields.Ha[p], o3 = fields.O3[p], s2 = fields.S2[p], st = fields.Stars[p];
            var r0 = bg * 0.7 + ha + o3 * 0.1 + st;
            var g0 = bg * 0.8 + ha * 0.28 + o3 * 0.85 + st * 0.97;
            var b0 = bg * 1.2 + ha * 0.3 + o3 * 0.95 + st * 1.05;
            var r1 = bg * 0.7 + s2 * 1.7 + st;
            var g1 = bg * 0.8 + ha * 0.62 + o3 * 0.18 + st * 0.97;
            var b1 = bg * 1.2 + o3 * 1.05 + st * 1.05;
            var r = (r0 + (r1 - r0) * mix) * signal + e + chroma;
            var g = (g0 + (g1 - g0) * mix) * signal + e;
            var b = (b0 + (b1 - b0) * mix) * signal + e - chroma;
            r = r / (1 + Math.Max(0, r)); g = g / (1 + Math.Max(0, g)); b = b / (1 + Math.Max(0, b));
            var R = (int)Math.Clamp(r * 300, 0, 255);
            var G = (int)Math.Clamp(g * 300, 0, 255);
            var B = (int)Math.Clamp(b * 310, 0, 255);
            _pixels[p] = unchecked((int)0xFF000000) | (R << 16) | (G << 8) | B;
        }

        _bitmap ??= new WriteableBitmap(new PixelSize(W, H), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var buffer = _bitmap.Lock();
        for (var row = 0; row < H; row++)
            Marshal.Copy(_pixels, row * W, buffer.Address + row * buffer.RowBytes, W);
    }

    private static int StableHash(string text)
    {
        unchecked
        {
            var hash = 17;
            foreach (var character in text.ToUpperInvariant()) hash = hash * 31 + character;
            return hash & 0x7FFF;
        }
    }

    private sealed class Fields
    {
        public required float[] Ha, O3, S2, Bg, Stars;
        public required float[][] Noise;

        public static Fields Build(int seed)
        {
            var ha = new float[W * H]; var o3 = new float[W * H]; var s2 = new float[W * H]; var bg = new float[W * H]; var stars = new float[W * H];
            var random = new Random(seed);
            // The filament arc sits somewhere different for each target.
            var cx = 0.1 + random.NextDouble() * 0.15; var cy = 1.0 + random.NextDouble() * 0.1; var radius = 0.72 + random.NextDouble() * 0.1;
            for (var y = 0; y < H; y++)
                for (var x = 0; x < W; x++)
                {
                    double u = x / (double)W, v = y / (double)H, nx = u * 3.2, ny = v * 2.2;
                    var wx = nx + Fbm(nx + 3, ny, seed + 7, false) * 1.3;
                    var wy = ny + Fbm(nx, ny + 5, seed + 9, false) * 1.3;
                    var arc = Math.Exp(-Math.Pow((Math.Sqrt((u - cx - 0.05) * (u - cx - 0.05) + (v - cy) * (v - cy)) - radius - 0.06) / 0.16, 2));
                    var arcO = Math.Exp(-Math.Pow((Math.Sqrt((u - cx) * (u - cx) + (v - cy - 0.03) * (v - cy - 0.03)) - radius) / 0.12, 2));
                    var p = y * W + x;
                    ha[p] = (float)(Math.Pow(Fbm(wx, wy, seed + 1, true), 4.2) * 1.6 * (0.25 + arc * 1.3));
                    o3[p] = (float)(Math.Pow(Fbm(wx * 1.1 + 0.3, wy * 1.1, seed + 4, true), 4.6) * 1.5 * (0.18 + arcO * 1.4));
                    s2[p] = (float)(ha[p] * (0.35 + 0.9 * Math.Pow(Fbm(wx * 1.3, wy * 1.3, seed + 21, true), 3)));
                    bg[p] = (float)(Fbm(nx * 0.6, ny * 0.6, seed + 11, false) * 0.08);
                }
            for (var k = 0; k < 420; k++)
            {
                int x = random.Next(W), y = random.Next(H);
                var brightness = Math.Pow(random.NextDouble(), 6) * 2.2 + 0.15;
                for (var dy = -2; dy <= 2; dy++)
                    for (var dx = -2; dx <= 2; dx++)
                    {
                        int px = x + dx, py = y + dy;
                        if (px < 0 || py < 0 || px >= W || py >= H) continue;
                        stars[py * W + px] += (float)(Math.Exp(-(dx * dx + dy * dy) / (brightness > 1 ? 1.4 : 0.45)) * brightness);
                    }
            }
            var noise = new float[NoiseFrames][];
            for (var f = 0; f < NoiseFrames; f++)
            {
                noise[f] = new float[W * H];
                for (var i = 0; i < noise[f].Length; i++)
                    noise[f][i] = (float)((random.NextDouble() + random.NextDouble() + random.NextDouble() - 1.5) * 1.25);
            }
            return new Fields { Ha = ha, O3 = o3, S2 = s2, Bg = bg, Stars = stars, Noise = noise };
        }

        private static double Hash(int x, int y, int s)
        {
            unchecked
            {
                var h = x * 374761393 + y * 668265263 + s * 1442695041;
                h = (h ^ (int)((uint)h >> 13)) * 1274126177;
                return (uint)(h ^ (int)((uint)h >> 16)) / 4294967295.0;
            }
        }

        private static double ValueNoise(double x, double y, int s)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            double xf = x - xi, yf = y - yi, u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
            double a = Hash(xi, yi, s), b = Hash(xi + 1, yi, s), c = Hash(xi, yi + 1, s), d = Hash(xi + 1, yi + 1, s);
            return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
        }

        private static double Fbm(double x, double y, int s, bool ridged)
        {
            double total = 0, amplitude = 0.55, frequency = 1;
            for (var octave = 0; octave < 5; octave++)
            {
                var n = ValueNoise(x * frequency, y * frequency, s + octave);
                if (ridged) n = 1 - Math.Abs(n * 2 - 1);
                total += n * amplitude;
                amplitude *= 0.5;
                frequency *= 2.03;
            }
            return total;
        }
    }
}
