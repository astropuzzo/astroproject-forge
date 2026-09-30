using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>A folder of the exported project, with the files it will hold and the filter colour it carries.</summary>
public sealed record ExportNode(string Name, int Files, Color? Colour, IReadOnlyList<ExportNode> Children);

/// <summary>
/// The project as it will land on disk, drawn as a tree growing from the project folder to its filter and night
/// folders. While the copy runs, light travels from the root to the leaves at the pace of the export; a light
/// shimmer runs along the branches while it waits.
/// </summary>
public sealed class ExportMap : AmbientControl
{
    public static readonly StyledProperty<ExportNode?> RootProperty =
        AvaloniaProperty.Register<ExportMap, ExportNode?>(nameof(Root));
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<ExportMap, double>(nameof(Progress));
    public static readonly StyledProperty<bool> IsRunningProperty =
        AvaloniaProperty.Register<ExportMap, bool>(nameof(IsRunning));
    public static readonly StyledProperty<bool> IsPreviewProperty =
        AvaloniaProperty.Register<ExportMap, bool>(nameof(IsPreview));

    private static readonly Typeface Mono = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#JetBrains Mono"));
    private static readonly Typeface Body = new(new FontFamily("Inter"), weight: FontWeight.SemiBold);
    private static readonly Typeface Display = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#Unbounded"), weight: FontWeight.Medium);
    private static readonly Color Accent = Color.Parse("#9DB8FF"), Oiii = Color.Parse("#3FE0D0");
    private static readonly IBrush Fg = new ImmutableSolidColorBrush(Color.Parse("#EEF1FF"));
    private static readonly IBrush LabelPlate = new ImmutableSolidColorBrush(Color.Parse("#B80A0F22"));
    private static readonly IBrush Muted = new ImmutableSolidColorBrush(Color.Parse("#8E97BD"));

    private const double LeafPitch = 34, Top = 14, MaxDepth = 3;

    private double _reveal = 1, _shownProgress, _flash;

    static ExportMap() => AffectsMeasure<ExportMap>(RootProperty);

    public ExportNode? Root { get => GetValue(RootProperty); set => SetValue(RootProperty, value); }
    /// <summary>Export progress from 0 to 1.</summary>
    public double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public bool IsRunning { get => GetValue(IsRunningProperty); set => SetValue(IsRunningProperty, value); }
    /// <summary>True while the tree is only the expected layout, before a real plan exists.</summary>
    public bool IsPreview { get => GetValue(IsPreviewProperty); set => SetValue(IsPreviewProperty, value); }

    protected override bool WantsAmbient => Root is not null;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        var host = TopLevel.GetTopLevel(this);
        if (change.Property == RootProperty && change.OldValue is null != change.NewValue is null || change.Property == RootProperty && Shape(change.OldValue as ExportNode) != Shape(change.NewValue as ExportNode))
        {
            _reveal = 0;
            _ = Motion.Tween(host, TimeSpan.FromMilliseconds(1100), t => t, t => { _reveal = t; InvalidateVisual(); });
        }
        else if (change.Property == ProgressProperty)
        {
            var from = _shownProgress;
            var to = Math.Clamp(Progress, 0, 1);
            if (to >= 1 && from < 1)
                _ = Motion.Tween(host, TimeSpan.FromMilliseconds(1300), t => t, t => { _flash = 1 - t; InvalidateVisual(); });
            _ = Motion.Tween(host, TimeSpan.FromMilliseconds(500), Motion.EaseOutExpo, t => { _shownProgress = from + (to - from) * t; InvalidateVisual(); });
        }
        InvalidateVisual();
    }

    private static string Shape(ExportNode? node) => node is null ? "" : $"{node.Name}({string.Join(',', node.Children.Select(Shape))})";

    private static IEnumerable<ExportNode> Leaves(ExportNode node, int depth) =>
        node.Children.Count == 0 || depth >= MaxDepth ? [node] : node.Children.SelectMany(child => Leaves(child, depth + 1));

    protected override Size MeasureOverride(Size availableSize)
    {
        var leaves = Root is null ? 4 : Leaves(Root, 0).Count();
        return new(double.IsInfinity(availableSize.Width) ? 700 : availableSize.Width, Top * 2 + Math.Max(4, leaves) * LeafPitch);
    }

    private sealed class Placed(ExportNode node, int depth, Placed? parent)
    {
        public ExportNode Node { get; } = node;
        public int Depth { get; } = depth;
        public Placed? Parent { get; } = parent;
        public Point At { get; set; }
    }

    private List<Placed> Place()
    {
        var placed = new List<Placed>();
        if (Root is null) return placed;
        var columns = Math.Max(1, Math.Min(MaxDepth, Depth(Root, 0)));
        var columnWidth = (Bounds.Width - 250) / columns;
        var leaf = 0;

        Placed Visit(ExportNode node, int depth, Placed? parent)
        {
            var item = new Placed(node, depth, parent);
            placed.Add(item);
            var x = 14 + depth * columnWidth;
            if (node.Children.Count == 0 || depth >= MaxDepth) item.At = new Point(x, Top + LeafPitch * (leaf++ + 0.5));
            else item.At = new Point(x, node.Children.Select(child => Visit(child, depth + 1, item)).ToList().Average(child => child.At.Y));
            return item;
        }

        Visit(Root, 0, null);
        return placed;
    }

    private static int Depth(ExportNode node, int depth) => node.Children.Count == 0 ? depth : node.Children.Max(child => Depth(child, depth + 1));

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0) return;
        var culture = CultureInfo.CurrentCulture;
        if (Root is null)
        {
            var empty = new FormattedText(CanvasText.T("Nessuna struttura. Analizza il progetto."), culture, FlowDirection.LeftToRight, Mono, 11, Muted);
            context.DrawText(empty, new Point(0, Top + 20));
            return;
        }

        var placed = Place();
        var maxFiles = Math.Max(1, placed.Max(item => item.Node.Files));
        var right = placed.Max(item => item.At.X);
        var threshold = 14 + (right - 14 + 12) * Math.Clamp(_shownProgress, 0, 1);
        var columns = Math.Max(1, placed.Max(item => item.Depth));
        // The waiting shimmer: a band of light that runs from the root to the leaves every few seconds.
        var shimmer = Motion.Reduced ? double.NaN : 14 + (right + 160) * ((Clock / 4.2) % 1) - 80;

        foreach (var item in placed.Where(item => item.Parent is not null))
        {
            var appear = Math.Clamp(_reveal * (columns + 1.5) - (item.Depth - 1), 0, 1);
            if (appear <= 0) continue;
            var colour = item.Node.Colour ?? item.Parent!.Node.Colour ?? Accent;
            var from = item.Parent!.At;
            var to = item.At;
            var mid = (to.X - from.X) * 0.55;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(from, false);
                g.CubicBezierTo(new Point(from.X + mid, from.Y), new Point(to.X - mid, to.Y), to);
                g.EndFigure(false);
            }
            var width = 1.2 + 3 * Math.Sqrt(item.Node.Files / (double)maxFiles);
            using (context.PushOpacity(appear))
            {
                context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(IsPreview ? (byte)40 : (byte)70, colour.R, colour.G, colour.B)), width, lineCap: PenLineCap.Round), geometry);
                if (_shownProgress > 0)
                    using (context.PushClip(new Rect(0, 0, threshold, Bounds.Height)))
                    {
                        context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(60, colour.R, colour.G, colour.B)), width * 3.2, lineCap: PenLineCap.Round), geometry);
                        context.DrawGeometry(null, new Pen(new SolidColorBrush(colour), width, lineCap: PenLineCap.Round), geometry);
                    }
                if (!double.IsNaN(shimmer) && !IsRunning && _shownProgress < 1)
                    using (context.PushClip(new Rect(shimmer, 0, 80, Bounds.Height)))
                        context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(110, colour.R, colour.G, colour.B)), width, lineCap: PenLineCap.Round), geometry);
            }
        }

        // Sparks ride the copy front while the export runs.
        if (IsRunning && !Motion.Reduced && _shownProgress is > 0 and < 1)
            foreach (var item in placed.Where(item => item.Parent is not null && item.Parent.At.X < threshold && item.At.X >= threshold - 4))
            {
                var colour = item.Node.Colour ?? item.Parent!.Node.Colour ?? Accent;
                var span = Math.Max(1, item.At.X - item.Parent!.At.X);
                var t = Math.Clamp((threshold - item.Parent.At.X) / span, 0, 1);
                var y = item.Parent.At.Y + (item.At.Y - item.Parent.At.Y) * (3 * t * t - 2 * t * t * t);
                var flicker = 0.7 + 0.3 * Math.Sin(Clock * 18 + item.At.Y);
                context.DrawEllipse(new ImmutableSolidColorBrush(Color.FromArgb((byte)(90 * flicker), colour.R, colour.G, colour.B)), null, new Point(threshold, y), 7, 7);
                context.DrawEllipse(new ImmutableSolidColorBrush(Colors.White), null, new Point(threshold, y), 2, 2);
            }

        foreach (var item in placed)
        {
            var appear = Math.Clamp(_reveal * (columns + 1.5) - item.Depth + 0.3, 0, 1);
            if (appear <= 0) continue;
            var colour = item.Node.Colour ?? item.Parent?.Node.Colour ?? Accent;
            var lit = _shownProgress > 0 && item.At.X <= threshold;
            var radius = item.Depth == 0 ? 7 : 3 + 4 * Math.Sqrt(item.Node.Files / (double)maxFiles);
            using (context.PushOpacity(appear))
            {
                if (lit || item.Depth == 0)
                    context.DrawEllipse(new ImmutableSolidColorBrush(Color.FromArgb(50, colour.R, colour.G, colour.B)), null, item.At, radius + 6, radius + 6);
                if (_flash > 0)
                    context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(160 * _flash), Oiii.R, Oiii.G, Oiii.B)), 1.2), item.At, radius + 4 + 16 * (1 - _flash), radius + 4 + 16 * (1 - _flash));
                context.DrawEllipse(lit || item.Depth == 0 ? new ImmutableSolidColorBrush(colour) : new ImmutableSolidColorBrush(Color.Parse("#0C1126")),
                    new Pen(new SolidColorBrush(colour), 1.3), item.At, radius, radius);

                var leaf = item.Node.Children.Count == 0 || item.Depth >= MaxDepth;
                var name = new FormattedText(item.Node.Name, culture, FlowDirection.LeftToRight, item.Depth == 0 ? Display : Body, item.Depth == 0 ? 12 : 11, Fg)
                {
                    MaxTextWidth = leaf ? 236 : 170, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis
                };
                var count = new FormattedText(item.Node.Files == 1 ? "1 file" : $"{item.Node.Files} file", culture, FlowDirection.LeftToRight, Mono, 9, Muted);
                if (leaf)
                {
                    context.DrawText(name, new Point(item.At.X + radius + 8, item.At.Y - name.Height + 2));
                    context.DrawText(count, new Point(item.At.X + radius + 8, item.At.Y + 1));
                }
                else
                {
                    // Branch labels sit above their node on a dark plate, clear of the links that leave it.
                    var origin = new Point(item.At.X - 4, item.At.Y - radius - name.Height - count.Height - 3);
                    context.DrawRectangle(LabelPlate, null, new Rect(origin.X - 4, origin.Y - 1, Math.Max(name.Width, count.Width) + 8, name.Height + count.Height + 3), 5, 5);
                    context.DrawText(name, origin);
                    context.DrawText(count, new Point(origin.X, origin.Y + name.Height));
                }
            }
        }

        if (IsPreview)
        {
            var caption = new FormattedText(CanvasText.T("struttura provvisoria · calibrazioni da completare"), culture, FlowDirection.LeftToRight, Mono, 9.5, Muted);
            context.DrawText(caption, new Point(Bounds.Width - caption.Width, Bounds.Height - caption.Height));
        }
    }
}
