using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>One stop of the tour: the workspace tab to open, the real control to light up and what to say about it.</summary>
public sealed record TourStop(int Tab, Func<Control?> Target, string Title, string Body);

/// <summary>
/// A guided tour over the real interface. The window dims like the sky at dusk and a finder reticle slews from one
/// control to the next, opening the right workspace on the way; a card beside it says what the control is for.
/// The highlighted control stays usable, so people can try it while the tour waits.
/// </summary>
public sealed class GuidedTour : Panel
{
    private readonly TourScrim _scrim = new();
    private readonly Border _card;
    private readonly TextBlock _step = new() { Classes = { "eyebrow" } };
    private readonly TextBlock _title = new() { FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _body = new() { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _back = new();
    private readonly Button _next = new() { Classes = { "primary" } };
    private readonly Button _close = new() { Content = "✕", Padding = new Thickness(9, 4) };
    private IReadOnlyList<TourStop> _stops = [];
    private Action<int>? _selectTab;
    private int _index;
    private int _generation;

    public GuidedTour()
    {
        IsVisible = false;
        ZIndex = 60;
        _back.Click += (_, _) => Go(_index - 1);
        _next.Click += (_, _) => { if (_index + 1 >= _stops.Count) Stop(); else Go(_index + 1); };
        _close.Click += (_, _) => Stop();
        var header = new Grid { ColumnDefinitions = new("*,Auto") };
        header.Children.Add(_step);
        Grid.SetColumn(_close, 1);
        header.Children.Add(_close);
        var footer = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        footer.Children.Add(_dots);
        Grid.SetColumn(_back, 1);
        Grid.SetColumn(_next, 2);
        footer.Children.Add(_back);
        footer.Children.Add(_next);
        _card = new Border
        {
            Classes = { "card", "hero" },
            Background = new ImmutableSolidColorBrush(Color.Parse("#F20C1328")),
            Width = 360,
            Padding = new Thickness(20, 16),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new StackPanel { Spacing = 8, Children = { header, _title, _body, footer } },
        };
        Children.Add(_scrim);
        Children.Add(_card);
    }

    public bool IsRunning => IsVisible;

    /// <summary>Raised when the tour ends, finished or dismissed.</summary>
    public event EventHandler? Finished;

    public void Start(IReadOnlyList<TourStop> stops, int from, Action<int> selectTab)
    {
        if (stops.Count == 0) return;
        _stops = stops;
        _selectTab = selectTab;
        _scrim.Reset(Bounds);
        IsVisible = true;
        Go(Math.Clamp(from, 0, stops.Count - 1));
    }

    public void Stop()
    {
        if (!IsVisible) return;
        _generation++;
        IsVisible = false;
        Finished?.Invoke(this, EventArgs.Empty);
    }

    public void Next() { if (_index + 1 < _stops.Count) Go(_index + 1); else Stop(); }
    public void Previous() { if (_index > 0) Go(_index - 1); }

    private async void Go(int index)
    {
        var generation = ++_generation;
        _index = index;
        var stop = _stops[index];
        _selectTab?.Invoke(stop.Tab);
        _step.Text = string.Format(CanvasText.T("TOUR · {0} DI {1}"), index + 1, _stops.Count);
        _title.Text = CanvasText.T(stop.Title);
        _body.Text = CanvasText.T(stop.Body);
        _back.Content = CanvasText.T("Indietro");
        _back.IsEnabled = index > 0;
        _next.Content = index + 1 < _stops.Count ? CanvasText.T("Avanti") : CanvasText.T("Inizia a lavorare");
        _dots.Children.Clear();
        for (var dot = 0; dot < _stops.Count; dot++)
            _dots.Children.Add(new Border { Width = dot == index ? 16 : 6, Height = 6, CornerRadius = new CornerRadius(3), Background = new ImmutableSolidColorBrush(dot == index ? Color.Parse("#3FE0D0") : Color.Parse("#405B6490")) });
        _card.Opacity = 0;

        // Let the workspace transition and the first layout pass of a freshly opened tab settle before measuring.
        await Task.Delay(Motion.Reduced ? 60 : 380);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        if (generation != _generation || !IsVisible) return;

        var target = stop.Target();
        Rect? spot = null;
        if (target is { IsEffectivelyVisible: true } && target.Bounds.Width > 0 && target.TranslatePoint(default, this) is { } origin)
        {
            var rect = new Rect(origin, target.Bounds.Size).Inflate(8).Intersect(new Rect(Bounds.Size).Deflate(6));
            if (rect.Width > 20 && rect.Height > 20) spot = rect;
        }
        _scrim.SlewTo(spot, Bounds);
        PlaceCard(spot);
        _ = Motion.Tween(TopLevel.GetTopLevel(this), TimeSpan.FromMilliseconds(320), Motion.EaseOutExpo, t => _card.Opacity = t, TimeSpan.FromMilliseconds(Motion.Reduced ? 0 : 260));
    }

    private void PlaceCard(Rect? spot)
    {
        _card.Measure(new Size(_card.Width, double.PositiveInfinity));
        var size = new Size(_card.Width, _card.DesiredSize.Height);
        const double gap = 22, edge = 16;
        Point at;
        if (spot is not { } s) at = new((Bounds.Width - size.Width) / 2, (Bounds.Height - size.Height) / 2);
        else if (s.Right + gap + size.Width + edge <= Bounds.Width) at = new(s.Right + gap, s.Y);
        else if (s.X - gap - size.Width >= edge) at = new(s.X - gap - size.Width, s.Y);
        else if (s.Bottom + gap + size.Height + edge <= Bounds.Height) at = new(s.X, s.Bottom + gap);
        else if (s.Y - gap - size.Height >= edge) at = new(s.X, s.Y - gap - size.Height);
        else at = new(s.Right - size.Width - edge, s.Bottom - size.Height - edge);
        at = new(Math.Clamp(at.X, edge, Math.Max(edge, Bounds.Width - size.Width - edge)), Math.Clamp(at.Y, edge, Math.Max(edge, Bounds.Height - size.Height - edge)));
        _card.Margin = new Thickness(at.X, at.Y, 0, 0);
    }
}

/// <summary>The dusk over the window, with a clear hole and a finder reticle around the control being shown.</summary>
internal sealed class TourScrim : AmbientControl
{
    private static readonly Color Oiii = Color.Parse("#3FE0D0");
    private static readonly IBrush Dusk = new ImmutableSolidColorBrush(Color.Parse("#C2050914"));
    private Rect _from, _to, _spot;
    private bool _hasSpot;

    public void Reset(Rect bounds)
    {
        _spot = _from = _to = new Rect(bounds.Center, new Size(0, 0));
        _hasSpot = false;
    }

    public void SlewTo(Rect? target, Rect bounds)
    {
        _from = _hasSpot ? _spot : target is { } t ? new Rect(t.Center, new Size(0, 0)) : new Rect(bounds.Center, new Size(0, 0));
        _to = target ?? new Rect(bounds.Center, new Size(0, 0));
        _hasSpot = target is not null;
        _ = Motion.Tween(TopLevel.GetTopLevel(this), TimeSpan.FromMilliseconds(520), Motion.EaseInOutCubic, t =>
        {
            _spot = new Rect(Lerp(_from.X, _to.X, t), Lerp(_from.Y, _to.Y, t), Lerp(_from.Width, _to.Width, t), Lerp(_from.Height, _to.Height, t));
            InvalidateVisual();
        });
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        var hole = _spot;
        var radius = Math.Min(14, Math.Min(hole.Width, hole.Height) / 2);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.SetFillRule(FillRule.EvenOdd);
            g.BeginFigure(bounds.TopLeft, true);
            g.LineTo(bounds.TopRight); g.LineTo(bounds.BottomRight); g.LineTo(bounds.BottomLeft);
            g.EndFigure(true);
            if (hole.Width > 1 && hole.Height > 1)
            {
                g.BeginFigure(new Point(hole.X + radius, hole.Y), true);
                g.LineTo(new Point(hole.Right - radius, hole.Y));
                g.ArcTo(new Point(hole.Right, hole.Y + radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
                g.LineTo(new Point(hole.Right, hole.Bottom - radius));
                g.ArcTo(new Point(hole.Right - radius, hole.Bottom), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
                g.LineTo(new Point(hole.X + radius, hole.Bottom));
                g.ArcTo(new Point(hole.X, hole.Bottom - radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
                g.LineTo(new Point(hole.X, hole.Y + radius));
                g.ArcTo(new Point(hole.X + radius, hole.Y), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
                g.EndFigure(true);
            }
        }
        context.DrawGeometry(Dusk, null, geometry);
        if (hole.Width < 8 || hole.Height < 8) return;

        // Finder reticle: a soft glow on the rim, corner brackets that breathe and a slow marching outline.
        var breathe = Motion.Reduced ? 0.5 : 0.5 + 0.5 * Math.Sin(Clock * 2.2);
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(40 + 40 * breathe), Oiii.R, Oiii.G, Oiii.B)), 6), hole, radius, radius);
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(150, Oiii.R, Oiii.G, Oiii.B)), 1, new DashStyle([4, 4], -Clock * 6)), hole, radius, radius);
        var arm = Math.Min(22, Math.Min(hole.Width, hole.Height) / 3);
        var push = 5 + 3 * breathe;
        var pen = new Pen(new SolidColorBrush(Oiii), 2, lineCap: PenLineCap.Round);
        var outer = hole.Inflate(push);
        foreach (var (corner, dx, dy) in new[] { (outer.TopLeft, 1, 1), (outer.TopRight, -1, 1), (outer.BottomRight, -1, -1), (outer.BottomLeft, 1, -1) })
        {
            context.DrawLine(pen, corner, corner + new Vector(arm * dx, 0));
            context.DrawLine(pen, corner, corner + new Vector(0, arm * dy));
        }
    }
}
