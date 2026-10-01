using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AstroForge.Core.Filters;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>
/// A filter's transmission drawn on the visible spectrum (380–720 nm): pass bands glow in their own
/// colour over a faint rainbow, with ticks at the emission lines astrophotographers chase.
/// </summary>
public sealed class SpectrumBar : Control
{
    public static readonly StyledProperty<IReadOnlyList<FilterBand>?> BandsProperty =
        AvaloniaProperty.Register<SpectrumBar, IReadOnlyList<FilterBand>?>(nameof(Bands));
    public static readonly StyledProperty<bool> ShowLabelsProperty =
        AvaloniaProperty.Register<SpectrumBar, bool>(nameof(ShowLabels), true);
    public static readonly StyledProperty<bool> LargeProperty =
        AvaloniaProperty.Register<SpectrumBar, bool>(nameof(Large));

    private static readonly IBrush Label = new ImmutableSolidColorBrush(Color.Parse("#8E97BD"));
    private static readonly IBrush Tick = new ImmutableSolidColorBrush(Color.Parse("#405B6490"));
    private static readonly Typeface Mono = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#JetBrains Mono"));

    static SpectrumBar()
    {
        AffectsRender<SpectrumBar>(BandsProperty, ShowLabelsProperty, LargeProperty);
        AffectsMeasure<SpectrumBar>(ShowLabelsProperty, LargeProperty);
    }

    public IReadOnlyList<FilterBand>? Bands { get => GetValue(BandsProperty); set => SetValue(BandsProperty, value); }
    public bool ShowLabels { get => GetValue(ShowLabelsProperty); set => SetValue(ShowLabelsProperty, value); }
    /// <summary>The card variant: a tall dimmed spectrum with each pass band as a glowing line labelled inside the bar.</summary>
    public bool Large { get => GetValue(LargeProperty); set => SetValue(LargeProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width, Large ? 26 : ShowLabels ? 30 : 12);

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        if (width <= 0) return;
        if (Large) { RenderLarge(context, width); return; }
        var track = new Rect(0, 2, width, 8);
        var rainbow = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative) };
        for (var nm = SpectrumColors.MinNm; nm <= SpectrumColors.MaxNm; nm += 20)
            rainbow.GradientStops.Add(new GradientStop(SpectrumColors.Wavelength(nm, 40), (nm - SpectrumColors.MinNm) / (SpectrumColors.MaxNm - SpectrumColors.MinNm)));
        context.DrawRectangle(rainbow, null, track, 4, 4);

        double X(double nm) => (Math.Clamp(nm, SpectrumColors.MinNm, SpectrumColors.MaxNm) - SpectrumColors.MinNm) / (SpectrumColors.MaxNm - SpectrumColors.MinNm) * width;

        foreach (var line in EmissionLines.All.Where(line => line != EmissionLines.Nii))
            context.FillRectangle(Tick, new Rect(X(line.WavelengthNm) - 0.5, 0, 1, 12));

        foreach (var band in Bands ?? [])
        {
            var colour = SpectrumColors.Wavelength(band.CentreNm);
            var left = X(band.FromNm);
            var right = Math.Max(left + 4, X(band.ToNm));
            var glow = new Rect(left - 3, track.Y - 2, right - left + 6, track.Height + 4);
            context.DrawRectangle(new ImmutableSolidColorBrush(Color.FromArgb(70, colour.R, colour.G, colour.B)), null, glow, 5, 5);
            context.DrawRectangle(new ImmutableSolidColorBrush(colour), null, new Rect(left, track.Y, right - left, track.Height), 3, 3);
        }

        if (!ShowLabels) return;
        var labelled = new List<double>();
        foreach (var band in (Bands ?? []).Where(band => band.WidthNm < 60).OrderBy(band => band.CentreNm))
        {
            var peak = band.CentreNm;
            var x = X(peak);
            if (labelled.Any(other => Math.Abs(other - x) < 44)) continue;
            labelled.Add(x);
            var text = new FormattedText(peak.ToString("0.#", CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 9.5, Label);
            context.DrawText(text, new Point(Math.Clamp(x - text.Width / 2, 0, width - text.Width), 15));
        }
        if (labelled.Count == 0 && Bands is { Count: > 0 })
        {
            var text = new FormattedText(CanvasText.T("banda larga"), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 9.5, Label);
            context.DrawText(text, new Point(0, 15));
        }
    }

    private static readonly IBrush Shade = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#8C05070F"), 0), new GradientStop(Color.Parse("#4005070F"), 0.5), new GradientStop(Color.Parse("#8C05070F"), 1) }
    }.ToImmutable();
    private static readonly IBrush LargeLabel = new ImmutableSolidColorBrush(Color.Parse("#D9EEF1FF"));

    private void RenderLarge(DrawingContext context, double width)
    {
        var track = new Rect(0, 0, width, 26);
        var rainbow = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative) };
        for (var nm = SpectrumColors.MinNm; nm <= SpectrumColors.MaxNm; nm += 20)
            rainbow.GradientStops.Add(new GradientStop(SpectrumColors.Wavelength(nm, 80), (nm - SpectrumColors.MinNm) / (SpectrumColors.MaxNm - SpectrumColors.MinNm)));
        using (context.PushClip(new RoundedRect(track, 7)))
        {
            context.DrawRectangle(new ImmutableSolidColorBrush(Color.Parse("#0A0D1C")), null, track);
            context.DrawRectangle(rainbow, null, track);
            context.DrawRectangle(Shade, null, track);

            double X(double nm) => (Math.Clamp(nm, SpectrumColors.MinNm, SpectrumColors.MaxNm) - SpectrumColors.MinNm) / (SpectrumColors.MaxNm - SpectrumColors.MinNm) * width;
            var bands = Bands ?? [];
            foreach (var band in bands)
            {
                var colour = SpectrumColors.Wavelength(band.CentreNm);
                var left = X(band.FromNm);
                var right = Math.Max(left + 4, X(band.ToNm));
                if (band.WidthNm >= 60)
                {
                    context.DrawRectangle(new ImmutableSolidColorBrush(Color.FromArgb(90, colour.R, colour.G, colour.B)), null, new Rect(left, 0, right - left, 26));
                    continue;
                }
                context.DrawRectangle(new ImmutableSolidColorBrush(Color.FromArgb(80, colour.R, colour.G, colour.B)), null, new Rect(left - 5, 0, right - left + 10, 26));
                context.DrawRectangle(new ImmutableSolidColorBrush(colour), null, new Rect(left, 0, right - left, 26));
            }
            var labelled = new List<double>();
            foreach (var band in bands.Where(band => band.WidthNm < 60).OrderBy(band => band.CentreNm))
            {
                var peak = band.CentreNm;
                var x = X(peak);
                if (labelled.Any(other => Math.Abs(other - x) < 60)) continue;
                labelled.Add(x);
                var text = new FormattedText(peak.ToString("0.0", CultureInfo.CurrentCulture) + (labelled.Count == bands.Count(b => b.WidthNm < 60) ? " nm" : ""),
                    CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 9, LargeLabel);
                var tx = x + 6 + text.Width > width - 4 ? x - 6 - text.Width : x + 6;
                context.DrawText(text, new Point(tx, 13 - text.Height / 2));
            }
        }
    }
}
