using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>
/// "Prima luce": a nine-blade iris diaphragm that opens on the sky when the app starts.
/// A single guide star blinks in the closed aperture, then the blades twist away.
/// </summary>
public sealed class FirstLightOverlay : Control
{
    private const int Blades = 9;
    private static readonly IPen BladeEdge = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#48AABEFF")), 1);
    private double _open;
    private double _star = 1;

    public FirstLightOverlay()
    {
        IsHitTestVisible = false;
    }

    /// <summary>Opens the iris. <paramref name="revealed"/> fires once the aperture is wide enough to see through.</summary>
    public async Task PlayAsync(Action revealed)
    {
        var host = TopLevel.GetTopLevel(this);
        if (Motion.Reduced || host is null)
        {
            IsVisible = false;
            revealed();
            return;
        }

        IsVisible = true;
        await Motion.Tween(host, TimeSpan.FromMilliseconds(520), t => t, t =>
        {
            _star = 0.6 + 0.4 * Math.Sin(t * Math.PI * 5);
            InvalidateVisual();
        });

        var fired = false;
        await Motion.Tween(host, TimeSpan.FromMilliseconds(1350), Motion.EaseInOutCubic, t =>
        {
            _open = t;
            Opacity = t < 0.75 ? 1 : 1 - (t - 0.75) / 0.25;
            InvalidateVisual();
            if (!fired && t > 0.3)
            {
                fired = true;
                revealed();
            }
        });
        if (!fired) revealed();
        IsVisible = false;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || !IsVisible) return;
        var center = bounds.Center;
        var reach = Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height);
        var radius = 2 + _open * reach * 0.62;
        var twist = _open * 1.1;

        for (var index = 0; index < Blades; index++)
        {
            var angle = index * Math.Tau / Blades + twist;
            var normal = new Vector(Math.Cos(angle), Math.Sin(angle));
            var tangent = new Vector(-normal.Y, normal.X);
            var edge = center + normal * radius;
            var far = reach * 1.2;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(edge - tangent * far, true);
                g.LineTo(edge + tangent * (far * 0.35));
                g.LineTo(edge + tangent * (far * 0.35) + normal * far);
                g.LineTo(edge - tangent * far + normal * far);
                g.EndFigure(true);
            }
            var fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(edge, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(edge + normal * 260, RelativeUnit.Absolute),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#1B2350"), 0),
                    new GradientStop(Color.Parse("#0B0F24"), 0.08),
                    new GradientStop(Color.Parse("#04060D"), 1)
                }
            };
            context.DrawGeometry(fill, BladeEdge, geometry);
        }

        if (_open <= 0)
        {
            var star = new ImmutableSolidColorBrush(Color.FromArgb((byte)(255 * _star), 255, 255, 255));
            context.DrawEllipse(star, null, center, 1.8, 1.8);
            context.FillRectangle(star, new Rect(center.X - 10, center.Y - 0.4, 20, 0.8));
            context.FillRectangle(star, new Rect(center.X - 0.4, center.Y - 10, 0.8, 20));
        }
    }
}
