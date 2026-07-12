using AstroForge.Core.Models;

namespace AstroForge.Core.Matching;

public enum MatchStatus
{
    Exact,
    WithinTolerance,
    Ambiguous,
    InsufficientMetadata,
    Missing,
    Incompatible
}

public sealed record CalibrationPolicy(
    double DarkExposureToleranceSeconds = 0.5,
    double TemperatureToleranceC = 2.0,
    double RotatorToleranceDegrees = 0.5,
    double FocalLengthRelativeTolerance = 0.01);

public sealed class MatchCandidate
{
    public required FrameMetadata Frame { get; init; }
    public bool Compatible { get; set; } = true;
    public int Score { get; set; }
    public bool Exact { get; set; } = true;
    public List<string> Reasons { get; } = [];
    public List<string> MissingRequired { get; } = [];
}

public sealed record MatchResult(FrameKind RequestedKind, MatchStatus Status, MatchCandidate? Selected, IReadOnlyList<MatchCandidate> Candidates)
{
    public bool IsAccepted => Status is MatchStatus.Exact or MatchStatus.WithinTolerance;
}

public static class CalibrationMatcher
{
    public static MatchResult Find(FrameMetadata target, IEnumerable<FrameMetadata> frames, FrameKind requestedKind, CalibrationPolicy? policy = null)
    {
        policy ??= new();
        var evaluated = frames.Where(frame => frame.Kind == requestedKind).Select(frame => Evaluate(target, frame, requestedKind, policy)).ToArray();
        if (evaluated.Length == 0) return new(requestedKind, MatchStatus.Missing, null, []);
        var compatible = evaluated.Where(candidate => candidate.Compatible)
            .OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.Frame.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        if (compatible.Length == 0) return new(requestedKind, MatchStatus.Incompatible, null, evaluated);
        var best = compatible[0];
        if (compatible.Count(candidate => candidate.Score == best.Score) > 1) return new(requestedKind, MatchStatus.Ambiguous, null, compatible);
        if (best.MissingRequired.Count > 0) return new(requestedKind, MatchStatus.InsufficientMetadata, null, compatible);
        return new(requestedKind, best.Exact ? MatchStatus.Exact : MatchStatus.WithinTolerance, best, compatible);
    }

    private static MatchCandidate Evaluate(FrameMetadata target, FrameMetadata candidate, FrameKind requestedKind, CalibrationPolicy policy)
    {
        var result = new MatchCandidate { Frame = candidate };
        SameText(result, "camera", target.Camera.Value, candidate.Camera.Value, 8, true);
        Same(result, "larghezza", target.Width.Value, candidate.Width.Value, 8, true);
        Same(result, "altezza", target.Height.Value, candidate.Height.Value, 8, true);
        Same(result, "binning X", target.XBin.Value, candidate.XBin.Value, 8, true);
        Same(result, "binning Y", target.YBin.Value, candidate.YBin.Value, 8, true);
        Same(result, "gain", target.Gain.Value, candidate.Gain.Value, 7, true);
        Same(result, "offset", target.Offset.Value, candidate.Offset.Value, 5, requestedKind is FrameKind.Dark or FrameKind.Bias);
        SameText(result, "readout", target.ReadoutMode.Value, candidate.ReadoutMode.Value, 6, false);
        SameText(result, "Bayer", target.BayerPattern.Value, candidate.BayerPattern.Value, 5, false);

        if (requestedKind == FrameKind.Flat)
        {
            SameText(result, "filtro", target.FilterName.Value, candidate.FilterName.Value, 20, true);
            Within(result, "rotatore", target.RotatorAngleDeg.Value, candidate.RotatorAngleDeg.Value, policy.RotatorToleranceDegrees, 6, false);
            Relative(result, "focale", target.FocalLengthMm.Value, candidate.FocalLengthMm.Value, policy.FocalLengthRelativeTolerance, 6);
            if (target.SessionId.Value is not null && target.SessionId.Value == candidate.SessionId.Value) { result.Score += 2; result.Reasons.Add("stessa sessione"); }
        }
        else if (requestedKind is FrameKind.Dark or FrameKind.DarkFlat)
        {
            Within(result, "esposizione", target.ExposureSeconds.Value, candidate.ExposureSeconds.Value, policy.DarkExposureToleranceSeconds, 20, true);
            Within(result, "temperatura", target.EffectiveTemperatureC, candidate.EffectiveTemperatureC, policy.TemperatureToleranceC, 14, true);
        }
        else if (requestedKind == FrameKind.Bias)
            Within(result, "temperatura", target.EffectiveTemperatureC, candidate.EffectiveTemperatureC, policy.TemperatureToleranceC, 8, false);

        if (candidate.IsMaster) { result.Score++; result.Reasons.Add("Master riconosciuto"); }
        if (FromConfiguredLibrary(candidate))
        {
            result.Score += 4;
            result.Reasons.Add("Master proveniente dalla libreria configurata");
        }
        return result;
    }

    private static bool FromConfiguredLibrary(FrameMetadata frame) =>
        frame.Gain.OriginalSource == MetadataSource.LibraryPath ||
        frame.SetTemperatureC.OriginalSource == MetadataSource.LibraryPath ||
        frame.Offset.OriginalSource == MetadataSource.LibraryPath;

    private static void SameText(MatchCandidate result, string label, string? left, string? right, int points, bool required)
    {
        if (left is null || right is null) { Missing(result, label, required); return; }
        if (Normalize(left) == Normalize(right)) Pass(result, label, points); else Fail(result, label, left, right);
    }
    private static void Same<T>(MatchCandidate result, string label, T? left, T? right, int points, bool required) where T : struct
    {
        if (!left.HasValue || !right.HasValue) { Missing(result, label, required); return; }
        if (EqualityComparer<T>.Default.Equals(left.Value, right.Value)) Pass(result, label, points); else Fail(result, label, left.Value, right.Value);
    }
    private static void Within(MatchCandidate result, string label, double? left, double? right, double tolerance, int points, bool required)
    {
        if (!left.HasValue || !right.HasValue) { Missing(result, label, required); return; }
        if (Math.Abs(left.Value - right.Value) <= tolerance) Pass(result, label, points); else Fail(result, label, left.Value, right.Value);
        if (left is not null && right is not null && Math.Abs(left.Value - right.Value) > 1e-9 && result.Compatible) result.Exact = false;
    }
    private static void Relative(MatchCandidate result, string label, double? left, double? right, double tolerance, int points)
    {
        if (!left.HasValue || !right.HasValue) { Missing(result, label, false); return; }
        if (Math.Abs(left.Value - right.Value) <= Math.Abs(left.Value) * tolerance) Pass(result, label, points); else Fail(result, label, left.Value, right.Value);
        if (left is not null && right is not null && Math.Abs(left.Value - right.Value) > 1e-9 && result.Compatible) result.Exact = false;
    }
    private static void Missing(MatchCandidate result, string label, bool required)
    {
        result.Exact = false;
        result.Reasons.Add($"{label}: dato incompleto");
        if (required) result.MissingRequired.Add(label);
    }
    private static void Pass(MatchCandidate result, string label, int points) { result.Score += points; result.Reasons.Add($"{label}: compatibile"); }
    private static void Fail<T>(MatchCandidate result, string label, T left, T right) { result.Compatible = false; result.Reasons.Add($"{label}: incompatibile ({left} vs {right})"); }
    private static string Normalize(string value) => string.Join(' ', value.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
