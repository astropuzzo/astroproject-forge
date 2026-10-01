using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>One filter flowing through WBPP: its colour, its Lights and the integration it brings.</summary>
public sealed record PipelineStream(string Filter, Color Colour, int Lights, double Seconds);

/// <summary>
/// What WeightedBatchPreprocessing will do with the project, drawn as light: each filter is a stream of its own
/// colour that crosses calibration, gathers at registration around one reference and leaves as its own master.
/// Grains of light keep flowing along the streams while the screen is visible.
/// </summary>
public sealed class PipelineFlow : AmbientControl
{
    public static readonly StyledProperty<IReadOnlyList<PipelineStream>?> StreamsProperty =
        AvaloniaProperty.Register<PipelineFlow, IReadOnlyList<PipelineStream>?>(nameof(Streams));
    public static readonly StyledProperty<IReadOnlyList<string>?> KeywordsProperty =
        AvaloniaProperty.Register<PipelineFlow, IReadOnlyList<string>?>(nameof(Keywords));

    private static readonly Typeface Mono = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#JetBrains Mono"));
    private static readonly Typeface MonoBold = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#JetBrains Mono"), weight: FontWeight.Medium);
    private static readonly Typeface Body = new(new FontFamily("Inter"), weight: FontWeight.SemiBold);
    private static readonly IBrush Fg = new ImmutableSolidColorBrush(Color.Parse("#EEF1FF"));
    private static readonly IBrush Muted = new ImmutableSolidColorBrush(Color.Parse("#8E97BD"));
    private static readonly IBrush Dim = new ImmutableSolidColorBrush(Color.Parse("#5B6490"));
    private static readonly IBrush AccentBrush = new ImmutableSolidColorBrush(Color.Parse("#9DB8FF"));
    private static readonly IBrush StageFill = new ImmutableLinearGradientBrush(
        [new ImmutableGradientStop(0, Color.Parse("#2A1A2448")), new ImmutableGradientStop(1, Color.Parse("#140C1230"))],
        startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));
    private static readonly IPen StageRim = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#3396AAFF")), 1);
    private static readonly IBrush ChipFill = new ImmutableSolidColorBrush(Color.Parse("#229DB8FF"));
    private static readonly IPen ChipRim = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#669DB8FF")), 1);

    private static readonly (string Title, string Detail)[] Stages =
    [
        ("LIGHT", "dal progetto"),
        ("CALIBRAZIONE", "Flat · Dark · Bias"),
        ("REGISTRAZIONE", "StarAlignment"),
        ("INTEGRAZIONE", "un master per filtro")
    ];

    private double _reveal = 1;

    static PipelineFlow() => AffectsRender<PipelineFlow>(KeywordsProperty);

    public IReadOnlyList<PipelineStream>? Streams { get => GetValue(StreamsProperty); set => SetValue(StreamsProperty, value); }
    public IReadOnlyList<string>? Keywords { get => GetValue(KeywordsProperty); set => SetValue(KeywordsProperty, value); }

    protected override bool WantsAmbient => Streams is { Count: > 0 };

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != StreamsProperty) return;
        _reveal = 0;
        _ = Motion.Tween(TopLevel.GetTopLevel(this), TimeSpan.FromMilliseconds(1400), t => t, t => { _reveal = t; InvalidateVisual(); });
    }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 720 : availableSize.Width, 306);

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0) return;
        var culture = CultureInfo.CurrentCulture;
        var streams = Streams ?? [];
        var left = 16.0;
        var right = Bounds.Width - 190;
        var top = 84.0;
        var bottom = Bounds.Height - 44;
        var stageX = Enumerable.Range(0, Stages.Length).Select(index => left + (right - left) * index / (Stages.Length - 1)).ToArray();

        // Stage columns: glass panes the light passes through.
        var titles = Stages.Select((stage, index) => new FormattedText(CanvasText.T(stage.Title), culture, FlowDirection.LeftToRight, MonoBold, 9.5, index == 0 ? Dim : AccentBrush)).ToArray();
        var details = Stages.Select(stage => new FormattedText(CanvasText.T(stage.Detail), culture, FlowDirection.LeftToRight, Mono, 9.5, Muted) { MaxTextWidth = 150 }).ToArray();
        // The first column's note wraps into the room the second one leaves it, instead of running under it.
        var firstRoom = stageX[1] - Math.Max(titles[1].Width, details[1].Width) / 2 - 4 - (stageX[0] - 4);
        details[0].MaxTextWidth = Math.Clamp(firstRoom, 46, 150);
        details[0].MaxLineCount = 2;
        for (var index = 0; index < Stages.Length; index++)
        {
            var x = stageX[index];
            var pane = new Rect(x - 13, top - 8, 26, bottom - top + 16);
            if (index > 0) context.DrawRectangle(StageFill, StageRim, pane, 13, 13);
            var title = titles[index];
            var detail = details[index];
            var labelX = index == 0 ? x - 4 : Math.Clamp(x - title.Width / 2, 0, Bounds.Width - title.Width);
            context.DrawText(title, new Point(labelX, 4));
            context.DrawText(detail, new Point(index == 0 ? x - 4 : Math.Clamp(x - detail.Width / 2, 0, Bounds.Width - detail.Width), 20));
        }

        // The grouping keywords WBPP needs sit on the calibration pane.
        var chipX = stageX[1] - 13;
        var chipY = bottom + 14;
        foreach (var keyword in Keywords ?? [])
        {
            var text = new FormattedText(keyword, culture, FlowDirection.LeftToRight, MonoBold, 9.5, AccentBrush);
            var chip = new Rect(chipX, chipY, text.Width + 16, 20);
            context.DrawRectangle(ChipFill, ChipRim, chip, 10, 10);
            context.DrawText(text, new Point(chip.X + 8, chip.Y + 3));
            chipX = chip.Right + 6;
        }

        if (streams.Count == 0)
        {
            var empty = new FormattedText(CanvasText.T("Nessun dato. Analizza il progetto."), culture, FlowDirection.LeftToRight, Mono, 11, Muted);
            context.DrawText(empty, new Point(stageX[1] + 24, (top + bottom) / 2 - 8));
            return;
        }

        var maxSeconds = Math.Max(1, streams.Max(stream => stream.Seconds));
        var centre = (top + bottom) / 2;
        var span = (bottom - top) / 2;
        double Lane(int index, double spread) => streams.Count == 1 ? centre : centre + span * spread * (index / (double)(streams.Count - 1) * 2 - 1);
        double[] spreads = [1, 0.85, 0.28, 0.9];

        for (var index = 0; index < streams.Count; index++)
        {
            var stream = streams[index];
            var points = Enumerable.Range(0, Stages.Length).Select(stage => new Point(stageX[stage], Lane(index, spreads[stage]))).ToArray();
            var path = Sample(points, 28);
            var drawn = Math.Clamp(_reveal * 1.25 - index * 0.08, 0, 1);
            if (drawn <= 0) continue;
            var visible = path.Take(Math.Max(2, (int)(path.Count * Motion.EaseInOutCubic(drawn)))).ToList();
            var width = 3 + 9 * Math.Sqrt(stream.Seconds / maxSeconds);

            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(visible[0], false);
                foreach (var point in visible.Skip(1)) g.LineTo(point);
                g.EndFigure(false);
            }
            var c = stream.Colour;
            var ribbon = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(20, c.R, c.G, c.B), 0), new GradientStop(Color.FromArgb(120, c.R, c.G, c.B), 0.7), new GradientStop(Color.FromArgb(200, c.R, c.G, c.B), 1) }
            };
            context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(28, c.R, c.G, c.B)), width * 2.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
            context.DrawGeometry(null, new Pen(ribbon, width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);

            // Grains of light: more Lights, more grains.
            var grains = Math.Clamp(stream.Lights, 3, 14);
            var lengths = Cumulative(path);
            for (var grain = 0; grain < grains; grain++)
            {
                var u = (Clock * 0.11 * (1 + index * 0.07) + grain / (double)grains + index * 0.13) % 1;
                if (u > Motion.EaseInOutCubic(drawn)) continue;
                var point = At(path, lengths, u);
                var alpha = (byte)(255 * Math.Sin(Math.PI * u));
                context.DrawEllipse(new ImmutableSolidColorBrush(Color.FromArgb((byte)(alpha / 3), c.R, c.G, c.B)), null, point, width * 0.9, width * 0.9);
                context.DrawEllipse(new ImmutableSolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)), null, point, 1.6, 1.6);
            }

            // Source label and the master it becomes.
            using (context.PushOpacity(Math.Clamp(drawn * 3, 0, 1)))
            {
                var source = new FormattedText($"{stream.Filter}", culture, FlowDirection.LeftToRight, Body, 11, Fg) { MaxTextWidth = stageX[1] - stageX[0] - 30, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
                context.DrawText(source, new Point(points[0].X + 4, points[0].Y - source.Height - width * 1.3 - 2));
            }
            if (drawn >= 1)
            {
                var end = points[^1];
                var pulse = Motion.Reduced ? 0 : 0.5 + 0.5 * Math.Sin(Clock * 2 + index);
                context.DrawEllipse(new ImmutableSolidColorBrush(Color.FromArgb((byte)(40 + 40 * pulse), c.R, c.G, c.B)), null, end, width + 8 + 3 * pulse, width + 8 + 3 * pulse);
                context.DrawEllipse(new ImmutableSolidColorBrush(c), new Pen(Fg, 1.2), end, width * 0.7 + 3, width * 0.7 + 3);
                var master = new FormattedText($"Master {stream.Filter}", culture, FlowDirection.LeftToRight, Body, 12, Fg) { MaxTextWidth = 160, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
                var detail = new FormattedText($"{stream.Lights} light · {Hours(stream.Seconds)}", culture, FlowDirection.LeftToRight, Mono, 9.5, Muted);
                context.DrawText(master, new Point(end.X + width + 16, end.Y - master.Height + 1));
                context.DrawText(detail, new Point(end.X + width + 16, end.Y + 2));
            }
        }
    }

    private static string Hours(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes:00}m" : $"{span.Minutes}m";
    }

    /// <summary>A smooth path through the stage points, leaving and entering each pane horizontally.</summary>
    private static List<Point> Sample(IReadOnlyList<Point> points, int perSegment)
    {
        var result = new List<Point> { points[0] };
        for (var index = 0; index < points.Count - 1; index++)
        {
            var a = points[index];
            var b = points[index + 1];
            var handle = (b.X - a.X) * 0.5;
            var c1 = new Point(a.X + handle, a.Y);
            var c2 = new Point(b.X - handle, b.Y);
            for (var step = 1; step <= perSegment; step++)
            {
                var t = step / (double)perSegment;
                var u = 1 - t;
                result.Add(new Point(
                    u * u * u * a.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * b.X,
                    u * u * u * a.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * b.Y));
            }
        }
        return result;
    }

    private static double[] Cumulative(IReadOnlyList<Point> path)
    {
        var lengths = new double[path.Count];
        for (var index = 1; index < path.Count; index++) lengths[index] = lengths[index - 1] + ((Vector)(path[index] - path[index - 1])).Length;
        return lengths;
    }

    private static Point At(IReadOnlyList<Point> path, double[] lengths, double u)
    {
        var target = lengths[^1] * u;
        var index = Array.BinarySearch(lengths, target);
        if (index >= 0) return path[index];
        index = ~index;
        if (index <= 0) return path[0];
        if (index >= path.Count) return path[^1];
        var t = (target - lengths[index - 1]) / Math.Max(1e-6, lengths[index] - lengths[index - 1]);
        return path[index - 1] + (path[index] - path[index - 1]) * t;
    }
}
