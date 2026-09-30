using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AstroForge.Core.Analysis;
using AstroForge.Core.Filters;
using AstroForge.Core.Matching;
using AstroForge.Core.Models;

namespace AstroForge.CrossPlatform.Controls;

public enum CalibrationCellState { Exact, Tolerance, Choose, Missing }

public sealed record CalibrationCell(string Calibration, CalibrationCellState State, string Detail)
{
    public bool Pending => State is CalibrationCellState.Choose or CalibrationCellState.Missing;
}

/// <summary>One Light group of the map: a filter on one night, and how its Flat, Dark and Bias are matched.</summary>
public sealed record CalibrationRow(string Filter, string Night, Color Colour, IReadOnlyList<FrameMetadata> Lights, IReadOnlyList<CalibrationCell> Cells)
{
    public string Key => $"{Filter}|{Night}";

    public static IReadOnlyList<CalibrationRow> Build(ProjectAnalysis? analysis, Func<FrameMetadata, Color> colourOf)
    {
        if (analysis is null) return [];
        return analysis.Lights
            .GroupBy(item => (Filter: item.Light.FilterName.Value ?? "—", Night: item.Light.SessionId.Value ?? "—"))
            .OrderBy(group => group.Key.Night, StringComparer.Ordinal).ThenBy(group => group.Key.Filter, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var items = group.ToList();
                return new CalibrationRow(group.Key.Filter, group.Key.Night, colourOf(items[0].Light), items.Select(item => item.Light).ToList(),
                [
                    Cell("Flat", items.Select(item => item.Flat).ToList(), items),
                    Cell("Dark", items.Select(item => item.Dark).ToList(), items),
                    Cell("Bias", items.Select(item => item.Bias).ToList(), items)
                ]);
            })
            .ToList();
    }

    private static CalibrationCell Cell(string calibration, IReadOnlyList<MatchResult> results, IReadOnlyList<LightCalibrationAnalysis> items)
    {
        if (results.Any(result => !result.IsAccepted))
        {
            var missing = results.Any(result => result.Status is MatchStatus.Missing or MatchStatus.Incompatible);
            var candidates = results.Max(result => result.Candidates.Count(candidate => candidate.Compatible));
            return new(calibration, missing ? CalibrationCellState.Missing : CalibrationCellState.Choose,
                missing ? CanvasText.T("Mancante") : string.Format(CultureInfo.CurrentCulture, CanvasText.T("{0} da scegliere"), Math.Max(2, candidates)));
        }
        var tolerance = results.Any(result => result.Status == MatchStatus.WithinTolerance);
        var selected = results.Select(result => result.Selected?.Frame).OfType<FrameMetadata>().Distinct().ToList();
        var detail = calibration switch
        {
            "Flat" => items.Select(item => item.FlatGroup).OfType<CalibrationGroup>().Distinct().Sum(group => group.Frames.Count) is var flats and > 0
                ? $"{flats} flat" : $"{selected.Count} flat",
            "Dark" => selected.Select(frame => frame.ExposureSeconds.Value).OfType<double>().Distinct().Order()
                .Select(seconds => seconds.ToString("0.#", CultureInfo.CurrentCulture) + " s").DefaultIfEmpty("Master").Aggregate((a, b) => $"{a} · {b}"),
            _ => selected.Any(frame => frame.IsMaster) ? "Master" : $"{selected.Count} bias"
        };
        return new(calibration, tolerance ? CalibrationCellState.Tolerance : CalibrationCellState.Exact, detail);
    }
}

/// <summary>
/// Every Light group against its Flat, Dark and Bias: matched cells glow teal, cells waiting for a choice breathe
/// amber, missing ones red. A ring counts how much of the project is calibrated; resolving a cell makes it ignite.
/// </summary>
public sealed class CalibrationMap : AmbientControl
{
    public static readonly StyledProperty<IReadOnlyList<CalibrationRow>?> RowsProperty =
        AvaloniaProperty.Register<CalibrationMap, IReadOnlyList<CalibrationRow>?>(nameof(Rows));

    public event EventHandler<(CalibrationRow Row, CalibrationCell Cell)>? CellActivated;

    private static readonly Typeface Mono = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#JetBrains Mono"));
    private static readonly Typeface MonoBold = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#JetBrains Mono"), weight: FontWeight.Medium);
    private static readonly Typeface Display = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#Unbounded"), weight: FontWeight.Medium);
    private static readonly Typeface Body = new(new FontFamily("Inter"), weight: FontWeight.SemiBold);
    private static readonly Color Oiii = Color.Parse("#3FE0D0"), Warn = Color.Parse("#FFC27A"), Ha = Color.Parse("#FF5D73"), Accent = Color.Parse("#9DB8FF");
    private static readonly IBrush Fg = new ImmutableSolidColorBrush(Color.Parse("#EEF1FF"));
    private static readonly IBrush Muted = new ImmutableSolidColorBrush(Color.Parse("#8E97BD"));
    private static readonly IBrush Dim = new ImmutableSolidColorBrush(Color.Parse("#5B6490"));
    private static readonly IPen Track = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#1F96AAFF")), 8, lineCap: PenLineCap.Round);
    private static readonly IPen RowRule = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#1296AAFF")), 1);

    private const double HeaderHeight = 24, RowPitch = 38, CellHeight = 28, LabelWidth = 200, RingWidth = 170;

    private readonly Dictionary<string, CalibrationCellState> _previous = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _ignitions = new(StringComparer.Ordinal);
    private double _reveal = 1, _shownFraction, _fromFraction;
    private (int Row, int Column)? _hover;

    static CalibrationMap() => AffectsMeasure<CalibrationMap>(RowsProperty);

    public IReadOnlyList<CalibrationRow>? Rows { get => GetValue(RowsProperty); set => SetValue(RowsProperty, value); }

    private IReadOnlyList<CalibrationRow> Items => Rows ?? [];
    private double Fraction => Items.Count == 0 ? 0 : Items.Sum(row => row.Cells.Count(cell => !cell.Pending)) / (double)Items.Sum(row => row.Cells.Count);
    protected override bool WantsAmbient => _ignitions.Count > 0 || Items.Any(row => row.Cells.Any(cell => cell.Pending));

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != RowsProperty) return;

        // Cells that were waiting and are now matched ignite; a whole new map rises in instead.
        var fresh = _previous.Count == 0 || !Items.Any(row => row.Cells.Any(cell => _previous.ContainsKey(CellKey(row, cell))));
        foreach (var row in Items)
            foreach (var cell in row.Cells)
                if (_previous.TryGetValue(CellKey(row, cell), out var before) && before is CalibrationCellState.Choose or CalibrationCellState.Missing && !cell.Pending)
                    _ignitions[CellKey(row, cell)] = Clock;
        _previous.Clear();
        foreach (var row in Items)
            foreach (var cell in row.Cells)
                _previous[CellKey(row, cell)] = cell.State;

        _fromFraction = fresh ? 0 : _shownFraction;
        var target = Fraction;
        var host = TopLevel.GetTopLevel(this);
        if (fresh)
        {
            _reveal = 0;
            _ = Motion.Tween(host, TimeSpan.FromMilliseconds(900), t => t, t => { _reveal = t; InvalidateVisual(); });
        }
        _ = Motion.Tween(host, TimeSpan.FromMilliseconds(1200), Motion.EaseOutExpo, t => { _shownFraction = _fromFraction + (target - _fromFraction) * t; InvalidateVisual(); });
        InvalidateVisual();
    }

    private static string CellKey(CalibrationRow row, CalibrationCell cell) => $"{row.Key}|{cell.Calibration}";

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 640 : availableSize.Width, Math.Max(RingWidth - 10, HeaderHeight + Math.Max(1, Items.Count) * RowPitch));

    private (Rect Grid, double ColumnWidth, Rect Ring) Layout()
    {
        var ring = Bounds.Width >= 620 ? new Rect(Bounds.Width - RingWidth, 0, RingWidth, Math.Min(Bounds.Height, 160)) : default;
        var gridWidth = Bounds.Width - (ring.Width > 0 ? RingWidth + 16 : 0);
        var labelWidth = Math.Min(LabelWidth, gridWidth * 0.36);
        var columnWidth = Math.Max(60, (gridWidth - labelWidth) / 3);
        return (new Rect(labelWidth, 0, columnWidth * 3, Bounds.Height), columnWidth, ring);
    }

    private Rect CellRect(Rect grid, double columnWidth, int row, int column) =>
        new(grid.X + column * columnWidth + 4, HeaderHeight + row * RowPitch + (RowPitch - CellHeight) / 2, columnWidth - 8, CellHeight);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = HitTestCell(e.GetPosition(this));
        if (hit == _hover) return;
        _hover = hit;
        Cursor = hit is { } h && Items[h.Row].Cells[h.Column].Pending ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (HitTestCell(e.GetPosition(this)) is not { } hit) return;
        var row = Items[hit.Row];
        CellActivated?.Invoke(this, (row, row.Cells[hit.Column]));
        e.Handled = true;
    }

    private (int Row, int Column)? HitTestCell(Point point)
    {
        var (grid, columnWidth, _) = Layout();
        for (var row = 0; row < Items.Count; row++)
            for (var column = 0; column < 3; column++)
                if (CellRect(grid, columnWidth, row, column).Inflate(3).Contains(point)) return (row, column);
        return null;
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0) return;
        var (grid, columnWidth, ring) = Layout();
        var culture = CultureInfo.CurrentCulture;

        string[] headers = ["FLAT", "DARK", "BIAS"];
        for (var column = 0; column < 3; column++)
        {
            var header = new FormattedText(headers[column], culture, FlowDirection.LeftToRight, MonoBold, 9.5, Dim);
            context.DrawText(header, new Point(grid.X + column * columnWidth + 10, 4));
        }

        if (Items.Count == 0)
        {
            var empty = new FormattedText(CanvasText.T("La mappa delle calibrazioni comparirà dopo l’analisi"), culture, FlowDirection.LeftToRight, Mono, 11, Muted);
            context.DrawText(empty, new Point(0, HeaderHeight + 10));
        }

        var total = Items.Count * 3;
        for (var row = 0; row < Items.Count; row++)
        {
            var item = Items[row];
            var top = HeaderHeight + row * RowPitch;
            var rowReveal = Motion.EaseOutExpo(Math.Clamp((_reveal * (total + 8) - row * 3) / 8, 0, 1));
            using (context.PushOpacity(rowReveal))
            {
                if (row > 0) context.DrawLine(RowRule, new Point(0, top), new Point(grid.Right, top));
                var centre = top + RowPitch / 2;
                // The filter's own glass colour, with a soft halo.
                context.DrawEllipse(new ImmutableSolidColorBrush(Color.FromArgb(60, item.Colour.R, item.Colour.G, item.Colour.B)), null, new Point(9, centre), 8, 8);
                context.DrawEllipse(new ImmutableSolidColorBrush(item.Colour), null, new Point(9, centre), 4.5, 4.5);
                var name = new FormattedText(item.Filter, culture, FlowDirection.LeftToRight, Body, 13, Fg) { MaxTextWidth = grid.X - 30, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
                context.DrawText(name, new Point(24, centre - 15));
                var night = new FormattedText($"{item.Night} · {item.Lights.Count} light", culture, FlowDirection.LeftToRight, Mono, 9.5, Muted) { MaxTextWidth = grid.X - 30, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
                context.DrawText(night, new Point(24, centre + 2));
            }

            for (var column = 0; column < item.Cells.Count && column < 3; column++)
            {
                var cell = item.Cells[column];
                var index = row * 3 + column;
                var appear = Math.Clamp((_reveal * (total + 8) - index) / 8, 0, 1);
                if (appear <= 0) continue;
                var eased = Motion.EaseOutBack(appear);
                var rect = CellRect(grid, columnWidth, row, column);
                var scaled = rect.CenterRect(new Rect(0, 0, rect.Width * (0.86 + 0.14 * eased), rect.Height * (0.86 + 0.14 * eased)));
                using (context.PushOpacity(appear))
                    DrawCell(context, scaled, cell, CellKey(item, cell), _hover == (row, column), index);
            }
        }

        if (ring.Width > 0) DrawRing(context, ring);
    }

    private void DrawCell(DrawingContext context, Rect rect, CalibrationCell cell, string key, bool hover, int index)
    {
        var colour = cell.State switch
        {
            CalibrationCellState.Exact => Oiii,
            CalibrationCellState.Tolerance => Accent,
            CalibrationCellState.Choose => Warn,
            _ => Ha
        };
        var breathe = cell.Pending && !Motion.Reduced ? 0.5 + 0.5 * Math.Sin(Clock * 2.6 + index * 0.7) : 0.5;
        byte Alpha(double value) => (byte)Math.Clamp(value * 255, 0, 255);

        if (cell.Pending)
        {
            // A breathing halo invites the click.
            var halo = rect.Inflate(2 + 3 * breathe);
            context.DrawRectangle(new ImmutableSolidColorBrush(Color.FromArgb(Alpha(0.10 + 0.12 * breathe), colour.R, colour.G, colour.B)), null, halo, 10, 10);
        }
        var fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(Alpha(cell.Pending ? 0.20 : 0.16), colour.R, colour.G, colour.B), 0),
                new GradientStop(Color.FromArgb(Alpha(cell.Pending ? 0.07 : 0.04), colour.R, colour.G, colour.B), 1)
            }
        };
        var border = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(Alpha(hover ? 0.95 : cell.Pending ? 0.45 + 0.35 * breathe : 0.38), colour.R, colour.G, colour.B)), hover ? 1.4 : 1);
        context.DrawRectangle(fill, border, rect, 8, 8);

        // Resolving a cell makes it flash and throw a ring of light.
        if (_ignitions.TryGetValue(key, out var start))
        {
            var t = (Clock - start) / 1.1;
            if (t >= 1 || Motion.Reduced) _ignitions.Remove(key);
            else
            {
                var flash = 1 - Motion.EaseOutExpo(t);
                context.DrawRectangle(new ImmutableSolidColorBrush(Color.FromArgb(Alpha(0.55 * flash), colour.R, colour.G, colour.B)), null, rect, 8, 8);
                var wave = rect.Inflate(4 + 18 * Motion.EaseOutExpo(t));
                context.DrawRectangle(null, new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(Alpha(0.6 * flash), colour.R, colour.G, colour.B)), 1.2), wave, 12, 12);
            }
        }

        var glyph = cell.State switch
        {
            CalibrationCellState.Exact => "✓",
            CalibrationCellState.Tolerance => "≈",
            CalibrationCellState.Choose => "?",
            _ => "✕"
        };
        var brush = new ImmutableSolidColorBrush(colour);
        var mark = new FormattedText(glyph, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, MonoBold, 12, brush);
        context.DrawText(mark, new Point(rect.X + 10, rect.Center.Y - mark.Height / 2));
        var text = new FormattedText(cell.Detail, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 10.5, cell.Pending ? brush : Fg)
        {
            MaxTextWidth = Math.Max(10, rect.Width - 34), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis
        };
        context.DrawText(text, new Point(rect.X + 26, rect.Center.Y - text.Height / 2));
    }

    private void DrawRing(DrawingContext context, Rect area)
    {
        var centre = new Point(area.Center.X, 68);
        const double radius = 52;
        context.DrawEllipse(null, Track, centre, radius, radius);
        var fraction = Math.Clamp(_shownFraction, 0, 1);
        var ready = Items.Count > 0 && Fraction >= 1;
        if (fraction > 0.001)
        {
            var sweep = Math.Min(fraction, 0.9999) * Math.Tau;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                var start = new Point(centre.X, centre.Y - radius);
                var end = new Point(centre.X + radius * Math.Sin(sweep), centre.Y - radius * Math.Cos(sweep));
                g.BeginFigure(start, false);
                g.ArcTo(end, new Size(radius, radius), 0, sweep > Math.PI, SweepDirection.Clockwise);
                g.EndFigure(false);
            }
            var gradient = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Accent, 0), new GradientStop(Oiii, 1) }
            };
            context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(50, Oiii.R, Oiii.G, Oiii.B)), 16, lineCap: PenLineCap.Round), geometry);
            context.DrawGeometry(null, new Pen(gradient, 8, lineCap: PenLineCap.Round), geometry);
        }

        var culture = CultureInfo.CurrentCulture;
        var percent = new FormattedText($"{Math.Round(fraction * 100):0}%", culture, FlowDirection.LeftToRight, Display, 24, Fg);
        context.DrawText(percent, new Point(centre.X - percent.Width / 2, centre.Y - percent.Height / 2 - 6));
        var caption = new FormattedText(CanvasText.T("calibrato"), culture, FlowDirection.LeftToRight, Mono, 9.5, Muted);
        context.DrawText(caption, new Point(centre.X - caption.Width / 2, centre.Y + 12));

        var pending = Items.Sum(row => row.Cells.Count(cell => cell.Pending));
        var status = Items.Count == 0 ? "" : ready ? CanvasText.T("Pronto per l’export") : string.Format(culture, CanvasText.T("{0} celle da risolvere"), pending);
        var statusText = new FormattedText(status, culture, FlowDirection.LeftToRight, MonoBold, 10, new ImmutableSolidColorBrush(ready ? Oiii : Warn)) { MaxTextWidth = area.Width, TextAlignment = TextAlignment.Center };
        context.DrawText(statusText, new Point(area.X, centre.Y + radius + 14));
    }
}
