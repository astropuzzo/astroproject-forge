using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AstroForge.App.ViewModels;

namespace AstroForge.App.Controls;

public sealed class QualityDistributionChart : FrameworkElement
{
    private readonly List<(Point Point, QualityFrameRow Row)> _points = [];
    private Rect _plotBounds;

    public QualityDistributionChart() => Cursor = Cursors.Hand;

    public event EventHandler<QualityFrameRow>? FrameSelected;
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable<QualityFrameRow>), typeof(QualityDistributionChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ThresholdProperty = DependencyProperty.Register(
        nameof(Threshold), typeof(double), typeof(QualityDistributionChart),
        new FrameworkPropertyMetadata(3.5d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SelectedScoreProperty = DependencyProperty.Register(
        nameof(SelectedScore), typeof(double), typeof(QualityDistributionChart),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<QualityFrameRow>? ItemsSource { get => (IEnumerable<QualityFrameRow>?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public double Threshold { get => (double)GetValue(ThresholdProperty); set => SetValue(ThresholdProperty, value); }
    public double SelectedScore { get => (double)GetValue(SelectedScoreProperty); set => SetValue(SelectedScoreProperty, value); }
    public void Refresh() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 120 || height < 90) return;

        dc.DrawRoundedRectangle(Brush("#080D14"), new Pen(Brush("#2B394A"), 1), new Rect(0.5, 0.5, width - 1, height - 1), 8, 8);
        var rows = ItemsSource?.Where(item => item.Error is null).ToArray() ?? [];
        if (rows.Length == 0)
        {
            Label(dc, "La distribuzione apparirà dopo l'analisi", 16, height / 2 - 8, Color.FromRgb(134, 150, 171), 11);
            return;
        }

        const int bins = 24;
        const double left = 46, right = 18, top = 36, bottom = 44;
        var plotWidth = Math.Max(1, width - left - right);
        var plotHeight = Math.Max(1, height - top - bottom);
        _plotBounds = new Rect(left, top, plotWidth, plotHeight);

        var maximumScore = rows.Max(item => item.OutlierScore);
        var maximum = Math.Max(6, Math.Max(Threshold * 1.18, maximumScore * 1.06));
        var thresholdX = left + plotWidth * Math.Clamp(Threshold / maximum, 0, 1);

        // The two zones mirror the exact rule used by the view model: score < threshold is accepted.
        dc.DrawRectangle(Brush("#101A2625"), null, new Rect(left, top, Math.Max(0, thresholdX - left), plotHeight));
        dc.DrawRectangle(Brush("#182B1D12"), null, new Rect(thresholdX, top, Math.Max(0, left + plotWidth - thresholdX), plotHeight));

        var counts = new int[bins];
        foreach (var row in rows)
            counts[Math.Clamp((int)(row.OutlierScore / maximum * bins), 0, bins - 1)]++;
        var peak = Math.Max(1, counts.Max());
        var gridPen = new Pen(Brush("#33495B71"), 1);
        for (var grid = 0; grid <= 4; grid++)
        {
            var y = top + plotHeight * grid / 4d;
            dc.DrawLine(gridPen, new Point(left, y), new Point(left + plotWidth, y));
            Label(dc, (100 - grid * 25) + "%", 8, y - 7, Color.FromRgb(101, 121, 145), 9);
        }

        var binWidth = plotWidth / bins;
        for (var index = 0; index < bins; index++)
        {
            var x = left + index * binWidth;
            var barHeight = counts[index] * plotHeight / peak;
            var binCenterScore = maximum * (index + 0.5) / bins;
            var color = binCenterScore >= Threshold ? "#B9FFAB56" : "#A83AD3C6";
            dc.DrawRectangle(Brush(color), null, new Rect(x + 1, top + plotHeight - barHeight, Math.Max(1, binWidth - 2), barHeight));
        }

        DrawMarker(dc, Math.Clamp(Threshold / maximum, 0, 1), Color.FromRgb(255, 171, 86), $"soglia {Threshold:0.0}", left, top, plotWidth, plotHeight);
        if (!double.IsNaN(SelectedScore))
            DrawMarker(dc, Math.Clamp(SelectedScore / maximum, 0, 1), Color.FromRgb(94, 233, 218), $"selezione {SelectedScore:0.0}", left, top, plotWidth, plotHeight);

        _points.Clear();
        var selectedRow = rows.FirstOrDefault(item => Math.Abs(item.OutlierScore - SelectedScore) < 0.0001);
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var point = new Point(left + plotWidth * Math.Clamp(row.OutlierScore / maximum, 0, 1), top + plotHeight + 8 + index % 2 * 7);
            _points.Add((point, row));
            var selected = ReferenceEquals(row, selectedRow);
            var color = selected ? Color.FromRgb(94, 233, 218) : row.IsExcluded ? Color.FromRgb(190, 107, 255) : row.IsSuspect ? Color.FromRgb(255, 171, 86) : Color.FromRgb(132, 151, 174);
            var radius = selected ? 4.2 : row.IsSuspect || row.IsExcluded ? 3.4 : 2.5;
            dc.DrawEllipse(new SolidColorBrush(color), null, point, radius, radius);
        }

        for (var tick = 0; tick <= 4; tick++)
        {
            var value = maximum * tick / 4d;
            Label(dc, value.ToString("0.0"), left + plotWidth * tick / 4d - 8, height - 22, Color.FromRgb(134, 150, 171), 9);
        }

        var suspectCount = rows.Count(item => item.IsSuspect);
        var acceptedCount = rows.Length - suspectCount;
        Label(dc, $"SCORE OUTLIER · {rows.Length} FRAME", left, 9, Color.FromRgb(173, 192, 215), 10);
        Label(dc, $"{acceptedCount} accettati", left + 150, 9, Color.FromRgb(58, 211, 198), 10);
        Label(dc, $"{suspectCount} da verificare", left + 245, 9, Color.FromRgb(255, 171, 86), 10);
        Label(dc, "clicca un punto per ispezionarlo", Math.Max(left, width - 190), height - 22, Color.FromRgb(101, 121, 145), 9);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_points.Count == 0) return;
        var point = e.GetPosition(this);
        var hit = _points.Select(item => (item.Row, Horizontal: Math.Abs(item.Point.X - point.X), Distance: (item.Point - point).Length))
            .OrderBy(item => item.Distance).First();
        // On the rug, use the exact dot; inside the plot, select the nearest score horizontally.
        if (hit.Distance <= 18 || (_plotBounds.Contains(point) && hit.Horizontal <= 12))
            FrameSelected?.Invoke(this, hit.Row);
    }

    private static void DrawMarker(DrawingContext dc, double position, Color color, string label, double left, double top, double width, double height)
    {
        var x = left + width * position;
        dc.DrawLine(new Pen(new SolidColorBrush(color), 1.5) { DashStyle = DashStyles.Dash }, new Point(x, top), new Point(x, top + height));
        Label(dc, label, Math.Min(x + 5, left + width - 74), top + 4, color, 9);
    }

    private static SolidColorBrush Brush(string value) => new((Color)ColorConverter.ConvertFromString(value));

    private static void Label(DrawingContext dc, string text, double x, double y, Color color, double size)
    {
        var dpi = Application.Current?.MainWindow is Visual visual ? VisualTreeHelper.GetDpi(visual).PixelsPerDip : 1;
        dc.DrawText(new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, new SolidColorBrush(color), dpi), new Point(x, y));
    }
}
