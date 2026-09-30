using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AstroForge.App.ViewModels;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>
/// The series as a small sky: every Light is a star placed by sharpness (FWHM, left is sharper) and signal (SNR, up is
/// stronger), as large as the stars it caught. The best subs gather top-left and twinkle; suspects pulse amber;
/// excluded frames fade to hollow rings. Click a star to inspect that frame.
/// </summary>
public sealed class QualitySky : AmbientControl
{
    public static readonly StyledProperty<IEnumerable<QualityFrameRow>?> ItemsProperty =
        AvaloniaProperty.Register<QualitySky, IEnumerable<QualityFrameRow>?>(nameof(Items));
    public static readonly StyledProperty<QualityFrameRow?> SelectedItemProperty =
        AvaloniaProperty.Register<QualitySky, QualityFrameRow?>(nameof(SelectedItem), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> ThresholdProperty =
        AvaloniaProperty.Register<QualitySky, double>(nameof(Threshold), 3.5);

    private static readonly Typeface Mono = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#JetBrains Mono"));
    private static readonly Color Star = Color.Parse("#DDE4FF"), Oiii = Color.Parse("#3FE0D0"), Warn = Color.Parse("#FFC27A"), MutedColour = Color.Parse("#5B6490");
    private static readonly IBrush Muted = new ImmutableSolidColorBrush(Color.Parse("#8E97BD"));
    private static readonly IBrush Dim = new ImmutableSolidColorBrush(Color.Parse("#5B6490"));
    private static readonly IBrush Fg = new ImmutableSolidColorBrush(Color.Parse("#EEF1FF"));
    private static readonly IPen Grid = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#1296AAFF")), 1, new ImmutableDashStyle([2, 4], 0));
    private static readonly IPen Reticle = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#EEF1FF")), 1);

    private readonly List<(Point At, double Radius, QualityFrameRow Row)> _stars = [];
    private QualityFrameRow? _hover;
    private double _reveal = 1;

    static QualitySky() => AffectsRender<QualitySky>(SelectedItemProperty, ThresholdProperty);

    public QualitySky() => Cursor = new Cursor(StandardCursorType.Hand);

    public IEnumerable<QualityFrameRow>? Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public QualityFrameRow? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public double Threshold { get => GetValue(ThresholdProperty); set => SetValue(ThresholdProperty, value); }

    protected override bool WantsAmbient => _stars.Count > 0;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ItemsProperty) return;
        _reveal = 0;
        _ = Motion.Tween(TopLevel.GetTopLevel(this), TimeSpan.FromMilliseconds(1000), t => t, t => { _reveal = t; InvalidateVisual(); });
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = HitTest(e.GetPosition(this));
        if (hit == _hover) return;
        _hover = hit;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (HitTest(e.GetPosition(this)) is { } row) { SelectedItem = row; e.Handled = true; }
    }

    private QualityFrameRow? HitTest(Point point) =>
        _stars.Where(star => ((Vector)(star.At - point)).Length <= Math.Max(8, star.Radius + 4))
            .OrderBy(star => ((Vector)(star.At - point)).Length).Select(star => star.Row).FirstOrDefault();

    public override void Render(DrawingContext context)
    {
        _stars.Clear();
        if (Bounds.Width <= 0) return;
        var culture = CultureInfo.CurrentCulture;
        var rows = Items?.Where(row => row.Error is null && row.Fwhm > 0).ToList() ?? [];
        var plot = new Rect(34, 12, Math.Max(10, Bounds.Width - 48), Math.Max(10, Bounds.Height - 40));
        if (rows.Count == 0)
        {
            var empty = new FormattedText(CanvasText.T("Nessuna misura. Analizza la serie."), culture, FlowDirection.LeftToRight, Mono, 11, Muted);
            context.DrawText(empty, new Point(plot.X, plot.Center.Y - 8));
            return;
        }

        double minF = rows.Min(row => row.Fwhm), maxF = rows.Max(row => row.Fwhm);
        double minS = rows.Min(row => row.Snr), maxS = rows.Max(row => row.Snr);
        var padF = Math.Max(0.15, (maxF - minF) * 0.15);
        var padS = Math.Max(0.5, (maxS - minS) * 0.15);
        minF -= padF; maxF += padF; minS -= padS; maxS += padS;
        var maxStars = Math.Max(1, rows.Max(row => row.StarCount));

        for (var step = 0; step <= 3; step++)
        {
            var x = plot.X + plot.Width * step / 3;
            var y = plot.Y + plot.Height * step / 3;
            context.DrawLine(Grid, new Point(x, plot.Y), new Point(x, plot.Bottom));
            context.DrawLine(Grid, new Point(plot.X, y), new Point(plot.Right, y));
            var fwhm = new FormattedText((minF + (maxF - minF) * step / 3).ToString(maxF - minF < 0.6 ? "0.00" : "0.0", culture), culture, FlowDirection.LeftToRight, Mono, 9, Dim);
            context.DrawText(fwhm, new Point(Math.Clamp(x - fwhm.Width / 2, plot.X, plot.Right - fwhm.Width), plot.Bottom + 4));
            var snr = new FormattedText((maxS - (maxS - minS) * step / 3).ToString(maxS - minS < 6 ? "0.0" : "0", culture), culture, FlowDirection.LeftToRight, Mono, 9, Dim);
            context.DrawText(snr, new Point(plot.X - snr.Width - 6, y - 6));
        }
        var xAxis = new FormattedText(CanvasText.T("FWHM px"), culture, FlowDirection.LeftToRight, Mono, 9, Muted);
        context.DrawText(xAxis, new Point(plot.Right - xAxis.Width, plot.Bottom + 16));
        var yAxis = new FormattedText("SNR", culture, FlowDirection.LeftToRight, Mono, 9, Muted);
        context.DrawText(yAxis, new Point(plot.X + 4, plot.Y));

        var ordered = rows.OrderBy(row => row.Path, StringComparer.Ordinal).ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            var row = ordered[index];
            var at = new Point(plot.X + (row.Fwhm - minF) / (maxF - minF) * plot.Width, plot.Bottom - (row.Snr - minS) / (maxS - minS) * plot.Height);
            var radius = 2.5 + 6 * Math.Sqrt(row.StarCount / (double)maxStars);
            _stars.Add((at, radius, row));
            var appear = Math.Clamp(_reveal * (ordered.Count + 6) - index, 0, 6) / 6;
            if (appear <= 0) continue;

            // Quality rank 0..1: sharp and strong subs shine more.
            var rank = 1 - Math.Clamp(((at.X - plot.X) / plot.Width + (at.Y - plot.Y) / plot.Height) / 2, 0, 1);
            var twinkle = Motion.Reduced ? 1 : 0.85 + 0.15 * Math.Sin(Clock * (1.3 + index % 5 * 0.37) + index * 1.7);
            var colour = row.IsExcluded ? MutedColour : row.IsSuspect ? Warn : Color.FromRgb(
                (byte)(Star.R + (Oiii.R - Star.R) * rank * 0.35), (byte)(Star.G + (Oiii.G - Star.G) * rank * 0.35), (byte)(Star.B + (Oiii.B - Star.B) * rank * 0.35));
            using (context.PushOpacity(appear))
            {
                if (row.IsExcluded)
                {
                    context.DrawEllipse(null, new Pen(new SolidColorBrush(colour), 1.2), at, radius, radius);
                    context.DrawLine(new Pen(new SolidColorBrush(colour), 1.2), at + new Vector(-radius, -radius) * 0.7, at + new Vector(radius, radius) * 0.7);
                    continue;
                }
                var glow = radius * (2.2 + rank * 1.4) * twinkle;
                var halo = new RadialGradientBrush
                {
                    GradientStops = { new GradientStop(Color.FromArgb((byte)(90 + 80 * rank), colour.R, colour.G, colour.B), 0), new GradientStop(Color.FromArgb(0, colour.R, colour.G, colour.B), 1) }
                };
                context.DrawEllipse(halo, null, at, glow, glow);
                if (rank > 0.55 && !row.IsSuspect)
                {
                    // Diffraction spikes on the best subs, like a bright star through a reflector.
                    var spike = radius * (2.6 + 1.6 * rank) * twinkle;
                    var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(120 * rank), colour.R, colour.G, colour.B)), 1);
                    context.DrawLine(pen, at - new Vector(spike, 0), at + new Vector(spike, 0));
                    context.DrawLine(pen, at - new Vector(0, spike), at + new Vector(0, spike));
                }
                context.DrawEllipse(new ImmutableSolidColorBrush(colour), null, at, radius * 0.55, radius * 0.55);
                if (row.IsSuspect)
                {
                    var pulse = Motion.Reduced ? 0.5 : 0.5 + 0.5 * Math.Sin(Clock * 3 + index);
                    context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(120 + 100 * pulse), Warn.R, Warn.G, Warn.B)), 1.3), at, radius + 3 + 3 * pulse, radius + 3 + 3 * pulse);
                }
            }
        }

        foreach (var (at, radius, row) in _stars.Where(star => star.Row == SelectedItem || star.Row == _hover))
        {
            var selected = row == SelectedItem;
            if (selected)
            {
                // A finder reticle around the inspected frame.
                var r = radius + 9;
                context.DrawEllipse(null, Reticle, at, r, r);
                foreach (var direction in new[] { new Vector(1, 0), new Vector(-1, 0), new Vector(0, 1), new Vector(0, -1) })
                    context.DrawLine(Reticle, at + direction * (r - 3), at + direction * (r + 6));
            }
            var label = new FormattedText($"{row.FileName} · FWHM {row.FwhmText} · SNR {row.SnrText}", culture, FlowDirection.LeftToRight, Mono, 9.5, Fg) { MaxTextWidth = 320, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            var origin = new Point(Math.Clamp(at.X + radius + 14, 0, Math.Max(0, Bounds.Width - label.Width - 8)), Math.Clamp(at.Y - label.Height - 6, 0, Bounds.Height - label.Height));
            context.DrawRectangle(new ImmutableSolidColorBrush(Color.Parse("#D00A0F22")), null, new Rect(origin.X - 5, origin.Y - 2, label.Width + 10, label.Height + 4), 5, 5);
            context.DrawText(label, origin);
        }
    }
}
