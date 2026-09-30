using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>One sub-exposure on the night timeline, in local hours from 18 (18:00) to 30 (06:00 next morning).</summary>
public sealed record SkyExposure(double Start, double Duration, Color Color, bool Flagged);

/// <summary>
/// One observing night: its exposures, the Moon's phase and, when the headers carry the site, the Sun's altitude
/// every ten minutes from 18:00 to 06:00 so twilight can be shaded where it really was.
/// </summary>
public sealed record SkyNight(string Label, string Detail, IReadOnlyList<SkyExposure> Exposures, double MoonAge, double MoonIllumination, IReadOnlyList<double>? SunAltitudes);

/// <summary>
/// Every night of the project as a row across the dark hours, 18:00 → 06:00, with a tick per sub in its filter's
/// colour, the Moon's phase beside the date and twilight at the edges. Nights beyond <see cref="Shown"/> fade out,
/// so the stack scrubber and the timeline tell the same story. Clicking a night integrates up to it.
/// </summary>
public sealed class NightSky : Control
{
    public static readonly StyledProperty<IReadOnlyList<SkyNight>?> NightsProperty =
        AvaloniaProperty.Register<NightSky, IReadOnlyList<SkyNight>?>(nameof(Nights));
    public static readonly StyledProperty<int> ShownProperty = AvaloniaProperty.Register<NightSky, int>(nameof(Shown), int.MaxValue);

    public const double StartHour = 18, EndHour = 30;
    private const double LabelWidth = 58, AxisHeight = 16;
    private static readonly Typeface Mono = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#JetBrains Mono"));
    private static readonly Typeface Body = new(FontFamily.Default);
    private static readonly IBrush Muted = new ImmutableSolidColorBrush(Color.Parse("#8E97BD"));
    private static readonly IBrush Dim = new ImmutableSolidColorBrush(Color.Parse("#5B6490"));
    private static readonly IBrush Accent = new ImmutableSolidColorBrush(Color.Parse("#9DB8FF"));
    private static readonly IBrush Fg = new ImmutableSolidColorBrush(Color.Parse("#EEF1FF"));
    private static readonly IBrush MoonDark = new ImmutableSolidColorBrush(Color.Parse("#219DB8FF"));
    private static readonly IBrush TipBackground = new ImmutableSolidColorBrush(Color.Parse("#F20B1122"));
    private static readonly IPen TipBorder = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#52AABEFF")), 1);
    private static readonly IPen Midnight = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#E69DB8FF")), 1, new ImmutableDashStyle([3, 3], 0));
    private static readonly Color Dusk = Color.Parse("#6D8CFF"), Dawn = Color.Parse("#FFB46B");
    private double _growth = 1;
    private int _hover = -1;
    private Point _pointer;

    static NightSky() => AffectsRender<NightSky>(NightsProperty, ShownProperty);

    public NightSky() => Cursor = new Cursor(StandardCursorType.Hand);

    public event EventHandler<int>? NightInvoked;

    public IReadOnlyList<SkyNight>? Nights { get => GetValue(NightsProperty); set => SetValue(NightsProperty, value); }
    public int Shown { get => GetValue(ShownProperty); set => SetValue(ShownProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != NightsProperty) return;
        _growth = 0;
        _ = Motion.Tween(TopLevel.GetTopLevel(this), TimeSpan.FromMilliseconds(1400), t => t, t => { _growth = t; InvalidateVisual(); }, TimeSpan.FromMilliseconds(200));
    }

    private Rect Plot => new(LabelWidth, 2, Math.Max(10, Bounds.Width - LabelWidth - 4), Math.Max(10, Bounds.Height - AxisHeight - 4));

    private double RowHeight(int count) => count == 0 ? 0 : Math.Min(28, Plot.Height / count);

    private double X(double hour) => Plot.X + (hour - StartHour) / (EndHour - StartHour) * Plot.Width;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _pointer = e.GetPosition(this);
        var nights = Nights ?? [];
        var row = RowHeight(nights.Count);
        var hit = row <= 0 ? -1 : (int)Math.Floor((_pointer.Y - Plot.Y) / row);
        _hover = hit >= 0 && hit < nights.Count ? hit : -1;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = -1;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_hover >= 0) NightInvoked?.Invoke(this, _hover);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0) return;
        var nights = Nights ?? [];
        if (nights.Count == 0)
        {
            var empty = new FormattedText(CanvasText.T("Nessuna notte. Analizza il progetto."), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 11, Muted);
            context.DrawText(empty, new Point(0, bounds.Height / 2 - 8));
            return;
        }

        var plot = Plot;
        var row = RowHeight(nights.Count);
        var used = new Rect(plot.X, plot.Y, plot.Width, row * nights.Count);
        var hasSite = nights.Any(night => night.SunAltitudes is { Count: > 0 });
        if (!hasSite)
        {
            // Without SITELAT/SITELONG: a typical dusk and dawn at the edges.
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(56, Dusk.R, Dusk.G, Dusk.B), 0), new GradientStop(Color.FromArgb(15, Dusk.R, Dusk.G, Dusk.B), 0.32),
                    new GradientStop(Color.FromArgb(0, Dusk.R, Dusk.G, Dusk.B), 0.44), new GradientStop(Color.FromArgb(0, Dawn.R, Dawn.G, Dawn.B), 0.77),
                    new GradientStop(Color.FromArgb(18, Dawn.R, Dawn.G, Dawn.B), 0.88), new GradientStop(Color.FromArgb(56, Dawn.R, Dawn.G, Dawn.B), 1)
                }
            };
            context.DrawRectangle(brush, null, used, 4, 4);
        }

        var midnight = X(24);
        var glow = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0, 157, 184, 255), 0), new GradientStop(Color.FromArgb(40, 157, 184, 255), 0.5), new GradientStop(Color.FromArgb(0, 157, 184, 255), 1) }
        };
        context.DrawRectangle(glow, null, new Rect(midnight - 26, used.Y, 52, used.Height));

        for (var index = 0; index < nights.Count; index++)
        {
            var night = nights[index];
            var y = plot.Y + index * row;
            var on = index < Shown;
            using var fade = context.PushOpacity(on ? (index == _hover || _hover < 0 ? 1 : 0.8) : 0.28);

            if (night.SunAltitudes is { Count: > 1 } altitudes)
            {
                var slice = plot.Width / (altitudes.Count - 1);
                for (var s = 0; s < altitudes.Count - 1; s++)
                {
                    var altitude = (altitudes[s] + altitudes[s + 1]) / 2;
                    if (altitude < -18) continue;
                    var strength = Math.Clamp((altitude + 18) / 18, 0, 1);
                    var colour = s < (altitudes.Count - 1) / 2 ? Dusk : Dawn;
                    var alpha = (byte)(10 + 60 * strength * strength);
                    context.DrawRectangle(new SolidColorBrush(Color.FromArgb(alpha, colour.R, colour.G, colour.B)), null, new Rect(plot.X + s * slice, y, slice + 0.5, row - 0.6));
                }
            }

            var label = new FormattedText(night.Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, Math.Min(10, row * 0.78), index == _hover ? Fg : Muted);
            context.DrawText(label, new Point(0, y + (row - label.Height) / 2));
            DrawMoon(context, new Point(LabelWidth - 9, y + row / 2), Math.Clamp(row / 2 - 1.4, 2, 6), night.MoonAge);

            // Ticks appear night by night, left to right, like the sky filling in.
            var local = Math.Clamp((_growth * (nights.Count + 6) - index) / 6, 0, 1);
            var reach = StartHour + (EndHour - StartHour) * Motion.EaseOutExpo(local);
            var tickHeight = Math.Clamp(row - 2.4, 1.5, 14);
            foreach (var exposure in night.Exposures)
            {
                if (exposure.Start > reach) continue;
                var x = X(exposure.Start);
                var width = Math.Max(1.4, X(exposure.Start + exposure.Duration) - x - 1.2);
                var colour = exposure.Flagged ? Color.Parse("#FFC27A") : exposure.Color;
                context.DrawRectangle(new SolidColorBrush(colour), null, new Rect(x, y + (row - tickHeight) / 2, width, tickHeight), 1, 1);
            }
        }

        context.DrawLine(Midnight, new Point(midnight, used.Y), new Point(midnight, used.Bottom + 3));
        foreach (var hour in new[] { 18, 20, 22, 24, 26, 28, 30 })
        {
            var text = hour == 24 ? CanvasText.T("00 mezzanotte") : (hour % 24).ToString("00", CultureInfo.InvariantCulture);
            var axis = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 9, hour == 24 ? Accent : Dim);
            var x = Math.Clamp(X(hour) - axis.Width / 2, plot.X, bounds.Width - axis.Width);
            context.DrawText(axis, new Point(x, used.Bottom + 4));
        }

        if (_hover >= 0) DrawTip(context, nights[_hover], bounds);
    }

    private void DrawTip(DrawingContext context, SkyNight night, Rect bounds)
    {
        var text = new FormattedText(night.Detail, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Body, 11.5, Fg);
        var box = new Rect(_pointer.X + 14, _pointer.Y - 34, text.Width + 20, text.Height + 12);
        if (box.Right > bounds.Width) box = box.WithX(_pointer.X - box.Width - 14);
        if (box.Y < 0) box = box.WithY(_pointer.Y + 14);
        context.DrawRectangle(TipBackground, TipBorder, box, 9, 9);
        context.DrawText(text, new Point(box.X + 10, box.Y + 6));
    }

    /// <summary>The Moon at <paramref name="age"/> days: lit on the right while waxing, on the left while waning.</summary>
    private static void DrawMoon(DrawingContext context, Point centre, double radius, double age)
    {
        context.DrawEllipse(MoonDark, null, centre, radius, radius);
        var phase = age / 29.530588853;
        var f = Math.Cos(2 * Math.PI * phase);
        var lit = (1 - f) / 2;
        if (lit < 0.02) return;
        var waxing = phase < 0.5;
        var top = new Point(centre.X, centre.Y - radius);
        var bottom = new Point(centre.X, centre.Y + radius);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(top, true);
            g.ArcTo(bottom, new Size(radius, radius), 0, false, waxing ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
            var terminator = Math.Max(0.01, Math.Abs(f) * radius);
            var backSweep = waxing ? (f > 0 ? SweepDirection.CounterClockwise : SweepDirection.Clockwise) : (f > 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
            g.ArcTo(top, new Size(terminator, radius), 0, false, backSweep);
            g.EndFigure(true);
        }
        context.DrawGeometry(new SolidColorBrush(Color.FromArgb((byte)(255 * (0.35 + 0.6 * lit)), 232, 236, 255)), null, geometry);
    }
}
