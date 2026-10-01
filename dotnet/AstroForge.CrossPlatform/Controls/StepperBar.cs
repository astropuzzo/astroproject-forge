using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AstroForge.CrossPlatform.Controls;

public enum StepState { Pending, Now, Done, Attention }

/// <param name="Complete">Nothing left to do on this step (a check is drawn on its node, even when it is the one on screen).</param>
/// <param name="Attention">Something is open on this step (amber detail line).</param>
/// <param name="Openable">The step has something to show, so a click opens it.</param>
public sealed record ShellStep(string Title, string Detail, StepState State, bool Complete = false, bool Attention = false, bool Openable = true);

/// <summary>
/// The four steps of a project in one row: a numbered node per step, a line between them that is drawn in as the work moves on,
/// a check on every finished step and a breathing ring on the one on screen. Click or Enter opens a step.
/// </summary>
public sealed class StepperBar : AmbientControl
{
    public static readonly StyledProperty<IReadOnlyList<ShellStep>?> StepsProperty =
        AvaloniaProperty.Register<StepperBar, IReadOnlyList<ShellStep>?>(nameof(Steps));

    private const double NodeRadius = 15;
    private const double DesignHeight = 66;

    private static readonly Typeface Body = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface Small = new(FontFamily.Default);
    private static readonly Typeface Mono = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#JetBrains Mono"), FontStyle.Normal, FontWeight.SemiBold);
    private static readonly IBrush Fg = new ImmutableSolidColorBrush(Color.Parse("#EEF1FF"));
    private static readonly IBrush Muted = new ImmutableSolidColorBrush(Color.Parse("#8E97BD"));
    private static readonly IBrush Dim = new ImmutableSolidColorBrush(Color.Parse("#5B6490"));
    private static readonly IBrush Warn = new ImmutableSolidColorBrush(Color.Parse("#FFC27A"));
    private static readonly IBrush Ink = new ImmutableSolidColorBrush(Color.Parse("#07102A"));
    private static readonly IBrush Hover = new ImmutableSolidColorBrush(Color.Parse("#0F9DB8FF"));
    private static readonly IPen FocusRing = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#9DB8FF")), 1.5);
    private static readonly IPen Ghost = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#2E96AAFF")), 1.2, new ImmutableDashStyle([2, 4], 0));
    private static readonly IPen DimRing = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#4D96AAFF")), 1.4);
    private static readonly IPen WarnRing = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#FFC27A")), 1.6);

    private double _lit;      // segments drawn so far, animated toward the step on screen
    private double _arrive = 1; // 0 → 1 each time another step comes on screen
    private int _current = -1;
    private int _hover = -1;
    private int _focus;

    static StepperBar() => AffectsRender<StepperBar>(StepsProperty);

    public StepperBar()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public event EventHandler<int>? StepInvoked;

    public IReadOnlyList<ShellStep>? Steps { get => GetValue(StepsProperty); set => SetValue(StepsProperty, value); }

    protected override bool WantsAmbient => Steps?.Any(step => step.State == StepState.Now) == true;

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 640 : availableSize.Width, DesignHeight);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != StepsProperty) return;
        var now = Steps?.ToList().FindIndex(step => step.State == StepState.Now) ?? -1;
        var host = TopLevel.GetTopLevel(this);
        if (now != _current)
        {
            _current = now;
            _focus = Math.Max(0, now);
            _arrive = 0;
            _ = Motion.Tween(host, TimeSpan.FromMilliseconds(700), Motion.EaseOutBack, t => { _arrive = t; InvalidateVisual(); });
        }
        var target = Math.Max(0, now);
        var from = _lit;
        if (Math.Abs(target - from) < 0.001) return;
        _ = Motion.Tween(host, TimeSpan.FromMilliseconds(900), Motion.EaseOutExpo, t => { _lit = from + (target - from) * t; InvalidateVisual(); });
    }

    private double CellWidth => Steps is { Count: > 0 } steps ? Bounds.Width / steps.Count : Bounds.Width;
    private double CentreX(int index) => CellWidth * (index + 0.5);
    private const double CentreY = NodeRadius + 4;

    private Rect CellOf(int index) => new(CellWidth * index, 0, CellWidth, Bounds.Height);

    private int HitTest(Point point)
    {
        var count = Steps?.Count ?? 0;
        for (var index = 0; index < count; index++)
            if (CellOf(index).Contains(point)) return index;
        return -1;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = HitTest(e.GetPosition(this));
        if (hit >= 0 && Steps is { } steps && !steps[hit].Openable) hit = -1;
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
        if (hit < 0 || Steps is not { } steps || !steps[hit].Openable) return;
        _focus = hit;
        StepInvoked?.Invoke(this, hit);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var count = Steps?.Count ?? 0;
        if (count == 0) return;
        if (e.Key is Key.Right or Key.Down) { _focus = (_focus + 1) % count; InvalidateVisual(); e.Handled = true; }
        else if (e.Key is Key.Left or Key.Up) { _focus = (_focus + count - 1) % count; InvalidateVisual(); e.Handled = true; }
        else if (e.Key is Key.Enter or Key.Space && Steps![_focus].Openable) { StepInvoked?.Invoke(this, _focus); e.Handled = true; }
    }

    protected override void OnGotFocus(FocusChangedEventArgs e) { base.OnGotFocus(e); InvalidateVisual(); }
    protected override void OnLostFocus(FocusChangedEventArgs e) { base.OnLostFocus(e); InvalidateVisual(); }

    public override void Render(DrawingContext context)
    {
        var steps = Steps ?? [];
        if (steps.Count == 0 || Bounds.Width < 40) return;

        // The line between nodes: a dotted ghost, with the finished stretch drawn over it.
        var lit = new ImmutablePen(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#3FE0D0"), 0), new GradientStop(Color.Parse("#9DB8FF"), 1) }
        }.ToImmutable(), 2, lineCap: PenLineCap.Round);
        for (var index = 0; index < steps.Count - 1; index++)
        {
            var from = new Point(CentreX(index) + NodeRadius + 8, CentreY);
            var to = new Point(CentreX(index + 1) - NodeRadius - 8, CentreY);
            context.DrawLine(Ghost, from, to);
            var amount = Math.Clamp(_lit - index, 0, 1);
            if (amount > 0) context.DrawLine(lit, from, new Point(from.X + (to.X - from.X) * amount, CentreY));
        }

        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var centre = new Point(CentreX(index), CentreY);
            var cell = CellOf(index);
            if (index == _hover) context.DrawRectangle(Hover, null, cell.Deflate(new Thickness(6, 0, 6, 2)), 12, 12);
            if (IsFocused && index == _focus && IsKeyboardFocusWithin) context.DrawRectangle(null, FocusRing, cell.Deflate(new Thickness(6, 0, 6, 2)), 12, 12);
            DrawNode(context, step, centre, index);
            DrawLabels(context, step, cell, centre);
        }
    }

    private void DrawNode(DrawingContext context, ShellStep step, Point centre, int index)
    {
        var isNow = step.State == StepState.Now;
        var pop = isNow && index == _current ? 1 + 0.18 * Math.Sin(Math.Clamp(_arrive, 0, 1) * Math.PI) : 1;
        var radius = NodeRadius * pop;
        var number = (index + 1).ToString(CultureInfo.InvariantCulture);

        if (isNow)
        {
            // A soft ring breathes out from the step on screen.
            var phase = Clock * 0.5 % 1;
            context.DrawEllipse(new RadialGradientBrush { GradientStops = { new GradientStop(Color.FromArgb(70, 157, 184, 255), 0), new GradientStop(Color.FromArgb(0, 157, 184, 255), 1) } }, null, centre, radius + 14, radius + 14);
            context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(130 * (1 - phase)), 157, 184, 255)), 1.2), centre, radius + 3 + 9 * phase, radius + 3 + 9 * phase);
        }
        if (step.Complete)
        {
            context.DrawEllipse(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#7CF2C9"), 0), new GradientStop(Color.Parse("#9DB8FF"), 1) }
            }, null, centre, radius, radius);
            DrawCheck(context, centre, radius);
            return;
        }
        if (isNow)
        {
            context.DrawEllipse(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#F4F6FF"), 0), new GradientStop(Color.Parse("#9DB8FF"), 1) }
            }, null, centre, radius, radius);
            DrawGlyph(context, number, centre, Ink);
            return;
        }
        var warn = step.State == StepState.Attention;
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(warn ? (byte)38 : (byte)18, warn ? (byte)255 : (byte)157, warn ? (byte)194 : (byte)184, warn ? (byte)122 : (byte)255)), warn ? WarnRing : DimRing, centre, radius - 1, radius - 1);
        DrawGlyph(context, warn ? "!" : number, centre, warn ? Warn : step.Openable ? Muted : Dim);
    }

    private static void DrawGlyph(DrawingContext context, string text, Point centre, IBrush brush)
    {
        var glyph = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 12.5, brush);
        context.DrawText(glyph, new Point(centre.X - glyph.Width / 2, centre.Y - glyph.Height / 2));
    }

    private static void DrawCheck(DrawingContext context, Point centre, double radius)
    {
        var scale = radius / NodeRadius;
        var pen = new Pen(Ink, 2.2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var path = new StreamGeometry();
        using (var g = path.Open())
        {
            g.BeginFigure(new Point(centre.X - 5 * scale, centre.Y + 0.5 * scale), false);
            g.LineTo(new Point(centre.X - 1.5 * scale, centre.Y + 4 * scale));
            g.LineTo(new Point(centre.X + 5.5 * scale, centre.Y - 4 * scale));
            g.EndFigure(false);
        }
        context.DrawGeometry(null, pen, path);
    }

    private static void DrawLabels(DrawingContext context, ShellStep step, Rect cell, Point centre)
    {
        var pending = step.State == StepState.Pending;
        var titleBrush = pending && !step.Openable ? Dim : Fg;
        var detailBrush = step.Attention ? Warn : pending ? Dim : Muted;
        var width = Math.Max(40, cell.Width - 16);
        var title = new FormattedText(step.Title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Body, 13.5, titleBrush) { MaxTextWidth = width, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center };
        var detail = new FormattedText(step.Detail, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Small, 11, detailBrush) { MaxTextWidth = width, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center };
        var left = cell.X + (cell.Width - width) / 2;
        var top = centre.Y + NodeRadius + 5;
        context.DrawText(title, new Point(left, top));
        context.DrawText(detail, new Point(left, top + title.Height));
    }
}
