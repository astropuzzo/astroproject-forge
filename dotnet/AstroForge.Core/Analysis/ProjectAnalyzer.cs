using AstroForge.Core.Matching;
using AstroForge.Core.Models;
using AstroForge.Core.IO;

namespace AstroForge.Core.Analysis;

public sealed record CalibrationGroup(string Id, FrameKind Kind, IReadOnlyList<FrameMetadata> Frames)
{
    public FrameMetadata Representative => Frames[0];
}

public sealed record LightCalibrationAnalysis(
    FrameMetadata Light,
    MatchResult Flat,
    MatchResult Dark,
    MatchResult Bias,
    CalibrationGroup? FlatGroup,
    string? FlatDecision = null);

public sealed record ProjectAnalysis(IReadOnlyList<CalibrationGroup> FlatGroups, IReadOnlyList<LightCalibrationAnalysis> Lights)
{
    public bool Ready => Lights.Count > 0 && Lights.All(item => item.Flat.IsAccepted && item.Dark.IsAccepted && item.Bias.IsAccepted);
    public int UnresolvedCount => Lights.Sum(item => new[] { item.Flat, item.Dark, item.Bias }.Count(result => !result.IsAccepted));
}

public static class ProjectAnalyzer
{
    public static ProjectAnalysis Analyze(IEnumerable<FrameMetadata> source, CalibrationPolicy? policy = null)
    {
        var frames = source.ToArray();
        var flatGroups = GroupFlats(frames.Where(frame => frame.Kind == FrameKind.Flat));
        var representatives = flatGroups.Select(group => group.Representative).ToArray();
        var groupByPath = flatGroups.ToDictionary(group => group.Representative.Path, PathIdentity.Comparer);
        var darks = frames.Where(frame => frame.Kind == FrameKind.Dark).ToArray();
        var biases = frames.Where(frame => frame.Kind == FrameKind.Bias).ToArray();
        var lightFrames = frames.Where(frame => frame.Kind == FrameKind.Light).ToArray();
        var flatMatches = ResolveFlatEpochs(lightFrames, flatGroups, representatives, policy);
        var lights = new List<LightCalibrationAnalysis>();
        foreach (var light in lightFrames)
        {
            var flatResolution = flatMatches[light.Path];
            var flat = flatResolution.Result;
            var dark = ResolveManual(CalibrationMatcher.Find(light, darks, FrameKind.Dark, policy), light.ManualDarkPath.Value);
            var bias = ResolveManual(CalibrationMatcher.Find(light, biases, FrameKind.Bias, policy), light.ManualBiasPath.Value);
            var group = flat.Selected is not null ? groupByPath.GetValueOrDefault(flat.Selected.Frame.Path) : null;
            lights.Add(new(light, flat, dark, bias, group, flatResolution.Decision));
        }
        return new(flatGroups, lights);
    }

    private static MatchResult ResolveManual(MatchResult automatic, string? requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath)) return automatic;
        var selected = automatic.Candidates.FirstOrDefault(candidate => PathIdentity.Equals(candidate.Frame.Path, requestedPath));
        if (selected is null || !selected.Compatible)
            return new(automatic.RequestedKind, MatchStatus.Incompatible, null, automatic.Candidates);
        selected.Reasons.Add("Master assegnato manualmente dall'utente");
        return new(automatic.RequestedKind, selected.Exact ? MatchStatus.Exact : MatchStatus.WithinTolerance, selected, automatic.Candidates);
    }

    public static IReadOnlyList<CalibrationGroup> GroupFlats(IEnumerable<FrameMetadata> source)
    {
        var groups = source.GroupBy(frame => string.Join('|',
            string.IsNullOrWhiteSpace(frame.FlatSetId.Value) ? frame.SessionId.Value ?? "unknown" : $"explicit:{Normalize(frame.FlatSetId.Value)}",
            Normalize(frame.FilterName.Value), Normalize(frame.Camera.Value),
            frame.Width.Value, frame.Height.Value, frame.XBin.Value, frame.YBin.Value,
            frame.Gain.Value, frame.Offset.Value, Normalize(frame.ReadoutMode.Value), Normalize(frame.BayerPattern.Value),
            Bucket(frame.FocalLengthMm.Value, 1), Bucket(frame.RotatorAngleDeg.Value, 0.5)))
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        return groups.Select((group, index) =>
        {
            var frames = group.OrderBy(frame => frame.Path, StringComparer.OrdinalIgnoreCase).ToArray();
            var representative = frames[0];
            var id = string.IsNullOrWhiteSpace(representative.FlatSetId.Value)
                ? $"AUTO-{representative.SessionId.Value ?? "UNKNOWN"}-{Token(representative.FilterName.Value ?? "UNKNOWN")}-{index + 1:00}"
                : $"USER-{Token(representative.FlatSetId.Value)}";
            return new CalibrationGroup(id, FrameKind.Flat, frames);
        }).ToArray();
    }

    private sealed record FlatResolution(MatchResult Result, string? Decision);

    private static Dictionary<string, FlatResolution> ResolveFlatEpochs(
        IReadOnlyList<FrameMetadata> lights,
        IReadOnlyList<CalibrationGroup> flatGroups,
        IReadOnlyList<FrameMetadata> representatives,
        CalibrationPolicy? policy)
    {
        var output = new Dictionary<string, FlatResolution>(PathIdentity.Comparer);
        foreach (var epoch in lights.GroupBy(LightEpochKey))
        {
            var epochLights = epoch.ToArray();
            var representative = epochLights[0];
            var initial = CalibrationMatcher.Find(representative, representatives, FrameKind.Flat, policy);
            var resolution = ResolveExplicitFlatSet(representative, initial, flatGroups) ?? ResolveTemporalFlatEpoch(epochLights, initial, flatGroups);
            foreach (var light in epochLights) output[light.Path] = resolution;
        }
        return output;
    }

    private static FlatResolution? ResolveExplicitFlatSet(FrameMetadata light, MatchResult initial, IReadOnlyList<CalibrationGroup> groups)
    {
        if (string.IsNullOrWhiteSpace(light.FlatSetId.Value)) return null;
        var requested = Normalize(light.FlatSetId.Value);
        var candidates = initial.Candidates.Where(candidate => candidate.Compatible && candidate.MissingRequired.Count == 0)
            .Where(candidate => groups.First(group => PathIdentity.Equals(group.Representative.Path, candidate.Frame.Path)).Frames.Any(flat => Normalize(flat.FlatSetId.Value) == requested)).ToArray();
        if (candidates.Length == 1)
        {
            candidates[0].Reasons.Add($"Flat Epoch imposto dall'utente: {light.FlatSetId.Value}");
            return new(new(FrameKind.Flat, candidates[0].Exact ? MatchStatus.Exact : MatchStatus.WithinTolerance, candidates[0], initial.Candidates), $"Assegnazione manuale Flat Epoch '{light.FlatSetId.Value}'");
        }
        var status = candidates.Length > 1 ? MatchStatus.Ambiguous : MatchStatus.Missing;
        return new(new(FrameKind.Flat, status, null, initial.Candidates), $"Flat Epoch richiesto '{light.FlatSetId.Value}' non risolto");
    }

    private static FlatResolution ResolveTemporalFlatEpoch(IReadOnlyList<FrameMetadata> lights, MatchResult initial, IReadOnlyList<CalibrationGroup> groups)
    {
        if (initial.IsAccepted) return new(initial, "Unico Flat Set compatibile");
        if (initial.Status != MatchStatus.Ambiguous) return new(initial, null);
        var epochTime = MedianTime(lights.Select(light => light.CapturedAt.Value));
        if (epochTime is null) return new(initial, "Timeline non disponibile: data dei Light mancante");

        var ranked = initial.Candidates.Where(candidate => candidate.Compatible && candidate.MissingRequired.Count == 0)
            .Select(candidate =>
            {
                var group = groups.First(group => PathIdentity.Equals(group.Representative.Path, candidate.Frame.Path));
                var flatTime = MedianTime(group.Frames.Select(flat => flat.CapturedAt.Value));
                return new { Candidate = candidate, Group = group, FlatTime = flatTime, Distance = flatTime is null ? TimeSpan.MaxValue : (flatTime.Value - epochTime.Value).Duration() };
            })
            .Where(item => item.FlatTime is not null).OrderBy(item => item.Distance).ThenBy(item => item.Group.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        if (ranked.Length < 2) return new(initial, null);
        var first = ranked[0];
        var second = ranked[1];
        var margin = second.Distance - first.Distance;
        var confident = margin >= TimeSpan.FromHours(12) &&
                        (first.Distance <= TimeSpan.FromHours(36) || second.Distance.TotalHours >= first.Distance.TotalHours * 1.5);
        if (!confident) return new(initial, $"Flat Epoch temporale incerta: {first.Group.Id} e {second.Group.Id} troppo vicini");

        first.Candidate.Exact = false;
        first.Candidate.Reasons.Add($"Flat Epoch temporale: set {first.Group.Id}, distanza {first.Distance.TotalHours:0.#} h, margine {margin.TotalHours:0.#} h");
        var result = new MatchResult(FrameKind.Flat, MatchStatus.WithinTolerance, first.Candidate, initial.Candidates);
        return new(result, $"Flat Epoch automatica {first.Group.Id} · distanza {first.Distance.TotalHours:0.#} h · margine {margin.TotalHours:0.#} h");
    }

    private static string LightEpochKey(FrameMetadata frame) => string.Join('|',
        frame.SessionId.Value ?? "unknown", Normalize(frame.FilterName.Value), Normalize(frame.Camera.Value),
        frame.Width.Value, frame.Height.Value, frame.XBin.Value, frame.YBin.Value,
        frame.Gain.Value, frame.Offset.Value, Normalize(frame.ReadoutMode.Value), Normalize(frame.BayerPattern.Value),
        Bucket(frame.FocalLengthMm.Value, 1), Bucket(frame.RotatorAngleDeg.Value, 0.5));

    private static DateTimeOffset? MedianTime(IEnumerable<DateTimeOffset?> values)
    {
        var ordered = values.Where(value => value.HasValue).Select(value => value!.Value).OrderBy(value => value).ToArray();
        if (ordered.Length == 0) return null;
        if (ordered.Length % 2 == 1) return ordered[ordered.Length / 2];
        var left = ordered[ordered.Length / 2 - 1];
        var right = ordered[ordered.Length / 2];
        return left + TimeSpan.FromTicks((right - left).Ticks / 2);
    }

    private static string Normalize(string? value) => string.Join(' ', (value ?? "").ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static long? Bucket(double? value, double size) => value is null ? null : (long)Math.Round(value.Value / size);
    private static string Token(string value) => string.Join('-', value.Split().Select(part => new string(part.Where(char.IsLetterOrDigit).ToArray())).Where(part => part.Length > 0));
}
