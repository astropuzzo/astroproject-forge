using Avalonia;
using Avalonia.Media;
using AstroForge.Core.Filters;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>Colours of light: turns a filter's pass bands into the tint its glass would show.</summary>
public static class SpectrumColors
{
    public const double MinNm = 380, MaxNm = 720;

    /// <summary>Approximate sRGB of a monochromatic wavelength (Bruton), brightened for a dark UI.</summary>
    public static Color Wavelength(double nm, byte alpha = 255)
    {
        double r, g, b;
        if (nm < 440) { r = -(nm - 440) / 60; g = 0; b = 1; }
        else if (nm < 490) { r = 0; g = (nm - 440) / 50; b = 1; }
        else if (nm < 510) { r = 0; g = 1; b = -(nm - 510) / 20; }
        else if (nm < 580) { r = (nm - 510) / 70; g = 1; b = 0; }
        else if (nm < 645) { r = 1; g = -(nm - 645) / 65; b = 0; }
        else { r = 1; g = 0; b = 0; }
        var edge = nm < 420 ? 0.35 + 0.65 * (nm - 380) / 40 : nm > 680 ? 0.35 + 0.65 * (720 - nm) / 40 : 1;
        static byte Channel(double value, double edge) => (byte)Math.Round(255 * Math.Pow(Math.Clamp(value * edge, 0, 1), 0.8) * 0.85 + 38);
        return Color.FromArgb(alpha, Channel(r, edge), Channel(g, edge), Channel(b, edge));
    }

    /// <summary>The glass colour of a filter: the average of its bands, or a neutral tint for luminance and clear glass.</summary>
    public static Color Glass(IReadOnlyList<FilterBand> bands, FilterKind kind = FilterKind.Unknown)
    {
        if (kind is FilterKind.None) return Color.Parse("#5B6490");
        if (bands.Count == 0) return kind == FilterKind.Unknown ? Color.Parse("#FFC27A") : Color.Parse("#C9D3F5");
        if (bands.Sum(band => band.WidthNm) > 180) return Color.Parse("#DDE4FF");
        double r = 0, g = 0, b = 0, weight = 0;
        foreach (var band in bands)
        {
            var colour = Wavelength(band.Peak > 0 ? band.Peak : (band.FromNm + band.ToNm) / 2);
            var w = Math.Max(1, Math.Min(band.WidthNm, 120));
            r += colour.R * w; g += colour.G * w; b += colour.B * w; weight += w;
        }
        return Color.FromRgb((byte)(r / weight), (byte)(g / weight), (byte)(b / weight));
    }

    public static IBrush GlassBrush(IReadOnlyList<FilterBand> bands, FilterKind kind)
    {
        var narrow = bands.Where(band => band.WidthNm < 60).ToList();
        if (narrow.Count >= 2 && narrow.Count == bands.Count)
        {
            // Dual and quad band glass reads as split colour, one half per line.
            var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative) };
            var ordered = narrow.OrderBy(band => band.Peak).ToList();
            for (var index = 0; index < ordered.Count; index++)
            {
                var colour = Wavelength(ordered[index].Peak > 0 ? ordered[index].Peak : (ordered[index].FromNm + ordered[index].ToNm) / 2);
                brush.GradientStops.Add(new GradientStop(colour, index / (double)ordered.Count));
                brush.GradientStops.Add(new GradientStop(colour, (index + 1) / (double)ordered.Count));
            }
            return brush;
        }
        return new SolidColorBrush(Glass(bands, kind));
    }

    public static IReadOnlyList<FilterBand> BandsOf(FilterIdentity identity)
    {
        if (identity.Product is { } product) return product.Bands;
        if (identity.Kind == FilterKind.Broadband)
            return identity.DisplayName switch
            {
                "R" => [new FilterBand(590, 700, 640)],
                "G" => [new FilterBand(490, 580, 535)],
                "B" => [new FilterBand(400, 500, 450)],
                _ => []
            };
        var width = identity.BandwidthNm ?? 7;
        return identity.Lines.Select(line => new FilterBand(line.WavelengthNm - width / 2, line.WavelengthNm + width / 2, line.WavelengthNm)).ToList();
    }
}
