using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>
/// Share of Light frames with an accepted Flat, Dark and Bias, as three arcs of one ring; the gap left in each
/// arc is the part still to assign. The arcs sweep in when the analysis changes.
/// </summary>
public sealed class CalibrationRing : Control
{
    public static readonly StyledProperty<double> FlatProperty = AvaloniaProperty.Register<CalibrationRing, double>(nameof(Flat));
    public static readonly StyledProperty<double> DarkProperty = AvaloniaProperty.Register<CalibrationRing, double>(nameof(Dark));
    public static readonly StyledProperty<double> BiasProperty = AvaloniaProperty.Register<CalibrationRing, double>(nameof(Bias));
    public static readonly StyledProperty<string?> CentreProperty = AvaloniaProperty.Register<CalibrationRing, string?>(nameof(Centre));

    private static readonly Typeface Display = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#Unbounded"), FontStyle.Normal, FontWeight.Light);
    private static readonly Typeface Body = new(FontFamily.Default);
    private static readonly IBrush Fg = new ImmutableSolidColorBrush(Color.Parse("#EEF1FF"));
    private static readonly IBrush Muted = new ImmutableSolidColorBrush(Color.Parse("#8E97BD"));
    private static readonly IPen Track = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#1A9DB8FF")), 9);
    private static readonly Color[] Colours = [Color.Parse("#3FE0D0"), Color.Parse("#9DB8FF"), Color.Parse("#C59BFF")];
    private double _sweep = 1;

    static CalibrationRing() => AffectsRender<CalibrationRing>(FlatProperty, DarkProperty, BiasProperty, CentreProperty);

    public double Flat { get => GetValue(FlatProperty); set => SetValue(FlatProperty, value); }
    public double Dark { get => GetValue(DarkProperty); set => SetValue(DarkProperty, value); }
    public double Bias { get => GetValue(BiasProperty); set => SetValue(BiasProperty, value); }
    public string? Centre { get => GetValue(CentreProperty); set => SetValue(CentreProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != FlatProperty && change.Property != DarkProperty && change.Property != BiasProperty) return;
        _sweep = 0;
        _ = Motion.Tween(TopLevel.GetTopLevel(this), TimeSpan.FromMilliseconds(1300), Motion.EaseOutExpo, t => { _sweep = t; InvalidateVisual(); }, TimeSpan.FromMilliseconds(150));
    }

    protected override Size MeasureOverride(Size availableSize) => new(118, 118);

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = size / 2 - 9;
        context.DrawEllipse(null, Track, centre, radius, radius);

        // Each part owns a third of the ring; its lit length is the accepted share, less a small gap.
        var values = new[] { Flat, Dark, Bias };
        const double gap = 5;
        for (var index = 0; index < 3; index++)
        {
            var start = -90 + index * 120 + gap / 2;
            var span = Math.Max(0, (120 - gap) * Math.Clamp(values[index], 0, 1) * _sweep);
            if (span < 0.5) continue;
            var pen = new Pen(new SolidColorBrush(Colours[index]), 9, lineCap: PenLineCap.Round);
            context.DrawGeometry(null, pen, Arc(centre, radius, start, span));
        }

        var text = Centre ?? "";
        var big = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Display, size * 0.2, Fg);
        var small = new FormattedText(CanvasText.T("calibrato"), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Body, 11, Muted);
        var top = centre.Y - (big.Height + small.Height) / 2;
        context.DrawText(big, new Point(centre.X - big.Width / 2, top));
        context.DrawText(small, new Point(centre.X - small.Width / 2, top + big.Height));
    }

    private static Geometry Arc(Point centre, double radius, double startDegrees, double sweepDegrees)
    {
        static Point On(Point c, double r, double degrees) => new(c.X + r * Math.Cos(degrees * Math.PI / 180), c.Y + r * Math.Sin(degrees * Math.PI / 180));
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        g.BeginFigure(On(centre, radius, startDegrees), false);
        g.ArcTo(On(centre, radius, startDegrees + sweepDegrees), new Size(radius, radius), 0, sweepDegrees > 180, SweepDirection.Clockwise);
        g.EndFigure(false);
        return geometry;
    }
}
