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

    private static readonly IBrush Label = new ImmutableSolidColorBrush(Color.Parse("#8E97BD"));
    private static readonly IBrush Tick = new ImmutableSolidColorBrush(Color.Parse("#405B6490"));
    private static readonly Typeface Mono = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#JetBrains Mono"));

    static SpectrumBar() => AffectsRender<SpectrumBar>(BandsProperty, ShowLabelsProperty);

    public IReadOnlyList<FilterBand>? Bands { get => GetValue(BandsProperty); set => SetValue(BandsProperty, value); }
    public bool ShowLabels { get => GetValue(ShowLabelsProperty); set => SetValue(ShowLabelsProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width, ShowLabels ? 30 : 12);

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        if (width <= 0) return;
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
            var colour = SpectrumColors.Wavelength(band.Peak > 0 ? band.Peak : (band.FromNm + band.ToNm) / 2);
            var left = X(band.FromNm);
            var right = Math.Max(left + 2.5, X(band.ToNm));
            var glow = new Rect(left - 3, track.Y - 2, right - left + 6, track.Height + 4);
            context.DrawRectangle(new ImmutableSolidColorBrush(Color.FromArgb(70, colour.R, colour.G, colour.B)), null, glow, 5, 5);
            context.DrawRectangle(new ImmutableSolidColorBrush(colour), null, new Rect(left, track.Y, right - left, track.Height), 3, 3);
        }

        if (!ShowLabels) return;
        var labelled = new List<double>();
        foreach (var band in (Bands ?? []).Where(band => band.WidthNm < 60).OrderBy(band => band.Peak))
        {
            var peak = band.Peak > 0 ? band.Peak : (band.FromNm + band.ToNm) / 2;
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
}
