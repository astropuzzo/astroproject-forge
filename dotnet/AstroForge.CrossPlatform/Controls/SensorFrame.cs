using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>
/// The sensor's footprint on the sky, to scale, laid over the preview: the sky outside the frame is dimmed,
/// the other optical configuration is the dashed frame, and the frame eases to its new size when the optics change.
/// </summary>
public sealed class SensorFrame : Control
{
    public static readonly StyledProperty<double> WidthDegProperty = AvaloniaProperty.Register<SensorFrame, double>(nameof(WidthDeg));
    public static readonly StyledProperty<double> HeightDegProperty = AvaloniaProperty.Register<SensorFrame, double>(nameof(HeightDeg));
    public static readonly StyledProperty<double> AltWidthDegProperty = AvaloniaProperty.Register<SensorFrame, double>(nameof(AltWidthDeg));
    public static readonly StyledProperty<double> AltHeightDegProperty = AvaloniaProperty.Register<SensorFrame, double>(nameof(AltHeightDeg));
    public static readonly StyledProperty<string?> AltLabelProperty = AvaloniaProperty.Register<SensorFrame, string?>(nameof(AltLabel));
    public static readonly StyledProperty<bool> WarnProperty = AvaloniaProperty.Register<SensorFrame, bool>(nameof(Warn));

    private static readonly Typeface Mono = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#JetBrains Mono"));
    private static readonly IBrush Shade = new ImmutableSolidColorBrush(Color.Parse("#6B04060D"));
    private static readonly IBrush Fg = new ImmutableSolidColorBrush(Color.Parse("#EEF2FF"));
    private static readonly IBrush Soft = new ImmutableSolidColorBrush(Color.Parse("#B3BECDFF"));
    private static readonly IPen Outline = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#EEF2FF")), 1.5);
    private static readonly IPen Other = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#73BECDFF")), 1, new ImmutableDashStyle([4, 5], 0));
    private double _w, _h, _aw, _ah;

    static SensorFrame() => AffectsRender<SensorFrame>(AltLabelProperty, WarnProperty);

    public SensorFrame() => IsHitTestVisible = false;

    public double WidthDeg { get => GetValue(WidthDegProperty); set => SetValue(WidthDegProperty, value); }
    public double HeightDeg { get => GetValue(HeightDegProperty); set => SetValue(HeightDegProperty, value); }
    public double AltWidthDeg { get => GetValue(AltWidthDegProperty); set => SetValue(AltWidthDegProperty, value); }
    public double AltHeightDeg { get => GetValue(AltHeightDegProperty); set => SetValue(AltHeightDegProperty, value); }
    public string? AltLabel { get => GetValue(AltLabelProperty); set => SetValue(AltLabelProperty, value); }
    public bool Warn { get => GetValue(WarnProperty); set => SetValue(WarnProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != WidthDegProperty && change.Property != HeightDegProperty && change.Property != AltWidthDegProperty && change.Property != AltHeightDegProperty) return;
        double w = _w, h = _h, aw = _aw, ah = _ah;
        if (w <= 0) { _w = WidthDeg; _h = HeightDeg; _aw = AltWidthDeg; _ah = AltHeightDeg; InvalidateVisual(); return; }
        _ = Motion.Tween(TopLevel.GetTopLevel(this), TimeSpan.FromMilliseconds(900), t => 1 - Math.Pow(1 - t, 3), t =>
        {
            _w = w + (WidthDeg - w) * t; _h = h + (HeightDeg - h) * t;
            _aw = aw + (AltWidthDeg - aw) * t; _ah = ah + (AltHeightDeg - ah) * t;
            InvalidateVisual();
        });
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || _w <= 0 || _h <= 0) return;
        // The larger of the two frames fills the free area right of the readout and above the dock.
        var box = new Rect(bounds.Width * 0.5, 30, Math.Max(40, bounds.Width * 0.5 - 30), Math.Max(40, bounds.Height - 30 - 118));
        var pxPerDeg = Math.Min(box.Width / Math.Max(0.05, Math.Max(_w, _aw)), box.Height / Math.Max(0.05, Math.Max(_h, _ah)));
        var centre = box.Center;
        var frame = new Rect(centre.X - _w * pxPerDeg / 2, centre.Y - _h * pxPerDeg / 2, _w * pxPerDeg, _h * pxPerDeg);

        var geometry = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(bounds), new RectangleGeometry(frame));
        context.DrawGeometry(Shade, null, geometry);

        if (_aw > 0 && _ah > 0 && Math.Abs(_aw - _w) > 0.001)
        {
            var other = new Rect(centre.X - _aw * pxPerDeg / 2, centre.Y - _ah * pxPerDeg / 2, _aw * pxPerDeg, _ah * pxPerDeg);
            context.DrawRectangle(null, Other, other);
            if (!string.IsNullOrEmpty(AltLabel))
                context.DrawText(new FormattedText(AltLabel, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 11, Soft), new Point(other.X + 8, other.Y + 6));
        }

        context.DrawRectangle(null, Outline, frame);
        var corner = new Pen(new SolidColorBrush(Color.Parse(Warn ? "#FFC27A" : "#9DB8FF")), 3);
        const double arm = 14;
        foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
        {
            var x = centre.X + sx * frame.Width / 2;
            var y = centre.Y + sy * frame.Height / 2;
            context.DrawLine(corner, new Point(x - sx * arm, y), new Point(x, y));
            context.DrawLine(corner, new Point(x, y), new Point(x, y - sy * arm));
        }
        var size = new FormattedText($"{_w.ToString("0.00", CultureInfo.CurrentCulture)}° × {_h.ToString("0.00", CultureInfo.CurrentCulture)}°", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 11, Fg);
        context.DrawText(size, new Point(frame.X + 8, frame.Bottom - size.Height - 8));
    }
}
