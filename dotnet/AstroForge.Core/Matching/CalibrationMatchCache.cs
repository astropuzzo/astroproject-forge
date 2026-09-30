using AstroForge.Core.Models;

namespace AstroForge.Core.Matching;

/// <summary>
/// Matches many targets against one set of calibration frames, evaluating each distinct target signature once:
/// thousands of lights of a project share a handful of camera, gain, exposure and temperature combinations.
/// Targets with the same signature receive the same <see cref="MatchResult"/> instance, so callers must not modify it.
/// </summary>
public sealed class CalibrationMatchCache(IEnumerable<FrameMetadata> frames, FrameKind requestedKind, CalibrationPolicy? policy = null)
{
    private readonly FrameMetadata[] _frames = frames.Where(frame => frame.Kind == requestedKind).ToArray();
    private readonly CalibrationPolicy _policy = policy ?? new();
    private readonly Dictionary<CalibrationMatcher.TargetSignature, MatchResult> _results = [];

    public MatchResult Find(FrameMetadata target)
    {
        var signature = CalibrationMatcher.Signature(target, requestedKind);
        if (!_results.TryGetValue(signature, out var result))
            _results[signature] = result = CalibrationMatcher.Find(target, _frames, requestedKind, _policy);
        return result;
    }
}
