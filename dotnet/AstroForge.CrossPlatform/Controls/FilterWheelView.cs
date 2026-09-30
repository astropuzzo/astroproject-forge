using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AstroForge.CrossPlatform.Controls;

public sealed record WheelSlot(string Label, IBrush Glass, bool Unknown);

/// <summary>
/// The filter wheel seen from the camera side. The selected filter turns into the light path at the
/// top, marked by the beam window; slots nobody has identified yet glow amber with a question mark.
/// </summary>
public sealed class FilterWheelView : Control
{
    public static readonly StyledProperty<IReadOnlyList<WheelSlot>?> SlotsProperty =
        AvaloniaProperty.Register<FilterWheelView, IReadOnlyList<WheelSlot>?>(nameof(Slots));
    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<FilterWheelView, int>(nameof(SelectedIndex), defaultBindingMode: BindingMode.TwoWay);

    private static readonly Typeface Mono = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#JetBrains Mono"));
    private static readonly IBrush LabelBrush = new ImmutableSolidColorBrush(Color.Parse("#C9D3F5"));
    private static readonly IBrush QuestionInk = new ImmutableSolidColorBrush(Color.Parse("#3A2408"));
    private static readonly IPen Rim = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#52AABEFF")), 1);
    private static readonly IPen SlotRim = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#3396AAFF")), 1);
    private static readonly IPen WarnRim = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#FFC27A")), 1.4, new ImmutableDashStyle([3, 2], 0));
    private static readonly IPen SelectedRim = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#EEF1FF")), 1.6);

    private double _angle, _velocity;
    private bool _animating;
    private TimeSpan _last;

    static FilterWheelView() => AffectsRender<FilterWheelView>(SlotsProperty);

    public FilterWheelView() => Cursor = new Cursor(StandardCursorType.Hand);

    public IReadOnlyList<WheelSlot>? Slots { get => GetValue(SlotsProperty); set => SetValue(SlotsProperty, value); }
    public int SelectedIndex { get => GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }

    private int Count => Math.Max(5, Slots?.Count ?? 0);
    private double TargetAngle => -SelectedIndex * Math.Tau / Count;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedIndexProperty || change.Property == SlotsProperty) Turn();
    }

    private void Turn()
    {
        var target = TargetAngle;
        // Always take the short way round.
        while (target - _angle > Math.PI) target -= Math.Tau;
        while (target - _angle < -Math.PI) target += Math.Tau;
        if (Motion.Reduced || TopLevel.GetTopLevel(this) is not { } host)
        {
            _angle = target;
            InvalidateVisual();
            return;
        }
        if (_animating) return;
        _animating = true;
        _last = TimeSpan.Zero;
        host.RequestAnimationFrame(Step);
    }

    private void Step(TimeSpan now)
    {
        var dt = _last == TimeSpan.Zero ? 1 / 60d : Math.Min(1 / 30d, (now - _last).TotalSeconds);
        _last = now;
        var target = TargetAngle;
        while (target - _angle > Math.PI) target -= Math.Tau;
        while (target - _angle < -Math.PI) target += Math.Tau;
        _angle = Motion.Spring(_angle, ref _velocity, target, dt, 120, 14);
        InvalidateVisual();
        if (Math.Abs(target - _angle) < 0.002 && Math.Abs(_velocity) < 0.01 || Motion.Reduced)
        {
            _angle = target;
            _velocity = 0;
            _animating = false;
            InvalidateVisual();
            return;
        }
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(Step);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Slots is not { Count: > 0 } slots) return;
        var geometry = Layout();
        var point = e.GetPosition(this);
        for (var index = 0; index < slots.Count; index++)
            if (((Vector)(point - SlotCenter(geometry, index))).Length <= geometry.SlotRadius + 4)
            {
                SelectedIndex = index;
                e.Handled = true;
                return;
            }
    }

    private (Point Center, double Radius, double SlotRadius, double Orbit) Layout()
    {
        var radius = Math.Max(20, Math.Min(Bounds.Width, Bounds.Height - 30) / 2 - 4);
        var center = new Point(Bounds.Width / 2, radius + 16);
        return (center, radius, radius * 0.23, radius * 0.66);
    }

    private Point SlotCenter((Point Center, double Radius, double SlotRadius, double Orbit) geometry, int index)
    {
        var angle = -Math.PI / 2 + index * Math.Tau / Count + _angle;
        return geometry.Center + new Vector(Math.Cos(angle), Math.Sin(angle)) * geometry.Orbit;
    }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 260 : availableSize.Width, 290);

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0) return;
        var geometry = Layout();
        var (center, radius, slotRadius, _) = geometry;

        // The light path: a beam window at the top of the wheel.
        var beam = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#009DB8FF"), 0), new GradientStop(Color.Parse("#559DB8FF"), 1) }
        };
        context.DrawRectangle(beam, null, new Rect(center.X - slotRadius * 0.55, 0, slotRadius * 1.1, center.Y - geometry.Orbit));

        var body = new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Color.Parse("#1A2240"), 0), new GradientStop(Color.Parse("#0C1126"), 0.8), new GradientStop(Color.Parse("#070B18"), 1) }
        };
        context.DrawEllipse(body, Rim, center, radius, radius);
        context.DrawEllipse(null, SlotRim, center, radius * 0.28, radius * 0.28);
        context.DrawEllipse(new ImmutableSolidColorBrush(Color.Parse("#0A0F22")), SlotRim, center, radius * 0.12, radius * 0.12);

        var slots = Slots ?? [];
        for (var index = 0; index < Count; index++)
        {
            var point = SlotCenter(geometry, index);
            if (index >= slots.Count)
            {
                context.DrawEllipse(new ImmutableSolidColorBrush(Color.Parse("#05070F")), SlotRim, point, slotRadius, slotRadius);
                continue;
            }
            var slot = slots[index];
            var selected = index == SelectedIndex;
            using (context.PushOpacity(selected ? 1 : 0.72))
                context.DrawEllipse(slot.Glass, slot.Unknown ? WarnRim : selected ? SelectedRim : SlotRim, point, slotRadius, slotRadius);
            // A glassy highlight so the disc reads as a filter, not a dot.
            var shine = new RadialGradientBrush
            {
                Center = new RelativePoint(0.35, 0.3, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.35, 0.3, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#66FFFFFF"), 0), new GradientStop(Color.Parse("#00FFFFFF"), 0.6) }
            };
            context.DrawEllipse(shine, null, point, slotRadius, slotRadius);
            if (slot.Unknown)
            {
                var mark = new FormattedText("?", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, slotRadius * 0.9, QuestionInk);
                context.DrawText(mark, point - new Vector(mark.Width / 2, mark.Height / 2));
            }
        }

        if (SelectedIndex >= 0 && SelectedIndex < slots.Count)
        {
            var label = new FormattedText(slots[SelectedIndex].Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 11, LabelBrush) { MaxTextWidth = Bounds.Width };
            context.DrawText(label, new Point(center.X - label.Width / 2, center.Y + radius + 6));
        }
    }
}
