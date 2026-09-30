using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AstroForge.CrossPlatform.Controls;

public sealed record NightSegment(Color Color, double Hours);
public sealed record NightBar(string Label, IReadOnlyList<NightSegment> Segments)
{
    public double Hours => Segments.Sum(segment => segment.Hours);
}

/// <summary>
/// Every clear night of the project as a column of hours, stacked by filter in the filter's own
/// colour, with the cumulative integration climbing across them. Columns rise in sequence.
/// </summary>
public sealed class NightsTimeline : Control
{
    public static readonly StyledProperty<IReadOnlyList<NightBar>?> NightsProperty =
        AvaloniaProperty.Register<NightsTimeline, IReadOnlyList<NightBar>?>(nameof(Nights));

    private static readonly Typeface Mono = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#JetBrains Mono"));
    private static readonly IBrush Muted = new ImmutableSolidColorBrush(Color.Parse("#8E97BD"));
    private static readonly IBrush Dim = new ImmutableSolidColorBrush(Color.Parse("#5B6490"));
    private static readonly IPen Grid = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#1496AAFF")), 1);
    private static readonly IPen Cumulative = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#EEF1FF")), 1.4, lineCap: PenLineCap.Round);
    private double _growth = 1;

    public IReadOnlyList<NightBar>? Nights { get => GetValue(NightsProperty); set => SetValue(NightsProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != NightsProperty) return;
        _growth = 0;
        _ = Motion.Tween(TopLevel.GetTopLevel(this), TimeSpan.FromMilliseconds(1100), t => t, t => { _growth = t; InvalidateVisual(); });
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width, 180);

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

        var plot = new Rect(34, 8, bounds.Width - 42, bounds.Height - 34);
        var maxHours = Math.Max(0.5, Math.Ceiling(nights.Max(night => night.Hours) * 2) / 2);
        var total = nights.Sum(night => night.Hours);
        for (var step = 0; step <= 2; step++)
        {
            var y = plot.Bottom - plot.Height * step / 2;
            context.DrawLine(Grid, new Point(plot.X, y), new Point(plot.Right, y));
            var label = new FormattedText($"{maxHours * step / 2:0.#}h", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 9, Dim);
            context.DrawText(label, new Point(0, y - 6));
        }

        var slot = plot.Width / nights.Count;
        var barWidth = Math.Clamp(slot * 0.56, 4, 34);
        var cumulative = new List<Point>();
        double running = 0;
        for (var index = 0; index < nights.Count; index++)
        {
            // Stagger: each column starts rising a little after the previous one.
            var local = Math.Clamp((_growth * (nights.Count + 4) - index) / 5, 0, 1);
            var eased = Motion.EaseOutExpo(local);
            var x = plot.X + slot * index + (slot - barWidth) / 2;
            var y = plot.Bottom;
            foreach (var segment in nights[index].Segments)
            {
                var height = segment.Hours / maxHours * plot.Height * eased;
                if (height <= 0) continue;
                var rect = new Rect(x, y - height, barWidth, height);
                var fill = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(segment.Color, 0), new GradientStop(Color.FromArgb(120, segment.Color.R, segment.Color.G, segment.Color.B), 1) }
                };
                context.DrawRectangle(fill, null, rect, 3, 3);
                y -= height + 1.5;
            }
            running += nights[index].Hours;
            cumulative.Add(new Point(x + barWidth / 2, plot.Bottom - running / total * plot.Height * Motion.EaseOutExpo(Math.Clamp(_growth * 1.3 - 0.2, 0, 1))));

            var every = Math.Max(1, (int)Math.Ceiling(60 / slot));
            if (index % every == 0)
            {
                var label = new FormattedText(nights[index].Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 9, Muted);
                context.DrawText(label, new Point(Math.Clamp(x + barWidth / 2 - label.Width / 2, plot.X, bounds.Width - label.Width), plot.Bottom + 8));
            }
        }

        if (cumulative.Count > 1)
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(cumulative[0], false);
                foreach (var point in cumulative.Skip(1)) g.LineTo(point);
                g.EndFigure(false);
            }
            using (context.PushOpacity(0.55)) context.DrawGeometry(null, Cumulative, geometry);
            var end = cumulative[^1];
            context.DrawEllipse(new ImmutableSolidColorBrush(Color.Parse("#EEF1FF")), null, end, 3, 3);
        }
    }
}
