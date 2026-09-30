using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AstroForge.CrossPlatform.Controls;

public enum StepState { Pending, Now, Done }

public sealed record ShellStep(string Title, string Detail, StepState State);

/// <summary>
/// The four steps of a project drawn as a constellation: each finished step is a lit star and the line
/// between stars is drawn in as the work moves on. The current step breathes. Click or Enter opens a step.
/// </summary>
public sealed class StepConstellation : AmbientControl
{
    public static readonly StyledProperty<IReadOnlyList<ShellStep>?> StepsProperty =
        AvaloniaProperty.Register<StepConstellation, IReadOnlyList<ShellStep>?>(nameof(Steps));

    // Star positions from the mockup, in a 210 × 330 box.
    private static readonly Point[] Stars = [new(17, 22), new(40, 110), new(22, 205), new(48, 300)];
    private const double DesignHeight = 330;

    private static readonly Typeface Body = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface Small = new(FontFamily.Default);
    private static readonly IBrush Fg = new ImmutableSolidColorBrush(Color.Parse("#EEF1FF"));
    private static readonly IBrush Muted = new ImmutableSolidColorBrush(Color.Parse("#8E97BD"));
    private static readonly IBrush Dim = new ImmutableSolidColorBrush(Color.Parse("#5B6490"));
    private static readonly IBrush StarLit = new ImmutableSolidColorBrush(Color.Parse("#DFE6FF"));
    private static readonly IBrush StarNow = new ImmutableSolidColorBrush(Color.Parse("#9DB8FF"));
    private static readonly IBrush Hover = new ImmutableSolidColorBrush(Color.Parse("#0F9DB8FF"));
    private static readonly IBrush FocusRing = new ImmutableSolidColorBrush(Color.Parse("#9DB8FF"));
    private static readonly IPen Ghost = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#2196AAFF")), 1, new ImmutableDashStyle([2, 5], 0));
    private static readonly IPen Lit = new ImmutablePen(new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#DFE6FF"), 0), new GradientStop(Color.Parse("#9DB8FF"), 1) }
    }.ToImmutable(), 1.4, lineCap: PenLineCap.Round);

    private double _lit;          // segments drawn so far, animated toward the number of finished steps
    private int _hover = -1;
    private int _focus;

    static StepConstellation() => AffectsRender<StepConstellation>(StepsProperty);

    public StepConstellation()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public event EventHandler<int>? StepInvoked;

    public IReadOnlyList<ShellStep>? Steps { get => GetValue(StepsProperty); set => SetValue(StepsProperty, value); }

    protected override bool WantsAmbient => Steps?.Any(step => step.State == StepState.Now) == true;

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 210 : availableSize.Width, DesignHeight);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != StepsProperty) return;
        var target = LitTarget();
        var from = _lit;
        if (Math.Abs(target - from) < 0.001) return;
        _ = Motion.Tween(TopLevel.GetTopLevel(this), TimeSpan.FromMilliseconds(1200), Motion.EaseOutExpo, t =>
        {
            _lit = from + (target - from) * t;
            InvalidateVisual();
        }, TimeSpan.FromMilliseconds(250));
    }

    private double LitTarget()
    {
        var steps = Steps ?? [];
        var done = 0;
        while (done < steps.Count && steps[done].State == StepState.Done) done++;
        return Math.Min(done, Stars.Length - 1);
    }

    private double Scale => Bounds.Height > 0 ? Math.Min(1, Bounds.Height / DesignHeight) : 1;

    private Rect RowOf(int index)
    {
        var star = Stars[index] * Scale;
        return new Rect(0, star.Y - 23, Bounds.Width, 46);
    }

    private int HitTest(Point point)
    {
        var steps = Steps ?? [];
        for (var index = 0; index < Math.Min(steps.Count, Stars.Length); index++)
            if (RowOf(index).Contains(point)) return index;
        return -1;
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
        _hover = -1;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var hit = HitTest(e.GetPosition(this));
        if (hit < 0) return;
        _focus = hit;
        StepInvoked?.Invoke(this, hit);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var count = Math.Min(Steps?.Count ?? 0, Stars.Length);
        if (count == 0) return;
        if (e.Key is Key.Down or Key.Right) { _focus = (_focus + 1) % count; InvalidateVisual(); e.Handled = true; }
        else if (e.Key is Key.Up or Key.Left) { _focus = (_focus + count - 1) % count; InvalidateVisual(); e.Handled = true; }
        else if (e.Key is Key.Enter or Key.Space) { StepInvoked?.Invoke(this, _focus); e.Handled = true; }
    }

    protected override void OnGotFocus(FocusChangedEventArgs e) { base.OnGotFocus(e); InvalidateVisual(); }
    protected override void OnLostFocus(FocusChangedEventArgs e) { base.OnLostFocus(e); InvalidateVisual(); }

    public override void Render(DrawingContext context)
    {
        var steps = Steps ?? [];
        if (steps.Count == 0) return;
        var scale = Scale;
        var points = Stars.Select(point => point * scale).ToArray();

        for (var index = 0; index < points.Length - 1; index++)
            context.DrawLine(Ghost, points[index], points[index + 1]);
        for (var index = 0; index < points.Length - 1; index++)
        {
            var amount = Math.Clamp(_lit - index, 0, 1);
            if (amount <= 0) break;
            var end = points[index] + (points[index + 1] - points[index]) * amount;
            context.DrawLine(Lit, points[index], end);
        }

        for (var index = 0; index < Math.Min(steps.Count, points.Length); index++)
        {
            var step = steps[index];
            var row = RowOf(index);
            if (index == _hover) context.DrawRectangle(Hover, null, row.Deflate(new Thickness(-6, 0, 0, 0)), 12, 12);
            if (IsFocused && index == _focus && IsKeyboardFocusWithin)
                context.DrawRectangle(null, new Pen(FocusRing, 1.5), row, 12, 12);

            var centre = points[index];
            var (brush, size) = step.State switch
            {
                StepState.Done => (StarLit, 1.0),
                StepState.Now => (StarNow, 0.85),
                _ => (Dim, 0.55)
            };
            if (step.State == StepState.Now)
            {
                // Halo ring expands and fades like a star twinkling in poor seeing.
                var phase = Clock * 0.55 % 1;
                var halo = new Pen(new SolidColorBrush(Color.FromArgb((byte)(150 * (1 - phase)), 157, 184, 255)), 1);
                var radius = 8 + 12 * phase;
                context.DrawEllipse(null, halo, centre, radius, radius);
            }
            if (step.State == StepState.Done)
            {
                var glow = new RadialGradientBrush
                {
                    GradientStops = { new GradientStop(Color.FromArgb(90, 157, 184, 255), 0), new GradientStop(Color.FromArgb(0, 157, 184, 255), 1) }
                };
                context.DrawEllipse(glow, null, centre, 16, 16);
            }
            context.DrawGeometry(brush, null, StarGeometry(centre, 14 * size));

            var textBrush = step.State == StepState.Pending ? Dim : Fg;
            var detailBrush = step.State == StepState.Pending ? Dim : Muted;
            var left = Math.Max(centre.X + 25, 58);
            var width = Math.Max(40, Bounds.Width - left - 4);
            var title = new FormattedText(step.Title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Body, 14, textBrush) { MaxTextWidth = width, Trimming = TextTrimming.CharacterEllipsis, MaxLineCount = 1 };
            var detail = new FormattedText(step.Detail, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Small, 11.5, detailBrush) { MaxTextWidth = width, Trimming = TextTrimming.CharacterEllipsis, MaxLineCount = 1 };
            var top = centre.Y - (title.Height + detail.Height + 1) / 2;
            context.DrawText(title, new Point(left, top));
            context.DrawText(detail, new Point(left, top + title.Height + 1));
        }
    }

    /// <summary>A four-pointed diffraction star, the same glyph as the app icon.</summary>
    private static Geometry StarGeometry(Point centre, double radius)
    {
        var waist = radius * 0.16;
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        g.BeginFigure(new Point(centre.X, centre.Y - radius), true);
        g.LineTo(new Point(centre.X + waist, centre.Y - waist));
        g.LineTo(new Point(centre.X + radius, centre.Y));
        g.LineTo(new Point(centre.X + waist, centre.Y + waist));
        g.LineTo(new Point(centre.X, centre.Y + radius));
        g.LineTo(new Point(centre.X - waist, centre.Y + waist));
        g.LineTo(new Point(centre.X - radius, centre.Y));
        g.LineTo(new Point(centre.X - waist, centre.Y - waist));
        g.EndFigure(true);
        return geometry;
    }
}
