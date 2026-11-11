using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AstroForge.App.Services;
using AstroForge.Core.Analysis;
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

    /// <summary>The filter as the rest of the interface names it ("Hα"), when it differs from the name in the headers.</summary>
    public string Label { get; init; } = "";

    public static IReadOnlyList<CalibrationRow> Build(ProjectAnalysis? analysis, Func<FrameMetadata, Color> colourOf, Func<FrameMetadata, string>? nameOf = null)
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
                ])
                { Label = nameOf?.Invoke(items[0].Light) ?? group.Key.Filter };
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
                missing ? CanvasText.T("Mancante") : string.Format(UiFormat.Culture, CanvasText.T("{0} da scegliere"), Math.Max(2, candidates)));
        }
        var tolerance = results.Any(result => result.Status == MatchStatus.WithinTolerance);
        var selected = results.Select(result => result.Selected?.Frame).OfType<FrameMetadata>().Distinct().ToList();
        var detail = calibration switch
        {
            "Flat" => items.Select(item => item.FlatGroup).OfType<CalibrationGroup>().Distinct().Sum(group => group.Frames.Count) is var flats and > 0
                ? UiFormat.Frames(flats, "Flat") : UiFormat.Frames(selected.Count, "Flat"),
            "Dark" => selected.Select(frame => frame.ExposureSeconds.Value).OfType<double>().Distinct().Order()
                .Select(seconds => UiFormat.Number(seconds, "0.#") + " s").DefaultIfEmpty("Master").Aggregate((a, b) => $"{a} · {b}"),
            _ => selected.Any(frame => frame.IsMaster) ? "Master" : $"{selected.Count} Bias"
        };
        return new(calibration, tolerance ? CalibrationCellState.Tolerance : CalibrationCellState.Exact, detail);
    }
}

/// <summary>
/// Every Light group against its Flat, Dark and Bias. A cell says how it was matched with a mark and a colour:
/// identical, within tolerance, a choice to make, or missing. Cells that need the user can be clicked or reached
/// with the arrow keys; resolving one makes it flash once.
/// </summary>
public sealed class CalibrationMap : AmbientControl
{
    public static readonly StyledProperty<IReadOnlyList<CalibrationRow>?> RowsProperty =
        AvaloniaProperty.Register<CalibrationMap, IReadOnlyList<CalibrationRow>?>(nameof(Rows));

    public event EventHandler<(CalibrationRow Row, CalibrationCell Cell)>? CellActivated;

    private static readonly Typeface Mono = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#JetBrains Mono"));
    private static readonly Typeface Body = new(new FontFamily("Inter"));
    private static readonly Typeface BodyBold = new(new FontFamily("Inter"), weight: FontWeight.SemiBold);
    private static readonly Color Oiii = Color.Parse("#3FE0D0"), Warn = Color.Parse("#FFC27A"), Ha = Color.Parse("#FF5D73"), Accent = Color.Parse("#9DB8FF");
    private static readonly IBrush Fg = new ImmutableSolidColorBrush(Color.Parse("#EEF1FF"));
    private static readonly IBrush Muted = new ImmutableSolidColorBrush(Color.Parse("#A3ABCD"));
    private static readonly IPen RowRule = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#1296AAFF")), 1);
    private static readonly IPen FocusRing = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#EEF1FF")), 2);

    private const double HeaderHeight = 26, RowPitch = 40, CellHeight = 28, LabelWidth = 200;

    private readonly Dictionary<string, CalibrationCellState> _previous = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _ignitions = new(StringComparer.Ordinal);
    private double _reveal = 1;
    private (int Row, int Column)? _hover;
    private (int Row, int Column) _cursor;
    private bool _showCursor;

    static CalibrationMap()
    {
        AffectsMeasure<CalibrationMap>(RowsProperty);
        FocusableProperty.OverrideDefaultValue<CalibrationMap>(true);
    }

    public IReadOnlyList<CalibrationRow>? Rows { get => GetValue(RowsProperty); set => SetValue(RowsProperty, value); }

    private IReadOnlyList<CalibrationRow> Items => Rows ?? [];
    protected override bool WantsAmbient => _ignitions.Count > 0;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != RowsProperty) return;

        // Cells that were waiting and are now matched flash once; a whole new map fades in instead.
        var fresh = _previous.Count == 0 || !Items.Any(row => row.Cells.Any(cell => _previous.ContainsKey(CellKey(row, cell))));
        foreach (var row in Items)
            foreach (var cell in row.Cells)
                if (_previous.TryGetValue(CellKey(row, cell), out var before) && before is CalibrationCellState.Choose or CalibrationCellState.Missing && !cell.Pending)
                    _ignitions[CellKey(row, cell)] = Clock;
        _previous.Clear();
        foreach (var row in Items)
            foreach (var cell in row.Cells)
                _previous[CellKey(row, cell)] = cell.State;

        _cursor = (Math.Clamp(_cursor.Row, 0, Math.Max(0, Items.Count - 1)), _cursor.Column);
        if (fresh)
        {
            _reveal = 0;
            _ = Motion.Tween(TopLevel.GetTopLevel(this), TimeSpan.FromMilliseconds(500), t => t, t => { _reveal = t; InvalidateVisual(); });
        }
        InvalidateVisual();
    }

    private static string CellKey(CalibrationRow row, CalibrationCell cell) => $"{row.Key}|{cell.Calibration}";

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 640 : availableSize.Width, HeaderHeight + Math.Max(1, Items.Count) * RowPitch);

    private (Rect Grid, double ColumnWidth) Layout()
    {
        var labelWidth = Math.Min(LabelWidth, Bounds.Width * 0.36);
        var columnWidth = Math.Max(60, (Bounds.Width - labelWidth) / 3);
        return (new Rect(labelWidth, 0, columnWidth * 3, Bounds.Height), columnWidth);
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
        _showCursor = false;
        if (HitTestCell(e.GetPosition(this)) is not { } hit) return;
        _cursor = hit;
        Activate(hit);
        e.Handled = true;
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        _showCursor = true;
        InvalidateVisual();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        _showCursor = false;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Items.Count == 0) return;
        var (row, column) = _cursor;
        switch (e.Key)
        {
            case Key.Left: column = Math.Max(0, column - 1); break;
            case Key.Right: column = Math.Min(2, column + 1); break;
            case Key.Up: row = Math.Max(0, row - 1); break;
            case Key.Down: row = Math.Min(Items.Count - 1, row + 1); break;
            case Key.Enter or Key.Space: Activate(_cursor); e.Handled = true; return;
            default: return;
        }
        _cursor = (row, column);
        _showCursor = true;
        e.Handled = true;
        InvalidateVisual();
    }

    private void Activate((int Row, int Column) cell)
    {
        if (cell.Row >= Items.Count || cell.Column >= Items[cell.Row].Cells.Count) return;
        var row = Items[cell.Row];
        CellActivated?.Invoke(this, (row, row.Cells[cell.Column]));
    }

    private (int Row, int Column)? HitTestCell(Point point)
    {
        var (grid, columnWidth) = Layout();
        for (var row = 0; row < Items.Count; row++)
            for (var column = 0; column < 3; column++)
                if (CellRect(grid, columnWidth, row, column).Inflate(3).Contains(point)) return (row, column);
        return null;
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0) return;
        var (grid, columnWidth) = Layout();
        var culture = UiFormat.Culture;

        string[] headers = ["Flat", "Dark", "Bias"];
        for (var column = 0; column < 3; column++)
        {
            var header = new FormattedText(headers[column], culture, FlowDirection.LeftToRight, Body, 12, Muted);
            context.DrawText(header, new Point(grid.X + column * columnWidth + 10, 4));
        }

        if (Items.Count == 0)
        {
            var empty = new FormattedText(CanvasText.T("Nessuna calibrazione. Analizza il progetto."), culture, FlowDirection.LeftToRight, Body, 12, Muted);
            context.DrawText(empty, new Point(0, HeaderHeight + 10));
        }

        using var fade = context.PushOpacity(Math.Clamp(_reveal, 0, 1));
        for (var row = 0; row < Items.Count; row++)
        {
            var item = Items[row];
            var top = HeaderHeight + row * RowPitch;
            if (row > 0) context.DrawLine(RowRule, new Point(0, top), new Point(grid.Right, top));
            var centre = top + RowPitch / 2;
            context.DrawEllipse(new ImmutableSolidColorBrush(item.Colour), null, new Point(9, centre), 4.5, 4.5);
            var name = new FormattedText(item.Label.Length == 0 ? item.Filter : item.Label, culture, FlowDirection.LeftToRight, BodyBold, 13, Fg) { MaxTextWidth = Math.Max(10, grid.X - 30), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            context.DrawText(name, new Point(24, centre - 16));
            var night = new FormattedText($"{NightLabel(item.Night)} · {UiFormat.Frames(item.Lights.Count, "Light")}", culture, FlowDirection.LeftToRight, Body, 11, Muted) { MaxTextWidth = Math.Max(10, grid.X - 30), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            context.DrawText(night, new Point(24, centre + 1));

            for (var column = 0; column < item.Cells.Count && column < 3; column++)
            {
                var cell = item.Cells[column];
                var rect = CellRect(grid, columnWidth, row, column);
                DrawCell(context, rect, cell, CellKey(item, cell), _hover == (row, column));
                if (_showCursor && IsFocused && _cursor == (row, column)) context.DrawRectangle(null, FocusRing, rect.Inflate(3), 9, 9);
            }
        }
    }

    private static string NightLabel(string night) =>
        DateTime.TryParseExact(night, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date.ToString("d MMM", UiFormat.Culture) : night;

    private void DrawCell(DrawingContext context, Rect rect, CalibrationCell cell, string key, bool hover)
    {
        var colour = cell.State switch
        {
            CalibrationCellState.Exact => Oiii,
            CalibrationCellState.Tolerance => Accent,
            CalibrationCellState.Choose => Warn,
            _ => Ha
        };
        static byte Alpha(double value) => (byte)Math.Clamp(value * 255, 0, 255);

        var fill = new ImmutableSolidColorBrush(Color.FromArgb(Alpha(cell.Pending ? 0.16 : 0.09), colour.R, colour.G, colour.B));
        var border = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(Alpha(hover && cell.Pending ? 1 : cell.Pending ? 0.8 : 0.4), colour.R, colour.G, colour.B)), cell.Pending ? 1.4 : 1);
        context.DrawRectangle(fill, border, rect, 6, 6);

        if (_ignitions.TryGetValue(key, out var start))
        {
            var t = (Clock - start) / 0.8;
            if (t >= 1 || Motion.Reduced) _ignitions.Remove(key);
            else context.DrawRectangle(new ImmutableSolidColorBrush(Color.FromArgb(Alpha(0.45 * (1 - Motion.EaseOutExpo(t))), colour.R, colour.G, colour.B)), null, rect, 6, 6);
        }

        var glyph = cell.State switch
        {
            CalibrationCellState.Exact => "✓",
            CalibrationCellState.Tolerance => "≈",
            CalibrationCellState.Choose => "?",
            _ => "✕"
        };
        var brush = new ImmutableSolidColorBrush(colour);
        var mark = new FormattedText(glyph, UiFormat.Culture, FlowDirection.LeftToRight, Mono, 12, brush);
        context.DrawText(mark, new Point(rect.X + 10, rect.Center.Y - mark.Height / 2));
        var text = new FormattedText(cell.Detail, UiFormat.Culture, FlowDirection.LeftToRight, Body, 12, cell.Pending ? brush : Fg)
        {
            MaxTextWidth = Math.Max(10, rect.Width - 34), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis
        };
        context.DrawText(text, new Point(rect.X + 26, rect.Center.Y - text.Height / 2));
    }
}
