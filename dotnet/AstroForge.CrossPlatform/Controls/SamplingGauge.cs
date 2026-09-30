using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>
/// Image scale in ″/px on a 0.5–4″ ruler, against the 1–2″/px band that samples a typical 2–4″ seeing
/// at Nyquist. The marker slides when the optics change.
/// </summary>
public sealed class SamplingGauge : Control
{
    public static readonly StyledProperty<double> ScaleProperty = AvaloniaProperty.Register<SamplingGauge, double>(nameof(Scale));
    private const double Min = 0.5, Max = 4;
    private static readonly Typeface Mono = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#JetBrains Mono"));
    private static readonly IBrush Dim = new ImmutableSolidColorBrush(Color.Parse("#5B6490"));
    private static readonly IBrush Track = new ImmutableSolidColorBrush(Color.Parse("#1F9DB8FF"));
    private static readonly IBrush Band = new ImmutableSolidColorBrush(Color.Parse("#737CF2C9"));
    private static readonly IBrush Marker = new ImmutableSolidColorBrush(Color.Parse("#EEF1FF"));
    private double _shown;

    public double Scale { get => GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ScaleProperty) return;
        var from = _shown <= 0 ? Scale : _shown;
        _ = Motion.Tween(TopLevel.GetTopLevel(this), TimeSpan.FromMilliseconds(800), Motion.EaseOutBack, t => { _shown = from + (Scale - from) * t; InvalidateVisual(); });
    }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 160 : Math.Min(availableSize.Width, 220), 30);

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        if (width <= 0) return;
        double X(double value) => (Math.Clamp(value, Min, Max) - Min) / (Max - Min) * width;
        context.DrawRectangle(Track, null, new Rect(0, 6, width, 4), 2, 2);
        context.DrawRectangle(Band, null, new Rect(X(1), 5, X(2) - X(1), 6), 3, 3);
        if (_shown > 0) context.DrawRectangle(Marker, null, new Rect(X(_shown) - 1, 1, 2, 14), 1, 1);
        foreach (var tick in new[] { 0.5, 1, 2, 3, 4 })
        {
            var label = new FormattedText(tick.ToString("0.#", CultureInfo.CurrentCulture) + (tick == 4 ? "″/px" : "″"), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 8.5, Dim);
            context.DrawText(label, new Point(Math.Clamp(X(tick) - label.Width / 2, 0, width - label.Width), 17));
        }
    }
}
