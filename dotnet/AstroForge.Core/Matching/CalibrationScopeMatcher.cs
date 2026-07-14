using AstroForge.Core.Models;

namespace AstroForge.Core.Matching;

public static class CalibrationScopeMatcher
{
    public static bool Matches(FrameMetadata reference, FrameMetadata candidate, FrameKind calibrationKind) =>
        candidate.Kind == FrameKind.Light &&
        SameText(reference.Camera.Value, candidate.Camera.Value) &&
        SameNumber(reference.Gain.Value, candidate.Gain.Value) &&
        SameNumber(reference.Offset.Value, candidate.Offset.Value) &&
        Within(reference.EffectiveTemperatureC, candidate.EffectiveTemperatureC, 2.0) &&
        reference.XBin.Value == candidate.XBin.Value && reference.YBin.Value == candidate.YBin.Value &&
        reference.Width.Value == candidate.Width.Value && reference.Height.Value == candidate.Height.Value &&
        SameText(reference.ReadoutMode.Value, candidate.ReadoutMode.Value) &&
        (calibrationKind != FrameKind.Dark || Within(reference.ExposureSeconds.Value, candidate.ExposureSeconds.Value, 0.5));

    private static bool SameNumber(double? left, double? right) =>
        left.HasValue == right.HasValue && (!left.HasValue || Math.Abs(left.Value - right!.Value) < 0.001);

    private static bool Within(double? left, double? right, double tolerance) =>
        left.HasValue == right.HasValue && (!left.HasValue || Math.Abs(left.Value - right!.Value) <= tolerance);

    private static bool SameText(string? left, string? right) => Normalize(left) == Normalize(right);
    private static string Normalize(string? value) => string.Join(' ', (value ?? "").ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
