using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AstroForge.Core.Filters;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>
/// The pass bands of a narrow or multi-band filter, each zoomed on its own: a few nanometres around its centre, so that
/// 3.2 nm and 3.7 nm do not both collapse into a hairline on the whole visible spectrum. Under each one: where it sits,
/// how wide it is, how much light it lets through, and the emission line it is there for.
/// </summary>
public sealed class PassbandView : Control
{
    public static readonly StyledProperty<IReadOnlyList<FilterBand>?> BandsProperty =
        AvaloniaProperty.Register<PassbandView, IReadOnlyList<FilterBand>?>(nameof(Bands));

    private const double PlotHeight = 44, TextHeight = 14, MaxPanelWidth = 230, Gap = 12;
    private static readonly IBrush Frame = new ImmutableSolidColorBrush(Color.Parse("#0A0D1C"));
    private static readonly IPen FrameLine = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#405B6490")), 1);
    private static readonly IPen GridLine = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#285B6490")), 1);
    private static readonly IPen LinePen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#99C9D3F5")), 1, new ImmutableDashStyle([2, 2], 0));
    private static readonly IBrush Bright = new ImmutableSolidColorBrush(Color.Parse("#E6EEF1FF"));
    private static readonly IBrush Muted = new ImmutableSolidColorBrush(Color.Parse("#8E97BD"));
    private static readonly Typeface Mono = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#JetBrains Mono"));

    static PassbandView()
    {
        AffectsRender<PassbandView>(BandsProperty);
        AffectsMeasure<PassbandView>(BandsProperty);
    }

    public IReadOnlyList<FilterBand>? Bands { get => GetValue(BandsProperty); set => SetValue(BandsProperty, value); }

    /// <summary>The bands worth zooming on: the narrow ones (a broadband filter is already the whole bar).</summary>
    private IReadOnlyList<FilterBand> Narrow => (Bands ?? []).Where(band => band.WidthNm < 60).OrderBy(band => band.CentreNm).Take(4).ToList();

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 240 : availableSize.Width;
        if (Narrow.Count == 0) return new Size(width, 0);
        return new Size(width, PlotHeight + 6 + TextHeight * (Narrow.Any(band => band.Peak > 0) ? 3 : 2));
    }

    public override void Render(DrawingContext context)
    {
        var bands = Narrow;
        if (bands.Count == 0 || Bounds.Width <= 0) return;
        var panel = Math.Min(MaxPanelWidth, (Bounds.Width - Gap * (bands.Count - 1)) / bands.Count);
        // The same window for every band, so that a wider one really looks wider: about a third of the window is the widest band.
        var half = Math.Max(5, bands.Max(band => band.WidthNm) * 1.5);

        for (var index = 0; index < bands.Count; index++)
        {
            var band = bands[index];
            var left = index * (panel + Gap);
            var plot = new Rect(left, 0, panel, PlotHeight);
            var colour = SpectrumColors.Wavelength(band.CentreNm);
            var start = band.CentreNm - half;
            double X(double nm) => left + (nm - start) / (2 * half) * panel;

            context.DrawRectangle(Frame, FrameLine, plot, 8, 8);
            using (context.PushClip(new RoundedRect(plot, 8)))
            {
                // one tick per nanometre: the width can be counted
                for (var nm = Math.Ceiling(start); nm <= start + 2 * half; nm += 1)
                    context.DrawLine(GridLine, new Point(X(nm), PlotHeight - 5), new Point(X(nm), PlotHeight));

                // the emission line this band is there for
                foreach (var line in EmissionLines.All.Where(line => line.WavelengthNm >= start && line.WavelengthNm <= start + 2 * half && line != EmissionLines.Nii))
                {
                    var x = X(line.WavelengthNm);
                    context.DrawLine(LinePen, new Point(x, 4), new Point(x, PlotHeight - 5));
                    var name = new FormattedText(line.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 9, Muted);
                    context.DrawText(name, new Point(Math.Min(x + 3, left + panel - name.Width - 3), 3));
                }

                // the pass band: its edges slope a little, its height is the light it lets through
                var transmission = band.Peak > 0 ? Math.Clamp(band.Peak, 0, 1) : 1;
                var baseline = PlotHeight - 6;
                var top = baseline - (PlotHeight - 18) * transmission;
                var slope = Math.Max(0.5, band.WidthNm * 0.18);
                var shape = new StreamGeometry();
                using (var ctx = shape.Open())
                {
                    ctx.BeginFigure(new Point(X(band.FromNm - slope), baseline), true);
                    ctx.LineTo(new Point(X(band.FromNm), top));
                    ctx.LineTo(new Point(X(band.ToNm), top));
                    ctx.LineTo(new Point(X(band.ToNm + slope), baseline));
                    ctx.EndFigure(true);
                }
                context.DrawGeometry(new ImmutableSolidColorBrush(Color.FromArgb(150, colour.R, colour.G, colour.B)), new ImmutablePen(new ImmutableSolidColorBrush(colour), 1.5), shape);
            }

            var row = PlotHeight + 5;
            Draw(context, $"{band.CentreNm.ToString("0.0", CultureInfo.CurrentCulture)} nm", left, row, Bright);
            Draw(context, $"{CanvasText.T("larghezza")} {band.WidthNm.ToString("0.0", CultureInfo.CurrentCulture)} nm", left, row + TextHeight, Muted);
            if (band.Peak > 0) Draw(context, $"{CanvasText.T("trasmissione")} {(band.Peak * 100).ToString("0", CultureInfo.CurrentCulture)} %", left, row + 2 * TextHeight, Muted);
        }
    }

    private static void Draw(DrawingContext context, string text, double x, double y, IBrush brush) =>
        context.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 10, brush), new Point(x + 2, y));
}
