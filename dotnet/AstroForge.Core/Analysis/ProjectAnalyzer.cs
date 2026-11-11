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
    string? FlatDecision = null,
    CalibrationGroup? DarkSet = null,
    CalibrationGroup? BiasSet = null);

public sealed record ProjectAnalysis(IReadOnlyList<CalibrationGroup> FlatGroups, IReadOnlyList<LightCalibrationAnalysis> Lights)
{
    public bool Ready => Lights.Count > 0 && Lights.All(item => item.Flat.IsAccepted && item.Dark.IsAccepted && item.Bias.IsAccepted);
    public int UnresolvedCount => Lights.Sum(item => new[] { item.Flat, item.Dark, item.Bias }.Count(result => !result.IsAccepted));

    /// <summary>The same analysis without the Lights the user set aside: they need no calibration and are not integration time.</summary>
    public ProjectAnalysis Without(IReadOnlySet<string>? excludedPaths) =>
        excludedPaths is not { Count: > 0 } ? this : this with { Lights = Lights.Where(item => !excludedPaths.Contains(item.Light.Path)).ToArray() };
}

public static class ProjectAnalyzer
{
    public static ProjectAnalysis Analyze(IEnumerable<FrameMetadata> source, CalibrationPolicy? policy = null)
    {
        var frames = source.ToArray();
        var flatGroups = GroupFlats(frames.Where(frame => frame.Kind == FrameKind.Flat));
        var representatives = flatGroups.Select(group => group.Representative).ToArray();
        var groupByPath = flatGroups.ToDictionary(group => group.Representative.Path, PathIdentity.Comparer);
        var darkSets = SetsByMember(GroupRawSets(frames, FrameKind.Dark));
        var biasSets = SetsByMember(GroupRawSets(frames, FrameKind.Bias));
        var darks = new CalibrationMatchCache(Candidates(frames, FrameKind.Dark, darkSets), FrameKind.Dark, policy);
        var biases = new CalibrationMatchCache(Candidates(frames, FrameKind.Bias, biasSets), FrameKind.Bias, policy);
        // An old stack left in a scanned folder (a master Light) is not a sub to calibrate and integrate again.
        var lightFrames = frames.Where(frame => frame.Kind == FrameKind.Light && !frame.IsMaster).ToArray();
        var flatMatches = ResolveFlatEpochs(lightFrames, flatGroups, representatives, policy);
        var lights = new List<LightCalibrationAnalysis>();
        foreach (var light in lightFrames)
        {
            var flatResolution = flatMatches[light.Path];
            var flat = flatResolution.Result;
            var dark = ResolveManual(darks.Find(light), light.ManualDarkPath.Value, darkSets);
            var bias = ResolveManual(biases.Find(light), light.ManualBiasPath.Value, biasSets);
            var group = flat.Selected is not null ? groupByPath.GetValueOrDefault(flat.Selected.Frame.Path) : null;
            lights.Add(new(light, flat, dark, bias, group, flatResolution.Decision, SetOf(dark, darkSets), SetOf(bias, biasSets)));
        }
        return new(flatGroups, lights);
    }

    /// <summary>
    /// Raw Dark or Bias subs that share camera, geometry, gain, offset, exposure and temperature are one set: the match is
    /// made against the set and the whole set is exported for WBPP to integrate, the way Flats already work. Without this
    /// every sub of a night of Darks tied with the others and one of them ended up exported as "the" Master.
    /// </summary>
    public static IReadOnlyList<CalibrationGroup> GroupRawSets(IEnumerable<FrameMetadata> source, FrameKind kind)
    {
        var sets = new List<CalibrationGroup>();
        var families = source.Where(frame => frame.Kind == kind && !frame.IsMaster)
            .GroupBy(frame => string.Join('|', Normalize(frame.Camera.Value), frame.Width.Value, frame.Height.Value, frame.XBin.Value, frame.YBin.Value,
                frame.Gain.Value, frame.Offset.Value, Normalize(frame.ReadoutMode.Value), Normalize(frame.BayerPattern.Value),
                kind == FrameKind.Bias ? null : Bucket(frame.ExposureSeconds.Value, 0.1)))
            .OrderBy(family => family.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var family in families)
        {
            // A sensor without a set point drifts by tenths of a degree: frames belong together until the temperature jumps by more than 1 °C.
            var current = new List<FrameMetadata>();
            double? last = null;
            foreach (var frame in family.OrderBy(frame => frame.EffectiveTemperatureC ?? double.MinValue).ThenBy(frame => frame.Path, StringComparer.OrdinalIgnoreCase))
            {
                var temperature = frame.EffectiveTemperatureC;
                if (current.Count > 0 && (temperature.HasValue != last.HasValue || temperature is { } now && last is { } before && now - before > 1))
                {
                    sets.Add(new($"RAW-{sets.Count + 1:00}", kind, MedianFirst(current)));
                    current = [];
                }
                current.Add(frame);
                last = temperature;
            }
            if (current.Count > 0) sets.Add(new($"RAW-{sets.Count + 1:00}", kind, MedianFirst(current)));
        }
        return sets;
    }

    /// <summary>
    /// The set's frames by path, with the one at the median temperature first: it stands for the set when matching and
    /// naming, so a set read between -9.4 and -10.3 °C is a -10 °C set and not whatever its first file happened to read.
    /// </summary>
    private static FrameMetadata[] MedianFirst(List<FrameMetadata> byTemperature)
    {
        var median = byTemperature[byTemperature.Count / 2];
        return [median, .. byTemperature.Where(frame => !ReferenceEquals(frame, median)).OrderBy(frame => frame.Path, StringComparer.OrdinalIgnoreCase)];
    }

    private static Dictionary<string, CalibrationGroup> SetsByMember(IReadOnlyList<CalibrationGroup> sets)
    {
        var map = new Dictionary<string, CalibrationGroup>(PathIdentity.Comparer);
        foreach (var set in sets)
            foreach (var frame in set.Frames) map[frame.Path] = set;
        return map;
    }

    /// <summary>What a Light can be matched against: every Master, and one representative per set of raw subs.</summary>
    private static IEnumerable<FrameMetadata> Candidates(IEnumerable<FrameMetadata> frames, FrameKind kind, Dictionary<string, CalibrationGroup> sets) =>
        frames.Where(frame => frame.Kind == kind && (!sets.TryGetValue(frame.Path, out var set) || ReferenceEquals(set.Representative, frame)));

    private static CalibrationGroup? SetOf(MatchResult result, Dictionary<string, CalibrationGroup> sets) =>
        result.Selected is { } selected ? sets.GetValueOrDefault(selected.Frame.Path) : null;

    private static MatchResult ResolveManual(MatchResult automatic, string? requestedPath, Dictionary<string, CalibrationGroup> sets)
    {
        if (string.IsNullOrWhiteSpace(requestedPath)) return automatic;
        // A choice saved on one sub of a set stands for its set.
        if (sets.TryGetValue(requestedPath, out var requestedSet)) requestedPath = requestedSet.Representative.Path;
        var match = automatic.Candidates.FirstOrDefault(candidate => PathIdentity.Equals(candidate.Frame.Path, requestedPath));
        if (match is null || !match.Compatible)
            return new(automatic.RequestedKind, MatchStatus.Incompatible, null, automatic.Candidates);
        // The export names folders after gain, offset and temperature: a pick that lacks them is not ready yet.
        if (match.MissingRequired.Count > 0)
            return new(automatic.RequestedKind, MatchStatus.InsufficientMetadata, null, automatic.Candidates);
        // The automatic result is shared by every light with the same signature: annotate a copy.
        var selected = match.Clone();
        selected.Reasons.Add("Master assegnato manualmente dall'utente");
        var candidates = automatic.Candidates.Select(candidate => ReferenceEquals(candidate, match) ? selected : candidate).ToArray();
        return new(automatic.RequestedKind, selected.Exact ? MatchStatus.Exact : MatchStatus.WithinTolerance, selected, candidates);
    }

    public static IReadOnlyList<CalibrationGroup> GroupFlats(IEnumerable<FrameMetadata> source)
    {
        var groups = source.GroupBy(frame => string.Join('|',
            string.IsNullOrWhiteSpace(frame.FlatSetId.Value) ? frame.SessionId.Value ?? "unknown" : $"explicit:{Normalize(frame.FlatSetId.Value)}",
            Normalize(frame.FilterName.Value), Normalize(frame.Camera.Value),
            frame.Width.Value, frame.Height.Value, frame.XBin.Value, frame.YBin.Value,
            frame.Gain.Value, frame.Offset.Value, Normalize(frame.ReadoutMode.Value), Normalize(frame.BayerPattern.Value),
            Bucket(frame.FocalLengthMm.Value, 1), Bucket(frame.RotatorAngleDeg.Value, 0.5), frame.IsMaster))
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

    // The Flat Set the user asked for is part of the key: a choice made for some Lights of a night must neither be
    // ignored nor spread to the rest of the night, whichever file happens to come first.
    private static string LightEpochKey(FrameMetadata frame) => string.Join('|',
        frame.SessionId.Value ?? "unknown", Normalize(frame.FlatSetId.Value), Normalize(frame.FilterName.Value), Normalize(frame.Camera.Value),
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
