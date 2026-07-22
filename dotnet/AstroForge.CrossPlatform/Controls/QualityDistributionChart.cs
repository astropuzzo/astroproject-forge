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
        AvaloniaProperty.Register<QualityDistributionChart, double>(nameof(Threshold), 4.0);
    public static readonly StyledProperty<QualityFrameRow?> SelectedItemProperty =
        AvaloniaProperty.Register<QualityDistributionChart, QualityFrameRow?>(nameof(SelectedItem), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    private IReadOnlyList<QualityFrameRow> _rows = [];
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

        var plot = new Rect(bounds.X + 40, bounds.Y + 10, bounds.Width - 52, bounds.Height - 38);
        _maximum = Math.Max(1, Math.Max(_rows.Max(row => row.OutlierScore), Threshold * 1.18));
        var bins = Math.Clamp((int)(plot.Width / 65), 8, 28);
        var counts = new int[bins];
        foreach (var row in _rows) counts[Math.Min(bins - 1, (int)(row.OutlierScore / _maximum * bins))]++;
        var maxCount = Math.Max(1, counts.Max());

        for (var grid = 0; grid <= 4; grid++)
        {
            var y = plot.Bottom - plot.Height * grid / 4;
            context.DrawLine(new Pen(Brush.Parse("#243246"), 1), new Point(plot.X, y), new Point(plot.Right, y));
            DrawText(context, $"{grid * 25}%", new Point(bounds.X, y - 7), "#61758E", 9);
        }

        var binWidth = plot.Width / bins;
        for (var index = 0; index < bins; index++)
        {
            var height = plot.Height * counts[index] / maxCount;
            var left = plot.X + index * binWidth + 1;
            context.DrawRectangle(Brush.Parse("#247F79"), null, new Rect(left, plot.Bottom - height, Math.Max(2, binWidth - 3), height));
        }

        var values = _rows.Select(row => row.OutlierScore).ToArray();
        var mean = values.Average();
        var deviation = Math.Max(.18, Math.Sqrt(values.Sum(value => Math.Pow(value - mean, 2)) / Math.Max(1, values.Length - 1)));
        Point? previous = null;
        for (var pixel = 0; pixel <= (int)plot.Width; pixel += 3)
        {
            var value = pixel / plot.Width * _maximum;
            var density = Math.Exp(-.5 * Math.Pow((value - mean) / deviation, 2));
            var point = new Point(plot.X + pixel, plot.Bottom - density * plot.Height * .82);
            if (previous is { } start) context.DrawLine(new Pen(Brush.Parse("#B8CBE1"), 1.6), start, point);
            previous = point;
        }

        foreach (var row in _rows)
        {
            var x = plot.X + row.OutlierScore / _maximum * plot.Width;
            var selected = ReferenceEquals(row, SelectedItem);
            var brush = selected ? Brush.Parse("#70FFF0") : row.IsSuspect ? Brush.Parse("#FFAA55") : Brush.Parse("#90A8C2");
            context.DrawEllipse(brush, selected ? new Pen(Brushes.White, 1) : null, new Point(x, plot.Bottom + 9), selected ? 4 : 2.6, selected ? 4 : 2.6);
        }

        var thresholdX = plot.X + Math.Min(1, Threshold / _maximum) * plot.Width;
        context.DrawLine(new Pen(Brush.Parse("#FFAA55"), 1.5, dashStyle: new DashStyle([4, 3], 0)), new Point(thresholdX, plot.Y), new Point(thresholdX, plot.Bottom + 13));
        DrawText(context, $"soglia {Threshold:0.0}σ", new Point(Math.Min(plot.Right - 75, thresholdX + 5), plot.Y + 2), "#FFAA55", 10);
        DrawText(context, "0", new Point(plot.X, plot.Bottom + 17), "#71859D", 9);
        DrawText(context, _maximum.ToString("0.0"), new Point(plot.Right - 22, plot.Bottom + 17), "#71859D", 9);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_rows.Count == 0) return;
        var plot = Bounds.Deflate(14);
        var left = plot.X + 40;
        var width = plot.Width - 52;
        var score = Math.Clamp((e.GetPosition(this).X - left) / width, 0, 1) * _maximum;
        SelectedItem = _rows.MinBy(row => Math.Abs(row.OutlierScore - score));
        InvalidateVisual();
    }

    private static void DrawText(DrawingContext context, string value, Point point, string color, double size)
    {
        var text = new FormattedText(value, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, size, Brush.Parse(color));
        context.DrawText(text, point);
    }
}
