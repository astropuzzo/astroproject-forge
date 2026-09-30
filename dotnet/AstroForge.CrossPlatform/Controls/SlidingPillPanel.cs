using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>
/// Tab strip panel that draws a glass pill behind the selected tab and springs it to the next one,
/// with a thin glowing base line: the workspace navigation's selection indicator.
/// </summary>
public sealed class SlidingPillPanel : StackPanel
{
    private static readonly IBrush PillFill = new ImmutableSolidColorBrush(Color.Parse("#249DB8FF"));
    private static readonly IPen PillEdge = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#52AABEFF")), 1);
    private static readonly IBrush Glow = new ImmutableSolidColorBrush(Color.Parse("#9DB8FF"));

    private Rect _current, _target;
    private double _vx, _vw;
    private bool _animating, _placed;
    private TimeSpan _last;
    private readonly PillLayer _layer;

    public SlidingPillPanel()
    {
        Orientation = Avalonia.Layout.Orientation.Horizontal;
        Spacing = 2;
        _layer = new PillLayer(this) { ZIndex = -1, IsHitTestVisible = false };
        VisualChildren.Add(_layer);
    }

    protected override void ChildrenChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        base.ChildrenChanged(sender, e);
        if (!VisualChildren.Contains(_layer)) VisualChildren.Add(_layer);
        foreach (var child in Children.OfType<TabItem>())
        {
            child.PropertyChanged -= Tab_PropertyChanged;
            child.PropertyChanged += Tab_PropertyChanged;
        }
    }

    private void Tab_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TabItem.IsSelectedProperty || e.Property == BoundsProperty) Retarget();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = base.MeasureOverride(availableSize);
        _layer.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        _layer.Arrange(new Rect(finalSize));
        Retarget();
        return size;
    }

    private void Retarget()
    {
        var selected = Children.OfType<TabItem>().FirstOrDefault(tab => tab.IsSelected && tab.IsVisible);
        if (selected is null) return;
        var target = selected.Bounds.Deflate(new Thickness(0, 3));
        if (target == _target) return;
        _target = target;
        if (!_placed || Motion.Reduced || TopLevel.GetTopLevel(this) is not { } host)
        {
            _placed = target.Width > 0;
            _current = target;
            _layer.InvalidateVisual();
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
        var x = Motion.Spring(_current.X, ref _vx, _target.X, dt, 260, 26);
        var width = Motion.Spring(_current.Width, ref _vw, _target.Width, dt, 260, 26);
        _current = new Rect(x, _target.Y, Math.Max(0, width), _target.Height);
        _layer.InvalidateVisual();
        var settled = Math.Abs(_target.X - x) < 0.3 && Math.Abs(_target.Width - width) < 0.3 && Math.Abs(_vx) < 2 && Math.Abs(_vw) < 2;
        if (settled || Motion.Reduced)
        {
            _current = _target;
            _vx = _vw = 0;
            _animating = false;
            _layer.InvalidateVisual();
            return;
        }
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(Step);
    }

    private void RenderPill(DrawingContext context)
    {
        if (_current.Width <= 0) return;
        context.DrawRectangle(PillFill, PillEdge, _current, 10, 10);
        var glowWidth = Math.Min(28, _current.Width * 0.4);
        var glow = new Rect(_current.Center.X - glowWidth / 2, _current.Bottom - 2, glowWidth, 2);
        context.DrawRectangle(Glow, null, glow, 1, 1);
    }

    private sealed class PillLayer(SlidingPillPanel owner) : Control
    {
        public override void Render(DrawingContext context) => owner.RenderPill(context);
    }
}
