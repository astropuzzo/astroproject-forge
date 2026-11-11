using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AstroForge.App.Services;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>One filter going through WBPP: its colour, its Lights and the integration it brings.</summary>
public sealed record PipelineStream(string Filter, Color Colour, int Lights, double Seconds);

/// <summary>
/// What WeightedBatchPreprocessing will do with the project: one straight lane per filter, from its Lights through
/// calibration and registration to the master it becomes, with the Lights and the integration time of each master.
/// </summary>
public sealed class PipelineFlow : Control
{
    public static readonly StyledProperty<IReadOnlyList<PipelineStream>?> StreamsProperty =
        AvaloniaProperty.Register<PipelineFlow, IReadOnlyList<PipelineStream>?>(nameof(Streams));
    public static readonly StyledProperty<IReadOnlyList<string>?> KeywordsProperty =
        AvaloniaProperty.Register<PipelineFlow, IReadOnlyList<string>?>(nameof(Keywords));

    private static readonly Typeface Mono = new(new FontFamily("avares://AstroProjectForge/Assets/Fonts#JetBrains Mono"));
    private static readonly Typeface Body = new(new FontFamily("Inter"));
    private static readonly Typeface BodyBold = new(new FontFamily("Inter"), weight: FontWeight.SemiBold);
    private static readonly IBrush Fg = new ImmutableSolidColorBrush(Color.Parse("#EEF1FF"));
    private static readonly IBrush Muted = new ImmutableSolidColorBrush(Color.Parse("#A3ABCD"));
    private static readonly IPen StageRule = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#3396AAFF")), 1);
    private static readonly IBrush Ground = new ImmutableSolidColorBrush(Color.Parse("#0C1326"));

    private static readonly (string Title, string Detail)[] Stages =
    [
        ("Light", "dal progetto"),
        ("Calibrazione", "Flat · Dark · Bias"),
        ("Registrazione", "StarAlignment"),
        ("Integrazione", "un master per filtro")
    ];

    private const double HeaderHeight = 50, LanePitch = 46, KeywordHeight = 26, MasterWidth = 170;

    static PipelineFlow()
    {
        AffectsRender<PipelineFlow>(KeywordsProperty, StreamsProperty);
        AffectsMeasure<PipelineFlow>(KeywordsProperty, StreamsProperty);
    }

    public IReadOnlyList<PipelineStream>? Streams { get => GetValue(StreamsProperty); set => SetValue(StreamsProperty, value); }
    public IReadOnlyList<string>? Keywords { get => GetValue(KeywordsProperty); set => SetValue(KeywordsProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsInfinity(availableSize.Width) ? 720 : availableSize.Width,
        HeaderHeight + Math.Max(1, Streams?.Count ?? 0) * LanePitch + (Keywords is { Count: > 0 } ? KeywordHeight : 0));

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0) return;
        var culture = UiFormat.Culture;
        var streams = Streams ?? [];
        var left = 4.0;
        var right = Math.Max(left + 240, Bounds.Width - MasterWidth);
        var top = HeaderHeight;
        var bottom = top + Math.Max(1, streams.Count) * LanePitch;
        var stageX = Enumerable.Range(0, Stages.Length).Select(index => left + (right - left) * index / (Stages.Length - 1)).ToArray();
        var room = (right - left) / (Stages.Length - 1) - 12;

        for (var index = 0; index < Stages.Length; index++)
        {
            var x = stageX[index];
            var title = new FormattedText(CanvasText.T(Stages[index].Title), culture, FlowDirection.LeftToRight, BodyBold, 12, Fg) { MaxTextWidth = Math.Max(40, room), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            var detail = new FormattedText(CanvasText.T(Stages[index].Detail), culture, FlowDirection.LeftToRight, Body, 11, Muted) { MaxTextWidth = Math.Max(40, room), MaxLineCount = 2, Trimming = TextTrimming.CharacterEllipsis };
            var labelX = index == 0 ? x : Math.Min(x - title.Width / 2, Bounds.Width - Math.Max(title.Width, detail.Width));
            context.DrawText(title, new Point(labelX, 0));
            context.DrawText(detail, new Point(index == 0 ? x : Math.Min(x - detail.Width / 2, Bounds.Width - detail.Width), 17));
            if (index > 0) context.DrawLine(StageRule, new Point(x, top - 2), new Point(x, bottom));
        }

        // The grouping keywords WBPP needs, written under the stage that uses them.
        if (Keywords is { Count: > 0 } keywords)
        {
            var text = new FormattedText(string.Join("   ", keywords), culture, FlowDirection.LeftToRight, Mono, 11, Muted) { MaxTextWidth = Math.Max(60, Bounds.Width - stageX[1] + 20), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            context.DrawText(text, new Point(Math.Max(0, stageX[1] - 20), bottom + 8));
        }

        if (streams.Count == 0)
        {
            var empty = new FormattedText(CanvasText.T("Nessun dato. Analizza il progetto."), culture, FlowDirection.LeftToRight, Body, 12, Muted);
            context.DrawText(empty, new Point(left, top + LanePitch / 2 - 8));
            return;
        }

        for (var index = 0; index < streams.Count; index++)
        {
            var stream = streams[index];
            var y = top + index * LanePitch + LanePitch / 2 + 6;
            var brush = new ImmutableSolidColorBrush(stream.Colour);
            context.DrawLine(new ImmutablePen(brush, 2), new Point(left, y), new Point(right, y));
            for (var stage = 1; stage < Stages.Length - 1; stage++)
                context.DrawEllipse(Ground, new ImmutablePen(brush, 2), new Point(stageX[stage], y), 4, 4);
            context.DrawEllipse(brush, null, new Point(right, y), 6, 6);

            var source = new FormattedText(stream.Filter, culture, FlowDirection.LeftToRight, BodyBold, 12, Fg) { MaxTextWidth = Math.Max(40, room), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            context.DrawText(source, new Point(left, y - source.Height - 5));
            var master = new FormattedText($"Master {stream.Filter}", culture, FlowDirection.LeftToRight, BodyBold, 12, Fg) { MaxTextWidth = MasterWidth - 18, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            var detail = new FormattedText($"{UiFormat.Frames(stream.Lights, "Light")} · {UiFormat.Duration(stream.Seconds / 3600)}", culture, FlowDirection.LeftToRight, Body, 11, Muted) { MaxTextWidth = MasterWidth - 18, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            context.DrawText(master, new Point(right + 14, y - master.Height + 1));
            context.DrawText(detail, new Point(right + 14, y + 2));
        }
    }
}
