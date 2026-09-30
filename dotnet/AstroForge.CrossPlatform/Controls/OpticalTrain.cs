using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>One element of the optical train and where Forge learnt about it.</summary>
public sealed record TrainPart(string Title, string Detail, string Source, bool Confirmed);

/// <summary>
/// Telescope, reducer, filter wheel and camera side by side, with starlight entering the objective, converging
/// through the train and taking on the colour of the filter in the beam before it reaches the sensor.
/// </summary>
public sealed class OpticalTrain : AmbientControl
{
    public static readonly StyledProperty<TrainPart?> TelescopeProperty = AvaloniaProperty.Register<OpticalTrain, TrainPart?>(nameof(Telescope));
    public static readonly StyledProperty<TrainPart?> ReducerProperty = AvaloniaProperty.Register<OpticalTrain, TrainPart?>(nameof(Reducer));
    public static readonly StyledProperty<TrainPart?> WheelProperty = AvaloniaProperty.Register<OpticalTrain, TrainPart?>(nameof(Wheel));
    public static readonly StyledProperty<TrainPart?> CameraProperty = AvaloniaProperty.Register<OpticalTrain, TrainPart?>(nameof(Camera));
    public static readonly StyledProperty<Color> BeamProperty = AvaloniaProperty.Register<OpticalTrain, Color>(nameof(Beam), Color.Parse("#EEF2FF"));

    private static readonly Typeface Body = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface Mono = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#JetBrains Mono"));
    private static readonly IBrush Fg = new ImmutableSolidColorBrush(Color.Parse("#EEF1FF"));
    private static readonly IBrush Muted = new ImmutableSolidColorBrush(Color.Parse("#8E97BD"));
    private static readonly IBrush Ok = new ImmutableSolidColorBrush(Color.Parse("#7CF2C9"));
    private static readonly IBrush Warn = new ImmutableSolidColorBrush(Color.Parse("#FFC27A"));
    private static readonly IPen MetalEdge = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#4DBECDFF")), 1);
    private static readonly IBrush Metal = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#3A4777"), 0), new GradientStop(Color.Parse("#1A2146"), 0.45), new GradientStop(Color.Parse("#0C1026"), 1) }
    }.ToImmutable();
    private static readonly IBrush Grille = new ImmutableSolidColorBrush(Color.Parse("#2EBECDFF"));

    static OpticalTrain() => AffectsRender<OpticalTrain>(TelescopeProperty, ReducerProperty, WheelProperty, CameraProperty, BeamProperty);

    public TrainPart? Telescope { get => GetValue(TelescopeProperty); set => SetValue(TelescopeProperty, value); }
    public TrainPart? Reducer { get => GetValue(ReducerProperty); set => SetValue(ReducerProperty, value); }
    public TrainPart? Wheel { get => GetValue(WheelProperty); set => SetValue(WheelProperty, value); }
    public TrainPart? Camera { get => GetValue(CameraProperty); set => SetValue(CameraProperty, value); }
    public Color Beam { get => GetValue(BeamProperty); set => SetValue(BeamProperty, value); }

    protected override bool WantsAmbient => Camera is not null;

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 620 : availableSize.Width, 170);

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || Camera is null) return;
        // Layout in the mockup's 620 × 170 units, stretched horizontally.
        var sx = bounds.Width / 620;
        double X(double x) => x * sx;
        var hasReducer = Reducer is not null;
        var tubeEnd = hasReducer ? 260 : 282;
        var wheelX = hasReducer ? 312 : 292;
        var cameraX = hasReducer ? 462 : 488;
        var cameraW = hasReducer ? 126 : 112;
        var beam = Beam;

        var beamGeometry = new StreamGeometry();
        using (var g = beamGeometry.Open())
        {
            g.BeginFigure(new Point(X(14), 30), true);
            g.LineTo(new Point(X(cameraX), 64)); g.LineTo(new Point(X(cameraX), 70)); g.LineTo(new Point(X(14), 104));
            g.EndFigure(true);
        }
        var filterAt = (wheelX + 14) / (double)cameraX;
        var beamBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(60, 238, 242, 255), 0), new GradientStop(Color.FromArgb(38, 238, 242, 255), filterAt - 0.02),
                new GradientStop(Color.FromArgb(150, beam.R, beam.G, beam.B), filterAt + 0.02), new GradientStop(Color.FromArgb(210, beam.R, beam.G, beam.B), 1)
            }
        };
        context.DrawGeometry(beamBrush, null, beamGeometry);

        // Telescope tube with its objective.
        context.DrawRectangle(Metal, MetalEdge, new Rect(X(8), 24, X(tubeEnd) - X(8), 86), 10, 10);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(20, 157, 184, 255)), MetalEdge, new Rect(X(8), 24, X(46), 86), 10, 10);
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 170, 200, 255)), new Pen(new SolidColorBrush(Color.FromArgb(150, 220, 230, 255)), 1), new Point(X(12), 67), 5, 40);
        if (hasReducer) context.DrawRectangle(Metal, MetalEdge, new Rect(X(262), 40, X(44), 54), 6, 6);
        context.DrawRectangle(Metal, MetalEdge, new Rect(X(wheelX), 16, X(28), 102), 7, 7);
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(230, beam.R, beam.G, beam.B)), null, new Point(X(wheelX + 14), 67), 9, 9);
        context.DrawRectangle(Metal, MetalEdge, new Rect(X(cameraX), 26, X(cameraW), 82), 12, 12);
        for (var k = 0; k < 6; k++) context.DrawRectangle(Grille, null, new Rect(X(cameraX + cameraW - 30 + k * 5), 34, 2, 66));
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(230, beam.R, beam.G, beam.B)), null, new Rect(X(cameraX), 52, 6, 30));

        if (!Motion.Reduced)
            for (var k = 0; k < 14; k++)
            {
                var u = (Clock * 0.55 + k / 14.0) % 1;
                var x = 14 + u * (cameraX - 14);
                var lane = (k * 37 % 13) / 12.0 - 0.5;
                var y = 67 + lane * (74 * (1 - u) + 6 * u);
                var past = x > wheelX + 14;
                var colour = past ? beam : Color.Parse("#EEF2FF");
                var alpha = (byte)(255 * (0.4 + 0.6 * Math.Sin(u * Math.PI)));
                context.DrawEllipse(new SolidColorBrush(Color.FromArgb(alpha, colour.R, colour.G, colour.B)), null, new Point(X(x), y), past ? 1.8 : 1.3, past ? 1.8 : 1.3);
            }

        DrawLabel(context, Telescope, X(8), 124, X(hasReducer ? 236 : 270));
        if (Reducer is { } reducer) DrawLabel(context, reducer, X(252), 124, X(200));
        if (Wheel is { } wheel)
        {
            var title = new FormattedText(wheel.Title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Body, 10.5, Fg);
            context.DrawText(title, new Point(X(wheelX + 36), 20));
            var detail = new FormattedText(wheel.Detail, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 9, wheel.Confirmed ? Muted : Warn) { MaxTextWidth = X(cameraX - wheelX - 40), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            context.DrawText(detail, new Point(X(wheelX + 36), 20 + title.Height));
        }
        DrawLabel(context, Camera, X(hasReducer ? 462 : 470), 124, bounds.Width - X(hasReducer ? 462 : 470));
    }

    private static void DrawLabel(DrawingContext context, TrainPart? part, double x, double y, double width)
    {
        if (part is null) return;
        var title = new FormattedText(part.Title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Body, 11.5, Fg) { MaxTextWidth = width, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        var detail = new FormattedText(part.Detail, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 9, Muted) { MaxTextWidth = width, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        var source = new FormattedText(part.Source, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 9, part.Confirmed ? Ok : Warn) { MaxTextWidth = width, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        context.DrawText(title, new Point(x, y));
        context.DrawText(detail, new Point(x, y + title.Height + 1));
        context.DrawText(source, new Point(x, y + title.Height + detail.Height + 3));
    }
}
