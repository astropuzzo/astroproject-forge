using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>
/// The living sky behind every screen: a procedural Hα/OIII nebula, twinkling stars in three depth
/// layers with pointer parallax, occasional meteors and a GoTo-slew warp used on navigation.
/// Draws a still frame when motion is reduced or the window is in the background.
/// </summary>
public sealed class StarfieldBackdrop : Control
{
    private const int StarCount = 320;
    private const int NebulaWidth = 360, NebulaHeight = 220;
    private static readonly Color[] StarColors = [Color.Parse("#EEF2FF"), Color.Parse("#B9D0FF"), Color.Parse("#FFD1B0")];
    private static readonly IBrush Void = new ImmutableSolidColorBrush(Color.Parse("#04060D"));

    private readonly Star[] _stars = new Star[StarCount];
    private readonly IBrush[,] _starBrushes = new IBrush[StarColors.Length, 17];
    private readonly Random _random = new(20260615);
    private TopLevel? _host;
    private Bitmap? _nebula;
    private bool _running;
    private TimeSpan _lastFrame, _lastDraw, _clock;
    private Point _pointer = new(0.5, 0.5), _pointerTarget = new(0.5, 0.5);
    private double _warp, _warpDirection = 1;
    private TimeSpan? _warpStart;
    private Meteor? _meteor;
    private TimeSpan _nextMeteor = TimeSpan.FromSeconds(6);

    public StarfieldBackdrop()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        for (var color = 0; color < StarColors.Length; color++)
            for (var level = 0; level <= 16; level++)
            {
                var c = StarColors[color];
                _starBrushes[color, level] = new ImmutableSolidColorBrush(Color.FromArgb((byte)(level * 255 / 16), c.R, c.G, c.B));
            }
        for (var index = 0; index < StarCount; index++)
        {
            var depth = Math.Pow(_random.NextDouble(), 1.6);
            var tint = _random.NextDouble();
            _stars[index] = new Star(
                _random.NextDouble() * 1.06 - 0.03, _random.NextDouble() * 1.06 - 0.03, depth,
                0.35 + depth * 1.45, _random.NextDouble() * Math.Tau, 0.4 + _random.NextDouble() * 1.4,
                tint < 0.07 ? 2 : tint < 0.16 ? 1 : 0);
        }
    }

    /// <summary>Smears the sky sideways, like a mount slewing to the next target.</summary>
    public void Warp(int direction)
    {
        if (Motion.Reduced) return;
        _warpDirection = direction < 0 ? -1 : 1;
        _warpStart = null;
        _warp = 0.0001;
        EnsureRunning();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _host = TopLevel.GetTopLevel(this);
        _host?.AddHandler(PointerMovedEvent, Host_PointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        if (_host is Window window)
        {
            window.Activated += Host_ActivityChanged;
            window.Deactivated += Host_ActivityChanged;
        }
        Motion.ReducedChanged += Motion_ReducedChanged;
        _ = BuildNebulaAsync();
        EnsureRunning();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _host?.RemoveHandler(PointerMovedEvent, Host_PointerMoved);
        if (_host is Window window)
        {
            window.Activated -= Host_ActivityChanged;
            window.Deactivated -= Host_ActivityChanged;
        }
        Motion.ReducedChanged -= Motion_ReducedChanged;
        _host = null;
        _running = false;
        base.OnDetachedFromVisualTree(e);
    }

    private void Motion_ReducedChanged(object? sender, EventArgs e)
    {
        _warp = 0;
        _meteor = null;
        InvalidateVisual();
        EnsureRunning();
    }

    private void Host_ActivityChanged(object? sender, EventArgs e) => EnsureRunning();

    private void Host_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var position = e.GetPosition(this);
        _pointerTarget = new Point(Math.Clamp(position.X / Bounds.Width, 0, 1), Math.Clamp(position.Y / Bounds.Height, 0, 1));
        EnsureRunning();
    }

    private bool IsIdle => _host is Window { IsActive: false } || Motion.Reduced;

    private void EnsureRunning()
    {
        if (_running || _host is null) return;
        if (IsIdle && _warp <= 0) return;
        _running = true;
        _lastFrame = TimeSpan.Zero;
        _host.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan now)
    {
        if (_host is null) { _running = false; return; }
        var dt = _lastFrame == TimeSpan.Zero ? 1 / 60d : Math.Min(0.1, (now - _lastFrame).TotalSeconds);
        _lastFrame = now;
        _clock += TimeSpan.FromSeconds(dt);

        var ease = 1 - Math.Pow(0.001, dt);
        _pointer = new Point(_pointer.X + (_pointerTarget.X - _pointer.X) * ease, _pointer.Y + (_pointerTarget.Y - _pointer.Y) * ease);

        if (_warp > 0)
        {
            _warpStart ??= now;
            var k = Math.Min(1, (now - _warpStart.Value).TotalMilliseconds / 950);
            _warp = k >= 1 ? 0 : Math.Max(0.0001, Math.Sin(k * Math.PI) * (1 - k * 0.3));
            for (var index = 0; index < StarCount; index++)
            {
                ref var star = ref _stars[index];
                star.X -= _warpDirection * _warp * (0.004 + star.Depth * 0.02) * dt * 60;
                if (star.X < -0.03) star.X += 1.06;
                if (star.X > 1.03) star.X -= 1.06;
            }
        }

        if (!Motion.Reduced) UpdateMeteor(dt);

        // 30 fps is plenty for twinkle and parallax; the slew and meteors get every frame.
        var urgent = _warp > 0 || _meteor is not null;
        if (urgent || (now - _lastDraw).TotalMilliseconds >= 33)
        {
            _lastDraw = now;
            InvalidateVisual();
        }

        if (IsIdle && !urgent)
        {
            _running = false;
            InvalidateVisual();
            return;
        }
        _host.RequestAnimationFrame(OnFrame);
    }

    private void UpdateMeteor(double dt)
    {
        if (_meteor is { } meteor)
        {
            meteor.Age += dt;
            if (meteor.Age >= meteor.Life) _meteor = null;
            return;
        }
        if (_clock < _nextMeteor || Bounds.Width <= 0) return;
        _nextMeteor = _clock + TimeSpan.FromSeconds(9 + _random.NextDouble() * 14);
        var angle = (150 + _random.NextDouble() * 22) * Math.PI / 180;
        _meteor = new Meteor(
            new Point(Bounds.Width * (0.35 + _random.NextDouble() * 0.55), Bounds.Height * (0.02 + _random.NextDouble() * 0.25)),
            new Vector(Math.Cos(angle), Math.Sin(angle)), 260 + _random.NextDouble() * 220, 1.1);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        context.FillRectangle(Void, bounds);

        var px = _pointer.X - 0.5;
        var py = _pointer.Y - 0.5;
        var drift = Motion.Reduced ? 0 : Math.Sin(_clock.TotalSeconds / 40) * 12;

        if (_nebula is { } nebula)
        {
            var nebulaRect = new Rect(-40 - px * 18 + drift, -30 - py * 12, bounds.Width + 80, bounds.Height + 60);
            using (context.PushOpacity(0.9)) context.DrawImage(nebula, new Rect(nebula.Size), nebulaRect);
        }

        DrawGlow(context, bounds, new Point(0.12, 0.08), 0.55, Color.Parse("#2A4DFF"), 0.20);
        DrawGlow(context, bounds, new Point(0.92, 0.95), 0.50, Color.Parse("#FF5D73"), 0.10);

        var time = _clock.TotalSeconds;
        var reduced = Motion.Reduced;
        foreach (var star in _stars)
        {
            var x = star.X * bounds.Width - px * 28 * star.Depth;
            var y = star.Y * bounds.Height - py * 18 * star.Depth;
            if (x < -30 || y < -30 || x > bounds.Width + 30 || y > bounds.Height + 30) continue;
            var twinkle = reduced ? 0.6 : 0.35 + 0.55 * (0.5 + 0.5 * Math.Sin(star.Phase + time * star.Speed));
            var alpha = twinkle * (0.35 + 0.65 * star.Depth);
            var brush = _starBrushes[star.Color, Math.Clamp((int)Math.Round(alpha * 16), 0, 16)];
            if (_warp > 0.01)
            {
                var length = _warpDirection * _warp * (18 + star.Depth * 130);
                context.DrawLine(new Pen(brush, star.Radius * 1.3, lineCap: PenLineCap.Round), new Point(x, y), new Point(x + length, y));
                continue;
            }
            context.DrawEllipse(brush, null, new Point(x, y), star.Radius, star.Radius);
            if (star.Radius > 1.5)
            {
                // Diffraction spikes on the brightest stars, like a Newtonian's spider vanes.
                var spike = _starBrushes[star.Color, Math.Clamp((int)Math.Round(alpha * 6), 0, 16)];
                context.FillRectangle(spike, new Rect(x - 5, y - 0.35, 10, 0.7));
                context.FillRectangle(spike, new Rect(x - 0.35, y - 5, 0.7, 10));
            }
        }

        if (_meteor is { } meteor)
        {
            var k = meteor.Age / meteor.Life;
            var head = meteor.Start + meteor.Direction * (meteor.Length * Motion.EaseOutExpo(k));
            var tail = head - meteor.Direction * (meteor.Length * 0.35 * (1 - k * 0.6));
            var opacity = k < 0.2 ? k / 0.2 : 1 - (k - 0.2) / 0.8;
            var gradient = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(tail, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(head, RelativeUnit.Absolute),
                GradientStops = { new GradientStop(Color.FromArgb(0, 255, 255, 255), 0), new GradientStop(Color.FromArgb((byte)(230 * opacity), 235, 242, 255), 1) }
            };
            context.DrawLine(new Pen(gradient, 1.4, lineCap: PenLineCap.Round), tail, head);
        }

        var vignette = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.45, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.45, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.85, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.85, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0, 4, 6, 13), 0.55), new GradientStop(Color.FromArgb(200, 4, 6, 13), 1) }
        };
        context.FillRectangle(vignette, bounds);
    }

    private static void DrawGlow(DrawingContext context, Rect bounds, Point center, double radius, Color color, double strength)
    {
        var size = Math.Max(bounds.Width, bounds.Height) * radius;
        var point = new Point(bounds.Width * center.X, bounds.Height * center.Y);
        var brush = new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Color.FromArgb((byte)(255 * strength), color.R, color.G, color.B), 0), new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1) }
        };
        context.DrawEllipse(brush, null, point, size, size);
    }

    private async Task BuildNebulaAsync()
    {
        if (_nebula is not null) return;
        var pixels = await Task.Run(RenderNebula);
        var bitmap = new WriteableBitmap(new PixelSize(NebulaWidth, NebulaHeight), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
            for (var row = 0; row < NebulaHeight; row++)
                Marshal.Copy(pixels, row * NebulaWidth, buffer.Address + row * buffer.RowBytes, NebulaWidth);
        _nebula = bitmap;
        InvalidateVisual();
    }

    /// <summary>
    /// A faint emission nebula along a curved filament, in the telescope's own palette:
    /// Hα red on the ridges, OIII teal in the diffuse shell. Premultiplied BGRA.
    /// </summary>
    private static int[] RenderNebula()
    {
        var pixels = new int[NebulaWidth * NebulaHeight];
        for (var y = 0; y < NebulaHeight; y++)
            for (var x = 0; x < NebulaWidth; x++)
            {
                double u = x / (double)NebulaWidth, v = y / (double)NebulaHeight;
                double nx = u * 3.2, ny = v * 2.2;
                var wx = nx + Fbm(nx + 3, ny, 7, false) * 1.3;
                var wy = ny + Fbm(nx, ny + 5, 9, false) * 1.3;
                var arc = Math.Exp(-Math.Pow((Math.Sqrt((u - 0.08) * (u - 0.08) + (v - 1.1) * (v - 1.1)) - 0.95) / 0.22, 2));
                var arc2 = Math.Exp(-Math.Pow((Math.Sqrt((u - 1.05) * (u - 1.05) + (v + 0.15) * (v + 0.15)) - 0.55) / 0.18, 2));
                var ridge = Fbm(wx * 1.6, wy * 1.6, 3, true);
                var cloud = Fbm(wx, wy, 11, false);
                var ha = Math.Clamp(Math.Pow(ridge, 3.2) * (arc * 1.1 + arc2 * 0.7) * 1.2, 0, 1);
                var o3 = Math.Clamp(Math.Pow(cloud, 2.4) * (arc * 0.8 + arc2 * 0.9 + 0.08) * 0.9, 0, 1);
                double r = 255 * ha * 0.95 + 63 * o3 * 0.6, g = 93 * ha * 0.5 + 224 * o3 * 0.55, b = 115 * ha * 0.6 + 208 * o3 * 0.62;
                var a = Math.Clamp(Math.Max(ha * 0.42, o3 * 0.34), 0, 1);
                r = Math.Min(r * a, 255 * a); g = Math.Min(g * a, 255 * a); b = Math.Min(b * a, 255 * a);
                pixels[y * NebulaWidth + x] = ((int)(a * 255) << 24) | ((int)r << 16) | ((int)g << 8) | (int)b;
            }
        return pixels;
    }

    private static double Hash(int x, int y, int seed)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177;
            return (h ^ (h >> 16)) / (double)uint.MaxValue;
        }
    }

    private static double ValueNoise(double x, double y, int seed)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
        double xf = x - xi, yf = y - yi, u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
        double a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed), c = Hash(xi, yi + 1, seed), d = Hash(xi + 1, yi + 1, seed);
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }

    private static double Fbm(double x, double y, int seed, bool ridged)
    {
        double total = 0, amplitude = 0.55, frequency = 1;
        for (var octave = 0; octave < 5; octave++)
        {
            var n = ValueNoise(x * frequency, y * frequency, seed + octave);
            if (ridged) n = 1 - Math.Abs(n * 2 - 1);
            total += n * amplitude;
            amplitude *= 0.5;
            frequency *= 2.03;
        }
        return total;
    }

    private struct Star(double x, double y, double depth, double radius, double phase, double speed, int color)
    {
        public double X = x, Y = y;
        public readonly double Depth = depth, Radius = radius, Phase = phase, Speed = speed;
        public readonly int Color = color;
    }

    private sealed class Meteor(Point start, Vector direction, double length, double life)
    {
        public Point Start { get; } = start;
        public Vector Direction { get; } = direction;
        public double Length { get; } = length;
        public double Life { get; } = life;
        public double Age { get; set; }
    }
}
