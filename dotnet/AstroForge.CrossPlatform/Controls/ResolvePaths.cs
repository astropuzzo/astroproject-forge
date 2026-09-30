using System.Globalization;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AstroForge.CrossPlatform.Controls;

public sealed record ResolveNode(string Title, string Detail);

/// <summary>
/// The open calibration choice as a small routing diagram: the Light group on the left, the candidate masters
/// on the right, and light flowing along the path Forge recommends. Clicking a candidate highlights its path.
/// </summary>
public sealed class ResolvePaths : AmbientControl
{
    public static readonly StyledProperty<ResolveNode?> SourceProperty = AvaloniaProperty.Register<ResolvePaths, ResolveNode?>(nameof(Source));
    public static readonly StyledProperty<IReadOnlyList<ResolveNode>?> OptionsProperty = AvaloniaProperty.Register<ResolvePaths, IReadOnlyList<ResolveNode>?>(nameof(Options));
    public static readonly StyledProperty<int> SelectedProperty = AvaloniaProperty.Register<ResolvePaths, int>(nameof(Selected));
    public static readonly StyledProperty<Color> AccentProperty = AvaloniaProperty.Register<ResolvePaths, Color>(nameof(Accent), Color.Parse("#9DB8FF"));

    private static readonly Typeface Body = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface Mono = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#JetBrains Mono"));
    private static readonly IBrush NodeFill = new ImmutableSolidColorBrush(Color.Parse("#B30A0F22"));
    private static readonly IPen NodeLine = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#52AABEFF")), 1);
    private static readonly IPen Alternative = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#528E97BD")), 1.2, new ImmutableDashStyle([2, 4], 0));
    private static readonly IBrush Fg = new ImmutableSolidColorBrush(Color.Parse("#EEF1FF"));
    private static readonly IBrush Muted = new ImmutableSolidColorBrush(Color.Parse("#8E97BD"));
    private static readonly IBrush Warn = new ImmutableSolidColorBrush(Color.Parse("#FFC27A"));
    private Rect[] _optionRects = [];

    static ResolvePaths() => AffectsRender<ResolvePaths>(SourceProperty, OptionsProperty, SelectedProperty, AccentProperty);

    public ResolvePaths() => Cursor = new Cursor(StandardCursorType.Hand);

    public event EventHandler<int>? OptionInvoked;

    public ResolveNode? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public IReadOnlyList<ResolveNode>? Options { get => GetValue(OptionsProperty); set => SetValue(OptionsProperty, value); }
    public int Selected { get => GetValue(SelectedProperty); set => SetValue(SelectedProperty, value); }
    public Color Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    protected override bool WantsAmbient => Source is not null && Options is { Count: > 0 };

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width, 112);

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var point = e.GetPosition(this);
        for (var index = 0; index < _optionRects.Length; index++)
            if (_optionRects[index].Contains(point))
            {
                Selected = index;
                OptionInvoked?.Invoke(this, index);
                return;
            }
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || Source is not { } source) return;
        var options = Options ?? [];
        var nodeWidth = Math.Min(170, bounds.Width * 0.36);
        var sourceRect = new Rect(0, bounds.Height / 2 - 21, nodeWidth * 0.86, 42);
        var slots = Math.Max(1, Math.Min(options.Count, 3));
        var spacing = bounds.Height / slots;
        _optionRects = Enumerable.Range(0, Math.Min(options.Count, 3))
            .Select(index => new Rect(bounds.Width - nodeWidth, spacing * index + (spacing - 42) / 2, nodeWidth, 42)).ToArray();
        var from = new Point(sourceRect.Right, sourceRect.Center.Y);

        if (_optionRects.Length == 0)
        {
            var missing = new Rect(bounds.Width - nodeWidth, bounds.Height / 2 - 21, nodeWidth, 42);
            context.DrawGeometry(null, Alternative, Curve(from, new Point(missing.X, missing.Center.Y)));
            DrawNode(context, missing, new ResolveNode(CanvasText.T("Nessun candidato"), CanvasText.T("da importare")), Warn);
        }

        for (var index = 0; index < _optionRects.Length; index++)
        {
            var to = new Point(_optionRects[index].X, _optionRects[index].Center.Y);
            var curve = Curve(from, to);
            if (index != Selected) context.DrawGeometry(null, Alternative, curve);
        }
        if (Selected >= 0 && Selected < _optionRects.Length)
        {
            var to = new Point(_optionRects[Selected].X, _optionRects[Selected].Center.Y);
            var curve = Curve(from, to);
            var accent = Accent;
            context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(70, accent.R, accent.G, accent.B)), 5, lineCap: PenLineCap.Round), curve);
            // Photons travel along the recommended path.
            var dash = new DashStyle([2, 5], -Clock * 18);
            context.DrawGeometry(null, new Pen(new SolidColorBrush(accent), 1.8, dash, PenLineCap.Round), curve);
        }

        DrawNode(context, sourceRect, source, Fg);
        for (var index = 0; index < _optionRects.Length; index++)
            DrawNode(context, _optionRects[index], options[index], index == Selected ? Fg : Muted, index == Selected ? Accent : null);
    }

    private static Geometry Curve(Point from, Point to)
    {
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        var mid = (from.X + to.X) / 2;
        g.BeginFigure(from, false);
        g.CubicBezierTo(new Point(mid, from.Y), new Point(mid, to.Y), to);
        g.EndFigure(false);
        return geometry;
    }

    private static void DrawNode(DrawingContext context, Rect rect, ResolveNode node, IBrush titleBrush, Color? accent = null)
    {
        var border = accent is { } colour ? new Pen(new SolidColorBrush(colour), 1.2) : NodeLine;
        context.DrawRectangle(NodeFill, border, rect, 10, 10);
        var width = rect.Width - 22;
        var title = new FormattedText(node.Title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Body, 11.5, titleBrush) { MaxTextWidth = width, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        var detail = new FormattedText(node.Detail, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 9, Muted) { MaxTextWidth = width, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        var top = rect.Center.Y - (title.Height + detail.Height) / 2;
        context.DrawText(title, new Point(rect.X + 11, top));
        context.DrawText(detail, new Point(rect.X + 11, top + title.Height));
    }
}
