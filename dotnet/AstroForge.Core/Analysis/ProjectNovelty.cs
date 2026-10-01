using AstroForge.Core.Models;

namespace AstroForge.Core.Analysis;

/// <summary>One night with data the project did not have before.</summary>
/// <param name="Night">The observing night, as the session id says it.</param>
/// <param name="WholeNight">No Light of this night was known before; otherwise the night grew.</param>
public sealed record NewNight(string Night, int Lights, double IntegrationSeconds, IReadOnlyList<string> Filters, bool WholeNight);

/// <summary>
/// What a project gained since the last time it was exported (or, for a project never exported, since its previous analysis):
/// the Lights and Flats that are not in it yet, which nights they belong to and how much integration they add. Master
/// calibrations are not data: they are never counted.
/// </summary>
public sealed record ProjectNovelty(
    int NewLights,
    int NewFlats,
    double NewIntegrationSeconds,
    long NewBytes,
    IReadOnlyList<NewNight> Nights,
    IReadOnlyList<string> NewFilters,
    IReadOnlySet<string> NewPaths)
{
    public int NewFiles => NewLights + NewFlats;
    public int WholeNights => Nights.Count(night => night.WholeNight);

    /// <summary>The frames of <paramref name="frames"/> that are not in <paramref name="known"/>; null when nothing is new or nothing was known.</summary>
    public static ProjectNovelty? Compute(IEnumerable<FrameMetadata> frames, IReadOnlySet<string> known)
    {
        if (known.Count == 0) return null;
        var data = frames.Where(frame => !frame.IsMaster && frame.Kind is FrameKind.Light or FrameKind.Flat).ToList();
        var fresh = data.Where(frame => !known.Contains(frame.Path)).ToList();
        if (fresh.Count == 0) return null;

        var knownLights = data.Where(frame => frame.Kind == FrameKind.Light && known.Contains(frame.Path)).ToList();
        string Night(FrameMetadata frame) => frame.SessionId.Value ?? "—";
        var knownNights = knownLights.Select(Night).ToHashSet(StringComparer.Ordinal);
        var knownFilters = knownLights.Select(frame => frame.FilterName.Value ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);

        var freshLights = fresh.Where(frame => frame.Kind == FrameKind.Light).ToList();
        var nights = freshLights.GroupBy(Night, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new NewNight(group.Key, group.Count(), group.Sum(frame => frame.ExposureSeconds.Value ?? 0),
                group.Select(frame => frame.FilterName.Value ?? "").Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList(),
                !knownNights.Contains(group.Key)))
            .ToList();
        var filters = freshLights.Select(frame => frame.FilterName.Value ?? "").Where(name => name.Length > 0 && !knownFilters.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        return new(freshLights.Count, fresh.Count - freshLights.Count, freshLights.Sum(frame => frame.ExposureSeconds.Value ?? 0), SizeOf(fresh), nights, filters,
            fresh.Select(frame => frame.Path).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    private static long SizeOf(IEnumerable<FrameMetadata> frames)
    {
        var total = 0L;
        foreach (var frame in frames)
        {
            try { total += new FileInfo(frame.Path).Length; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { /* a file that vanished adds nothing */ }
        }
        return total;
    }
}
