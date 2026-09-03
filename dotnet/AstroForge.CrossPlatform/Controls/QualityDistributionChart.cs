using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AstroForge.App.ViewModels;

namespace AstroForge.CrossPlatform.Controls;

public sealed class QualityDistributionChart : Control
{
    public static readonly StyledProperty<IEnumerable<QualityFrameRow>?> ItemsProperty =
        AvaloniaProperty.Register<QualityDistributionChart, IEnumerable<QualityFrameRow>?>(nameof(Items));
    public static readonly StyledProperty<double> ThresholdProperty =
        AvaloniaProperty.Register<QualityDistributionChart, double>(nameof(Threshold), 3.5);
    public static readonly StyledProperty<QualityFrameRow?> SelectedItemProperty =
        AvaloniaProperty.Register<QualityDistributionChart, QualityFrameRow?>(nameof(SelectedItem), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    private IReadOnlyList<QualityFrameRow> _rows = [];
    private readonly List<(Point Point, QualityFrameRow Row)> _points = [];
    private Rect _plotBounds;
    private double _maximum = 1;
    public IEnumerable<QualityFrameRow>? Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public double Threshold { get => GetValue(ThresholdProperty); set => SetValue(ThresholdProperty, value); }
    public QualityFrameRow? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }

    static QualityDistributionChart()
    {
        AffectsRender<QualityDistributionChart>(ItemsProperty, ThresholdProperty, SelectedItemProperty);
        AffectsMeasure<QualityDistributionChart>(ItemsProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        _rows = Items?.Where(row => row.Error is null).OrderBy(row => row.OutlierScore).ToArray() ?? [];
        var bounds = Bounds.Deflate(14);
        if (_rows.Count == 0 || bounds.Width < 120 || bounds.Height < 80)
        {
            DrawText(context, "La distribuzione apparirà dopo l’analisi della serie", new Point(bounds.X, bounds.Y + 18), "#7F93AB", 12);
            return;
        }

        var plot = new Rect(bounds.X + 44, bounds.Y + 26, bounds.Width - 58, bounds.Height - 66);
        _plotBounds = plot;
        _maximum = Math.Max(6, Math.Max(_rows.Max(row => row.OutlierScore) * 1.06, Threshold * 1.18));
        var bins = Math.Clamp((int)(plot.Width / 65), 8, 28);
        var counts = new int[bins];
        foreach (var row in _rows) counts[Math.Min(bins - 1, (int)(row.OutlierScore / _maximum * bins))]++;
        var maxCount = Math.Max(1, counts.Max());
        var thresholdX = plot.X + Math.Clamp(Threshold / _maximum, 0, 1) * plot.Width;

        context.DrawRectangle(Brush.Parse("#25101A26"), null, new Rect(plot.X, plot.Y, Math.Max(0, thresholdX - plot.X), plot.Height));
        context.DrawRectangle(Brush.Parse("#251E160D"), null, new Rect(thresholdX, plot.Y, Math.Max(0, plot.Right - thresholdX), plot.Height));

        for (var grid = 0; grid <= 4; grid++)
        {
            var y = plot.Y + plot.Height * grid / 4;
            context.DrawLine(new Pen(Brush.Parse("#243246"), 1), new Point(plot.X, y), new Point(plot.Right, y));
            DrawText(context, Math.Round(maxCount * (4 - grid) / 4d).ToString("0"), new Point(bounds.X + 4, y - 7), "#61758E", 9);
        }

        var binWidth = plot.Width / bins;
        for (var index = 0; index < bins; index++)
        {
            var height = plot.Height * counts[index] / maxCount;
            var left = plot.X + index * binWidth + 1;
            var centerScore = _maximum * (index + .5) / bins;
            var color = centerScore >= Threshold ? "#B9FFAB56" : "#A83AD3C6";
            context.DrawRectangle(Brush.Parse(color), null, new Rect(left, plot.Bottom - height, Math.Max(2, binWidth - 3), height));
        }

        _points.Clear();
        for (var index = 0; index < _rows.Count; index++)
        {
            var row = _rows[index];
            var x = plot.X + row.OutlierScore / _maximum * plot.Width;
            var selected = ReferenceEquals(row, SelectedItem);
            var point = new Point(x, plot.Bottom + 8 + index % 3 * 5);
            _points.Add((point, row));
            var brush = selected ? Brush.Parse("#70FFF0") : row.IsExcluded ? Brush.Parse("#BE6BFF") : row.IsSuspect ? Brush.Parse("#FFAA55") : Brush.Parse("#90A8C2");
            context.DrawEllipse(brush, selected ? new Pen(Brushes.White, 1) : null, point, selected ? 4 : row.IsSuspect || row.IsExcluded ? 3.3 : 2.5, selected ? 4 : row.IsSuspect || row.IsExcluded ? 3.3 : 2.5);
        }

        context.DrawLine(new Pen(Brush.Parse("#FFAA55"), 1.5, dashStyle: new DashStyle([4, 3], 0)), new Point(thresholdX, plot.Y), new Point(thresholdX, plot.Bottom + 13));
        DrawText(context, $"σ {Threshold:0.0}", new Point(Math.Min(plot.Right - 42, thresholdX + 5), plot.Y + 2), "#FFAA55", 10);
        if (SelectedItem is { Error: null })
        {
            var selectedX = plot.X + Math.Clamp(SelectedItem.OutlierScore / _maximum, 0, 1) * plot.Width;
            context.DrawLine(new Pen(Brush.Parse("#70FFF0"), 1.3, dashStyle: new DashStyle([3, 3], 0)), new Point(selectedX, plot.Y), new Point(selectedX, plot.Bottom + 13));
        }

        for (var tick = 0; tick <= 4; tick++)
            DrawText(context, (_maximum * tick / 4d).ToString("0.0"), new Point(plot.X + plot.Width * tick / 4d - 7, bounds.Bottom - 12), "#71859D", 9);
        var suspectCount = _rows.Count(row => row.IsSuspect);
        DrawText(context, $"SCORE · {_rows.Count} FRAME", new Point(plot.X, bounds.Y + 3), "#ADC0D7", 10);
        DrawText(context, $"OK {_rows.Count - suspectCount}", new Point(plot.X + 120, bounds.Y + 3), "#3AD3C6", 10);
        DrawText(context, $"CHECK {suspectCount}", new Point(plot.X + 180, bounds.Y + 3), "#FFAB56", 10);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_points.Count == 0) return;
        var pointer = e.GetPosition(this);
        var hit = _points.Select(item =>
            {
                var dx = item.Point.X - pointer.X;
                var dy = item.Point.Y - pointer.Y;
                return (item.Row, Horizontal: Math.Abs(dx), Distance: Math.Sqrt(dx * dx + dy * dy));
            })
            .OrderBy(item => item.Distance).First();
        if (hit.Distance > 18 && (!_plotBounds.Contains(pointer) || hit.Horizontal > 12)) return;
        SelectedItem = hit.Row;
        InvalidateVisual();
    }

    private static void DrawText(DrawingContext context, string value, Point point, string color, double size)
    {
        var text = new FormattedText(value, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, size, Brush.Parse(color));
        context.DrawText(text, point);
    }
}
