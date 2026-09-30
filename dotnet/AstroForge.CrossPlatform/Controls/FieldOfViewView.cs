using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>
/// The sensor's field of view drawn to scale on the sky, next to the full Moon (0.52°) so the size
/// means something at a glance. The frame eases to a new size when the optics change.
/// </summary>
public sealed class FieldOfViewView : Control
{
    public static readonly StyledProperty<double> WidthDegProperty = AvaloniaProperty.Register<FieldOfViewView, double>(nameof(WidthDeg));
    public static readonly StyledProperty<double> HeightDegProperty = AvaloniaProperty.Register<FieldOfViewView, double>(nameof(HeightDeg));

    private static readonly Typeface Mono = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#JetBrains Mono"));
    private static readonly IBrush Muted = new ImmutableSolidColorBrush(Color.Parse("#8E97BD"));
    private static readonly IBrush Fg = new ImmutableSolidColorBrush(Color.Parse("#EEF1FF"));
    private static readonly IPen Frame = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#9DB8FF")), 1.4);
    private static readonly IPen Grid = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#1496AAFF")), 1);
    private readonly (double X, double Y, double R, byte A)[] _stars;
    private double _shownWidth, _shownHeight;

    static FieldOfViewView() => AffectsRender<FieldOfViewView>(WidthDegProperty, HeightDegProperty);

    public FieldOfViewView()
    {
        var random = new Random(6992);
        _stars = Enumerable.Range(0, 220).Select(_ => (random.NextDouble(), random.NextDouble(), 0.3 + Math.Pow(random.NextDouble(), 3) * 1.6, (byte)(60 + random.Next(170)))).ToArray();
    }

    public double WidthDeg { get => GetValue(WidthDegProperty); set => SetValue(WidthDegProperty, value); }
    public double HeightDeg { get => GetValue(HeightDegProperty); set => SetValue(HeightDegProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != WidthDegProperty && change.Property != HeightDegProperty) return;
        double fromW = _shownWidth, fromH = _shownHeight;
        if (fromW <= 0) { _shownWidth = WidthDeg; _shownHeight = HeightDeg; InvalidateVisual(); return; }
        _ = Motion.Tween(TopLevel.GetTopLevel(this), TimeSpan.FromMilliseconds(700), Motion.EaseOutBack, t =>
        {
            _shownWidth = fromW + (WidthDeg - fromW) * t;
            _shownHeight = fromH + (HeightDeg - fromH) * t;
            InvalidateVisual();
        });
    }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 320 : availableSize.Width, 220);

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0) return;
        context.DrawRectangle(new ImmutableSolidColorBrush(Color.Parse("#CC04060D")), null, bounds, 14, 14);
        using var clip = context.PushClip(new RoundedRect(bounds, 14));

        foreach (var (x, y, r, a) in _stars)
            context.DrawEllipse(new ImmutableSolidColorBrush(Color.FromArgb(a, 230, 236, 255)), null, new Point(x * bounds.Width, y * bounds.Height), r, r);

        if (_shownWidth <= 0 || _shownHeight <= 0)
        {
            var empty = new FormattedText(CanvasText.T("Servono FOCALLEN e dimensione pixel"), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 11, Muted);
            context.DrawText(empty, new Point((bounds.Width - empty.Width) / 2, (bounds.Height - empty.Height) / 2));
            return;
        }

        // Span the view so the frame fills about 60% of the width, never less than two Moons.
        var span = Math.Max(Math.Max(_shownWidth / 0.6, _shownHeight / 0.6 * bounds.Width / bounds.Height), 1.2);
        var pxPerDeg = bounds.Width / span;
        for (var degree = 1; degree < span; degree++)
            context.DrawLine(Grid, new Point(degree * pxPerDeg, 0), new Point(degree * pxPerDeg, bounds.Height));

        var frame = new Rect(bounds.Center.X - _shownWidth * pxPerDeg / 2 + 30, bounds.Center.Y - _shownHeight * pxPerDeg / 2, _shownWidth * pxPerDeg, _shownHeight * pxPerDeg);
        context.DrawRectangle(new ImmutableSolidColorBrush(Color.Parse("#189DB8FF")), Frame, frame, 3, 3);
        var size = new FormattedText($"{_shownWidth:0.00}° × {_shownHeight:0.00}°", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 11, Fg);
        context.DrawText(size, new Point(frame.X + 8, frame.Y + 6));

        // The full Moon, 0.52°, as a ruler everybody knows.
        var moonRadius = 0.26 * pxPerDeg;
        var moonCenter = new Point(Math.Max(moonRadius + 14, frame.X - moonRadius - 22), bounds.Bottom - moonRadius - 26);
        var moon = new RadialGradientBrush
        {
            Center = new RelativePoint(0.4, 0.4, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.4, 0.4, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#F2F0E6"), 0), new GradientStop(Color.Parse("#B9B6A8"), 0.85), new GradientStop(Color.Parse("#8C897C"), 1) }
        };
        context.DrawEllipse(moon, null, moonCenter, moonRadius, moonRadius);
        var moonLabel = new FormattedText($"{CanvasText.T("Luna")} {0.52:0.00}°", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 9.5, Muted);
        context.DrawText(moonLabel, new Point(moonCenter.X - moonLabel.Width / 2, moonCenter.Y + moonRadius + 4));
    }
}
