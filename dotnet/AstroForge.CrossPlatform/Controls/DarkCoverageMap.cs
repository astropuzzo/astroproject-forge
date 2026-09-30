using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>One exposure × temperature slot: the Dark masters on the shelf and the Lights that need one.</summary>
public sealed record CoverageCell(double Exposure, double Temperature, int Masters, int Lights, bool Covered);

/// <summary>A gain (and offset) setting of the camera, with its Dark slots and how many Bias masters back it.</summary>
public sealed record CoverageGroup(string Title, IReadOnlyList<CoverageCell> Cells, int BiasMasters);

/// <summary>
/// The Dark library against the project's needs: exposures across, sensor temperatures down. Blue discs are masters on
/// the shelf, rings are Lights: teal when a master covers them, red and breathing when none does yet.
/// </summary>
public sealed class DarkCoverageMap : AmbientControl
{
    public static readonly StyledProperty<IReadOnlyList<CoverageGroup>?> GroupsProperty =
        AvaloniaProperty.Register<DarkCoverageMap, IReadOnlyList<CoverageGroup>?>(nameof(Groups));

    private static readonly Typeface Mono = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#JetBrains Mono"));
    private static readonly Typeface MonoBold = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#JetBrains Mono"), weight: FontWeight.Medium);
    private static readonly Color Dark = Color.Parse("#6F8CFF"), Oiii = Color.Parse("#3FE0D0"), Ha = Color.Parse("#FF5D73"), Bias = Color.Parse("#B89CFF");
    private static readonly IBrush Fg = new ImmutableSolidColorBrush(Color.Parse("#EEF1FF"));
    private static readonly IBrush Muted = new ImmutableSolidColorBrush(Color.Parse("#8E97BD"));
    private static readonly IBrush Dim = new ImmutableSolidColorBrush(Color.Parse("#5B6490"));
    private static readonly IPen Grid = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#1496AAFF")), 1);

    private const double RowHeight = 40, HeaderHeight = 44, LabelWidth = 64, MinColumn = 70, GroupGap = 18;

    private double _reveal = 1;

    static DarkCoverageMap() => AffectsMeasure<DarkCoverageMap>(GroupsProperty);

    public IReadOnlyList<CoverageGroup>? Groups { get => GetValue(GroupsProperty); set => SetValue(GroupsProperty, value); }

    private IReadOnlyList<CoverageGroup> Items => Groups ?? [];
    protected override bool WantsAmbient => Items.Any(group => group.Cells.Any(cell => cell.Lights > 0 && !cell.Covered));

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != GroupsProperty) return;
        _reveal = 0;
        _ = Motion.Tween(TopLevel.GetTopLevel(this), TimeSpan.FromMilliseconds(900), t => t, t => { _reveal = t; InvalidateVisual(); });
    }

    private static double[] Temperatures(CoverageGroup group) => group.Cells.Select(cell => cell.Temperature).Distinct().OrderByDescending(t => t).ToArray();
    private static double[] Exposures(CoverageGroup group) => group.Cells.Select(cell => cell.Exposure).Distinct().Order().ToArray();

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = Items.Count == 0 ? 60 : Items.Sum(group => HeaderHeight + Math.Max(1, Temperatures(group).Length) * RowHeight + 22) + GroupGap * (Items.Count - 1);
        return new(double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width, height);
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0) return;
        var culture = CultureInfo.CurrentCulture;
        if (Items.Count == 0)
        {
            var empty = new FormattedText(CanvasText.T("Nessun dato. Collega una libreria Master o analizza il progetto."), culture, FlowDirection.LeftToRight, Mono, 11, Muted) { MaxTextWidth = Bounds.Width };
            context.DrawText(empty, new Point(0, 20));
            return;
        }

        var top = 0.0;
        var index = 0;
        foreach (var group in Items)
        {
            var title = new FormattedText(group.Title, culture, FlowDirection.LeftToRight, MonoBold, 10.5, Fg);
            context.DrawText(title, new Point(0, top));
            var biasColour = group.BiasMasters > 0 ? Bias : Ha;
            var bias = new FormattedText(group.BiasMasters > 0 ? string.Format(culture, CanvasText.T("Bias · {0} master"), group.BiasMasters) : CanvasText.T("Bias mancante"),
                culture, FlowDirection.LeftToRight, Mono, 10, new ImmutableSolidColorBrush(biasColour));
            var chip = new Rect(title.Width + 14, top - 2, bias.Width + 16, 18);
            context.DrawRectangle(new ImmutableSolidColorBrush(Color.FromArgb(34, biasColour.R, biasColour.G, biasColour.B)), new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(110, biasColour.R, biasColour.G, biasColour.B)), 1), chip, 9, 9);
            context.DrawText(bias, new Point(chip.X + 8, chip.Y + 2));

            if (index == 0) DrawLegend(context, culture, top);
            var temperatures = Temperatures(group);
            var exposures = Exposures(group);
            var gridTop = top + HeaderHeight - 8;
            var columnWidth = Math.Max(MinColumn, (Bounds.Width - LabelWidth) / Math.Max(1, exposures.Length));
            for (var column = 0; column < exposures.Length; column++)
            {
                var label = new FormattedText(exposures[column].ToString("0.###", culture) + " s", culture, FlowDirection.LeftToRight, Mono, 9.5, Dim);
                context.DrawText(label, new Point(LabelWidth + column * columnWidth + columnWidth / 2 - label.Width / 2, gridTop - 16));
            }
            for (var row = 0; row < temperatures.Length; row++)
            {
                var y = gridTop + row * RowHeight;
                context.DrawLine(Grid, new Point(LabelWidth - 6, y + RowHeight / 2), new Point(Bounds.Width, y + RowHeight / 2));
                var label = new FormattedText(temperatures[row].ToString("0.#", culture) + " °C", culture, FlowDirection.LeftToRight, Mono, 9.5, Dim);
                context.DrawText(label, new Point(0, y + RowHeight / 2 - label.Height / 2));
            }

            foreach (var cell in group.Cells)
            {
                var column = Array.IndexOf(exposures, cell.Exposure);
                var row = Array.IndexOf(temperatures, cell.Temperature);
                var centre = new Point(LabelWidth + column * columnWidth + columnWidth / 2, gridTop + row * RowHeight + RowHeight / 2);
                var appear = Motion.EaseOutBack(Math.Clamp(_reveal * 1.6 - index++ * 0.04, 0, 1));
                if (appear <= 0) continue;
                using (context.PushOpacity(Math.Clamp(appear, 0, 1)))
                {
                    if (cell.Lights > 0)
                    {
                        var colour = cell.Covered ? Oiii : Ha;
                        var breathe = cell.Covered || Motion.Reduced ? 0 : 0.5 + 0.5 * Math.Sin(Clock * 2.6 + column + row);
                        var ring = 14 + 3 * breathe;
                        context.DrawEllipse(new ImmutableSolidColorBrush(Color.FromArgb((byte)(26 + 30 * breathe), colour.R, colour.G, colour.B)), null, centre, ring + 4, ring + 4);
                        context.DrawEllipse(null, new Pen(new SolidColorBrush(colour), 1.6, cell.Covered ? null : new DashStyle([3, 2], 0)), centre, ring * appear, ring * appear);
                        var count = new FormattedText($"{cell.Lights}L", culture, FlowDirection.LeftToRight, Mono, 9, new ImmutableSolidColorBrush(colour));
                        context.DrawText(count, new Point(centre.X + ring + 4, centre.Y - count.Height - 1));
                    }
                    if (cell.Masters > 0)
                    {
                        var disc = (6 + Math.Min(4, cell.Masters)) * appear;
                        var glow = new RadialGradientBrush { GradientStops = { new GradientStop(Color.FromArgb(150, Dark.R, Dark.G, Dark.B), 0), new GradientStop(Color.FromArgb(0, Dark.R, Dark.G, Dark.B), 1) } };
                        context.DrawEllipse(glow, null, centre, disc * 2, disc * 2);
                        context.DrawEllipse(new ImmutableSolidColorBrush(Dark), new Pen(Fg, 1), centre, disc, disc);
                        if (cell.Masters > 1)
                        {
                            var count = new FormattedText(cell.Masters.ToString(culture), culture, FlowDirection.LeftToRight, MonoBold, 9, Fg);
                            context.DrawText(count, centre - new Vector(count.Width / 2, count.Height / 2));
                        }
                    }
                    else if (cell.Lights == 0) context.DrawEllipse(null, new Pen(Dim, 1), centre, 4, 4);
                }
            }
            top = gridTop + temperatures.Length * RowHeight + 22 + GroupGap;
        }
    }

    private void DrawLegend(DrawingContext context, CultureInfo culture, double top)
    {
        var entries = new (string Text, Color Colour, bool Disc, bool Dashed)[]
        {
            (CanvasText.T("Master Dark"), Dark, true, false),
            (CanvasText.T("Light coperti"), Oiii, false, false),
            (CanvasText.T("Light senza Dark"), Ha, false, true),
        };
        var x = Bounds.Width;
        foreach (var (text, colour, disc, dashed) in entries.Reverse())
        {
            var label = new FormattedText(text, culture, FlowDirection.LeftToRight, Mono, 9.5, Muted);
            x -= label.Width;
            context.DrawText(label, new Point(x, top));
            var centre = new Point(x - 10, top + label.Height / 2);
            if (disc) context.DrawEllipse(new ImmutableSolidColorBrush(colour), null, centre, 4.5, 4.5);
            else context.DrawEllipse(null, new Pen(new SolidColorBrush(colour), 1.4, dashed ? new DashStyle([2, 1.5], 0) : null), centre, 5, 5);
            x -= 30;
        }
    }
}
