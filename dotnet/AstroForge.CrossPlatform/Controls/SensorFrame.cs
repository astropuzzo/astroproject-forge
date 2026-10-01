using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using AstroForge.Core.Analysis;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>
/// The sensor's footprint on the real sky, to scale: the picture of the target behind, the frame laid where the data were taken and turned as the camera was,
/// the sky outside it dimmed, the other optical configuration dashed, a compass and a scale bar. Without a picture the frame sits on an empty sky,
/// and it eases to its new size when the optics change.
/// </summary>
public sealed class SensorFrame : Control
{
    public static readonly StyledProperty<double> WidthDegProperty = AvaloniaProperty.Register<SensorFrame, double>(nameof(WidthDeg));
    public static readonly StyledProperty<double> HeightDegProperty = AvaloniaProperty.Register<SensorFrame, double>(nameof(HeightDeg));
    public static readonly StyledProperty<double> AltWidthDegProperty = AvaloniaProperty.Register<SensorFrame, double>(nameof(AltWidthDeg));
    public static readonly StyledProperty<double> AltHeightDegProperty = AvaloniaProperty.Register<SensorFrame, double>(nameof(AltHeightDeg));
    public static readonly StyledProperty<string?> AltLabelProperty = AvaloniaProperty.Register<SensorFrame, string?>(nameof(AltLabel));
    public static readonly StyledProperty<bool> WarnProperty = AvaloniaProperty.Register<SensorFrame, bool>(nameof(Warn));
    public static readonly StyledProperty<SkyScene?> SceneProperty = AvaloniaProperty.Register<SensorFrame, SkyScene?>(nameof(Scene));
    public static readonly StyledProperty<string?> CaptionProperty = AvaloniaProperty.Register<SensorFrame, string?>(nameof(Caption));
    public static readonly StyledProperty<string?> NoteProperty = AvaloniaProperty.Register<SensorFrame, string?>(nameof(Note));

    private static readonly Typeface Mono = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#JetBrains Mono"));
    private static readonly IBrush Void = new ImmutableSolidColorBrush(Color.Parse("#04060D"));
    private static readonly IBrush Shade = new ImmutableSolidColorBrush(Color.Parse("#7304060D"));
    private static readonly IBrush Pill = new ImmutableSolidColorBrush(Color.Parse("#B004060D"));
    private static readonly IBrush Fg = new ImmutableSolidColorBrush(Color.Parse("#EEF2FF"));
    private static readonly IBrush Soft = new ImmutableSolidColorBrush(Color.Parse("#B3BECDFF"));
    private static readonly IPen Outline = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#EEF2FF")), 1.5);
    private static readonly IPen Other = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#99BECDFF")), 1, new ImmutableDashStyle([4, 5], 0));
    private static readonly IPen Ruler = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#E6EEF2FF")), 1.5);
    private static readonly SkyPanelView[] Alone = [new(0, 0, 0, "")];
    private const double MoonDegrees = 0.52;
    // the readout column (24 px margin, at most 290 px wide, mostly narrower) ends here
    private const double ReadoutRight = 290;
    private static readonly IBrush MoonFill = new ImmutableRadialGradientBrush([new ImmutableGradientStop(0, Color.Parse("#F0EDE4")), new ImmutableGradientStop(0.75, Color.Parse("#C4C0B5")), new ImmutableGradientStop(1, Color.Parse("#8F8B82"))],
        gradientOrigin: new RelativePoint(0.38, 0.34, RelativeUnit.Relative), center: new RelativePoint(0.5, 0.5, RelativeUnit.Relative), radius: 0.62);
    private static readonly IBrush MoonSea = new ImmutableSolidColorBrush(Color.Parse("#2A5A6070"));
    private static readonly IPen MoonEdge = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#66EEF2FF")), 1);
    private static readonly IPen MoonRing = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#E6F0EDE4")), 1.5, new ImmutableDashStyle([2, 3], 0));
    private static readonly IPen TargetPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#E6FFC27A")), 1.5);
    private static readonly (double Dx, double Dy, double Rx, double Ry)[] Maria = [(-0.3, -0.25, 0.24, 0.18), (0.18, -0.34, 0.16, 0.12), (0.1, 0.18, 0.26, 0.16), (-0.38, 0.22, 0.12, 0.1), (0.42, 0.0, 0.1, 0.16), (-0.05, -0.5, 0.1, 0.07)];
    private static readonly (double X, double Y, double Size, IBrush Brush)[] Stars = EmptySkyStars();

    private double _w, _h, _aw, _ah;
    private double _imageAlpha = 1;

    static SensorFrame() => AffectsRender<SensorFrame>(AltLabelProperty, WarnProperty, CaptionProperty, NoteProperty);

    public SensorFrame()
    {
        IsHitTestVisible = false;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    public double WidthDeg { get => GetValue(WidthDegProperty); set => SetValue(WidthDegProperty, value); }
    public double HeightDeg { get => GetValue(HeightDegProperty); set => SetValue(HeightDegProperty, value); }
    public double AltWidthDeg { get => GetValue(AltWidthDegProperty); set => SetValue(AltWidthDegProperty, value); }
    public double AltHeightDeg { get => GetValue(AltHeightDegProperty); set => SetValue(AltHeightDegProperty, value); }
    public string? AltLabel { get => GetValue(AltLabelProperty); set => SetValue(AltLabelProperty, value); }
    public bool Warn { get => GetValue(WarnProperty); set => SetValue(WarnProperty, value); }
    /// <summary>The sky behind the frame: the picture and where each panel sits on it. Null draws one centred frame on an empty sky.</summary>
    public SkyScene? Scene { get => GetValue(SceneProperty); set => SetValue(SceneProperty, value); }
    /// <summary>The target and where it is, top right.</summary>
    public string? Caption { get => GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    /// <summary>Where the picture comes from, or why there is none, bottom left.</summary>
    public string? Note { get => GetValue(NoteProperty); set => SetValue(NoteProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SceneProperty)
        {
            // A new picture eases in over the empty sky; the same picture with other panels stays as it is.
            var previous = (change.OldValue as SkyScene)?.Image;
            if (Scene?.Image is null) { _imageAlpha = 1; InvalidateVisual(); }
            else if (!ReferenceEquals(previous, Scene.Image))
            {
                _imageAlpha = 0;
                _ = Motion.Tween(TopLevel.GetTopLevel(this), TimeSpan.FromMilliseconds(700), t => 1 - Math.Pow(1 - t, 2), t => { _imageAlpha = t; InvalidateVisual(); });
            }
            else InvalidateVisual();
            return;
        }
        if (change.Property != WidthDegProperty && change.Property != HeightDegProperty && change.Property != AltWidthDegProperty && change.Property != AltHeightDegProperty) return;
        double w = _w, h = _h, aw = _aw, ah = _ah;
        if (w <= 0) { _w = WidthDeg; _h = HeightDeg; _aw = AltWidthDeg; _ah = AltHeightDeg; InvalidateVisual(); return; }
        _ = Motion.Tween(TopLevel.GetTopLevel(this), TimeSpan.FromMilliseconds(900), t => 1 - Math.Pow(1 - t, 3), t =>
        {
            _w = w + (WidthDeg - w) * t; _h = h + (HeightDeg - h) * t;
            _aw = aw + (AltWidthDeg - aw) * t; _ah = ah + (AltHeightDeg - ah) * t;
            InvalidateVisual();
        });
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var scene = Scene;
        var panels = scene?.Panels is { Count: > 0 } list ? list : Alone;

        // The larger of the two frames, turned any way, over every panel, fills the free area right of the readout and above the dock.
        var area = new Rect(bounds.Width * 0.5, 30, Math.Max(40, bounds.Width * 0.5 - 30), Math.Max(40, bounds.Height - 30 - 118)).Deflate(14);
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        var widest = Math.Max(0.05, Math.Max(_w, _aw));
        var tallest = Math.Max(0.05, Math.Max(_h, _ah));
        foreach (var panel in panels)
            foreach (var (xi, eta) in SkyPointings.Corners(panel.Xi, panel.Eta, widest, tallest, panel.AngleDeg))
            { minX = Math.Min(minX, xi); maxX = Math.Max(maxX, xi); minY = Math.Min(minY, eta); maxY = Math.Max(maxY, eta); }
        var pxPerDeg = Math.Min(area.Width / Math.Max(0.05, maxX - minX), area.Height / Math.Max(0.05, maxY - minY));
        var (viewX, viewY) = ((minX + maxX) / 2, (minY + maxY) / 2);
        // East is left, north is up: the picture and everything on it share this one mapping.
        Point At(double xi, double eta) => new(area.Center.X - (xi - viewX) * pxPerDeg, area.Center.Y - (eta - viewY) * pxPerDeg);

        context.FillRectangle(Void, bounds);
        DrawEmptySky(context, bounds);
        if (scene?.Image is { } image && _imageAlpha > 0)
        {
            var side = scene.FovDeg * pxPerDeg;
            var origin = At(0, 0);
            var target = new Rect(origin.X - side / 2, origin.Y - side / 2, side, side);
            using (context.PushClip(bounds))
            {
                using (context.PushOpacity(_imageAlpha)) context.DrawImage(image, new Rect(image.Size), target);
                // DSS plates are dark: the same picture added over itself lifts the nebula without clipping the stars.
                using (context.PushRenderOptions(new RenderOptions { BitmapBlendingMode = BitmapBlendingMode.Plus, BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
                using (context.PushOpacity(_imageAlpha * 0.55)) context.DrawImage(image, new Rect(image.Size), target);
            }
        }

        if (_w > 0 && _h > 0)
        {
            var frame = DrawFrames(context, bounds, panels, At, pxPerDeg);
            if (scene?.Target is { } mark) DrawTarget(context, mark, At(mark.Xi, mark.Eta), panels);
            DrawMoon(context, bounds, frame, pxPerDeg);
        }
        var note = string.IsNullOrWhiteSpace(Note) ? null : new FormattedText(Note, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 10.5, Soft)
        { MaxTextWidth = Math.Max(120, Math.Min(300, bounds.Width * 0.5 - 56)), MaxLineCount = 3, Trimming = TextTrimming.CharacterEllipsis };
        var floor = bounds.Height - 118 - 14;
        // The compass and the ruler sit between the readout and the note; a short card has no room for them.
        var rulerBaseline = note is null ? floor : floor - note.Height - 12;
        if (rulerBaseline - 46 >= 196) DrawCompassAndRuler(context, rulerBaseline, pxPerDeg);
        DrawTexts(context, bounds, note, floor);
    }

    /// <summary>Draws the footprints and returns the screen rectangle that holds them.</summary>
    private Rect DrawFrames(DrawingContext context, Rect bounds, IReadOnlyList<SkyPanelView> panels, Func<double, double, Point> at, double pxPerDeg)
    {
        Point[] Footprint(SkyPanelView panel, double width, double height) =>
            SkyPointings.Corners(panel.Xi, panel.Eta, width, height, panel.AngleDeg).Select(corner => at(corner.Xi, corner.Eta)).ToArray();

        var footprints = panels.Select(panel => Footprint(panel, _w, _h)).ToList();
        var holes = new GeometryGroup { FillRule = FillRule.NonZero };
        foreach (var footprint in footprints) holes.Children.Add(Polygon(footprint));
        context.DrawGeometry(Shade, null, new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(bounds), holes));

        if (_aw > 0 && _ah > 0 && Math.Abs(_aw - _w) > 0.001)
            for (var index = 0; index < panels.Count; index++)
            {
                var other = Footprint(panels[index], _aw, _ah);
                context.DrawGeometry(null, Other, Polygon(other));
                if (index == 0 && !string.IsNullOrEmpty(AltLabel))
                {
                    var anchor = other.OrderBy(point => point.Y).ThenBy(point => point.X).First();
                    DrawText(context, AltLabel, Soft, new Point(anchor.X + 8, anchor.Y + 6), 11);
                }
            }

        var corner = new Pen(new SolidColorBrush(Color.Parse(Warn ? "#FFC27A" : "#9DB8FF")), 3);
        const double arm = 14;
        for (var index = 0; index < footprints.Count; index++)
        {
            var points = footprints[index];
            context.DrawGeometry(null, Outline, Polygon(points));
            for (var i = 0; i < 4; i++)
            {
                var (here, next, previous) = (points[i], points[(i + 1) % 4], points[(i + 3) % 4]);
                context.DrawLine(corner, here, Toward(here, next, arm));
                context.DrawLine(corner, here, Toward(here, previous, arm));
            }
            if (!string.IsNullOrEmpty(panels[index].Label))
            {
                var anchor = points.OrderBy(point => point.Y).ThenBy(point => point.X).First();
                DrawText(context, panels[index].Label, Fg, new Point(anchor.X + 8, anchor.Y + 6), 11, pill: true);
            }
        }

        // The size, at the lowest corner of the first panel (the bottom-left one while the camera is upright).
        var lowest = footprints[0].OrderByDescending(point => Math.Round(point.Y)).ThenBy(point => point.X).First();
        var size = $"{_w.ToString("0.00", CultureInfo.CurrentCulture)}° × {_h.ToString("0.00", CultureInfo.CurrentCulture)}°";
        DrawText(context, size, Fg, new Point(lowest.X + 8, lowest.Y - 22), 11);

        var all = footprints.SelectMany(points => points).ToList();
        return new Rect(new Point(all.Min(point => point.X), all.Min(point => point.Y)), new Point(all.Max(point => point.X), all.Max(point => point.Y)));
    }

    /// <summary>
    /// The Moon, 0.52° across, at the scale of the picture: how much sky the sensor takes in, in the one size everybody knows. It sits between the readout and the frame
    /// when there is room; when the field is narrower than that (a long focal length) it is a ring over the frame, so the frame can be read against it.
    /// </summary>
    private static void DrawMoon(DrawingContext context, Rect bounds, Rect frame, double pxPerDeg)
    {
        var diameter = MoonDegrees * pxPerDeg;
        if (diameter < 6) return;
        var radius = diameter / 2;
        var room = frame.Left - 10 - ReadoutRight;
        var label = CanvasText.T("Luna · 0,5°");
        if (diameter <= room && diameter <= bounds.Height - 30 - 118 - 30)
        {
            var centre = new Point(frame.Left - 10 - radius, Math.Min(frame.Bottom - radius - 6, bounds.Height - 118 - 28 - radius));
            context.DrawEllipse(MoonFill, MoonEdge, centre, radius, radius);
            foreach (var (dx, dy, rx, ry) in Maria)
                context.DrawEllipse(MoonSea, null, new Point(centre.X + dx * radius, centre.Y + dy * radius), rx * radius, ry * radius);
            DrawText(context, label, Soft, new Point(centre.X - TextWidth(label, 10) / 2, centre.Y + radius + 6), 10);
        }
        else
        {
            var centre = frame.Center;
            context.DrawEllipse(null, MoonRing, centre, radius, radius);
            DrawText(context, label, Soft, new Point(centre.X - TextWidth(label, 10) / 2, Math.Max(32, centre.Y - radius - 18)), 10, pill: true);
        }
    }

    /// <summary>Where the capture software was told to centre, when the frames are not on it.</summary>
    private void DrawTarget(DrawingContext context, SkyMark mark, Point at, IReadOnlyList<SkyPanelView> panels)
    {
        // on a frame's centre it would only sit on top of the object: draw it when it says something
        var nearest = panels.Min(panel => Math.Sqrt(Math.Pow(panel.Xi - mark.Xi, 2) + Math.Pow(panel.Eta - mark.Eta, 2)));
        if (nearest < 0.04 * Math.Min(_w, _h)) return;
        context.DrawEllipse(null, TargetPen, at, 7, 7);
        context.DrawLine(TargetPen, new Point(at.X - 12, at.Y), new Point(at.X + 12, at.Y));
        context.DrawLine(TargetPen, new Point(at.X, at.Y - 12), new Point(at.X, at.Y + 12));
    }

    /// <summary>A compass (the picture is north up, east left) and a scale bar, bottom left above the dock.</summary>
    private static void DrawCompassAndRuler(DrawingContext context, double baseline, double pxPerDeg)
    {
        var centre = new Point(38, baseline - 22);
        var arm = 11.0;
        context.DrawEllipse(Pill, null, centre, arm + 11, arm + 11);
        context.DrawLine(Ruler, centre, new Point(centre.X, centre.Y - arm));
        context.DrawLine(Ruler, centre, new Point(centre.X - arm, centre.Y));
        DrawText(context, "N", Fg, new Point(centre.X - 3, centre.Y - arm - 14), 10);
        DrawText(context, "E", Fg, new Point(centre.X - arm - 13, centre.Y - 6), 10);

        var steps = new[] { 0.02, 0.05, 0.1, 0.25, 0.5, 1, 2, 5, 10, 20, 40 };
        var step = steps.LastOrDefault(candidate => candidate * pxPerDeg <= 120, steps[0]);
        var length = step * pxPerDeg;
        if (length < 18) return;
        var left = 80.0;
        var y = centre.Y + 8;
        var label = step < 1 ? $"{(step * 60).ToString("0", CultureInfo.InvariantCulture)}′" : $"{step.ToString("0", CultureInfo.InvariantCulture)}°";
        context.DrawRectangle(Pill, null, new Rect(left - 8, y - 22, length + 16, 34), 6, 6);
        context.DrawLine(Ruler, new Point(left, y), new Point(left + length, y));
        context.DrawLine(Ruler, new Point(left, y - 4), new Point(left, y + 4));
        context.DrawLine(Ruler, new Point(left + length, y - 4), new Point(left + length, y + 4));
        DrawText(context, label, Fg, new Point(left, y - 19), 10);
    }

    private void DrawTexts(DrawingContext context, Rect bounds, FormattedText? note, double floor)
    {
        if (!string.IsNullOrWhiteSpace(Caption))
        {
            var caption = new FormattedText(Caption, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 11, Fg)
            { MaxTextWidth = Math.Max(60, bounds.Width * 0.5 - 40), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            var origin = new Point(bounds.Width - 24 - caption.Width, 8);
            context.DrawRectangle(Pill, null, new Rect(origin.X - 8, origin.Y - 3, caption.Width + 16, caption.Height + 6), 6, 6);
            context.DrawText(caption, origin);
        }

        if (note is not null)
        {
            var y = floor - note.Height;
            context.DrawRectangle(Pill, null, new Rect(16, y - 4, note.Width + 16, note.Height + 8), 6, 6);
            context.DrawText(note, new Point(24, y));
        }
    }

    private static double TextWidth(string text, double size) =>
        new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, size, Soft).Width;

    private static void DrawText(DrawingContext context, string text, IBrush brush, Point origin, double size, bool pill = false)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, size, brush);
        if (pill) context.DrawRectangle(Pill, null, new Rect(origin.X - 4, origin.Y - 2, formatted.Width + 8, formatted.Height + 4), 4, 4);
        context.DrawText(formatted, origin);
    }

    /// <summary>A quiet sky for when there is no picture: stars, no nebula, so nothing here can be mistaken for the target.</summary>
    private static void DrawEmptySky(DrawingContext context, Rect bounds)
    {
        foreach (var (x, y, size, brush) in Stars)
            context.DrawEllipse(brush, null, new Point(x * bounds.Width, y * bounds.Height), size, size);
    }

    private static (double, double, double, IBrush)[] EmptySkyStars()
    {
        var random = new Random(20260615);
        return Enumerable.Range(0, 150).Select(_ => (random.NextDouble(), random.NextDouble(), 0.4 + Math.Pow(random.NextDouble(), 3) * 1.3, (IBrush)new ImmutableSolidColorBrush(Color.FromArgb((byte)(50 + random.Next(150)), 190, 205, 255)))).ToArray();
    }

    private static Point Toward(Point from, Point to, double distance)
    {
        var (dx, dy) = (to.X - from.X, to.Y - from.Y);
        var length = Math.Sqrt(dx * dx + dy * dy);
        return length < 1e-6 ? from : new Point(from.X + dx / length * distance, from.Y + dy / length * distance);
    }

    private static StreamGeometry Polygon(Point[] points)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(points[0], true);
        for (var i = 1; i < points.Length; i++) context.LineTo(points[i]);
        context.EndFigure(true);
        return geometry;
    }
}
