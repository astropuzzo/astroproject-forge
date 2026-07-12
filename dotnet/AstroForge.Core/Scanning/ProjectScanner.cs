using System.Collections.Concurrent;
using AstroForge.Core.Models;
using AstroForge.Core.Parsing;
using AstroForge.Core.Sessions;

namespace AstroForge.Core.Scanning;

public sealed record ScanProgress(int Completed, int Total, string CurrentFile);

public sealed class ProjectScanner
{
    private static readonly HashSet<string> Extensions = [".fit", ".fits", ".fts", ".xisf"];
    public int LastCacheHits { get; private set; }
    public int LastParsedFiles { get; private set; }

    public async Task<IReadOnlyList<FrameMetadata>> ScanAsync(
        IEnumerable<string> roots,
        SessionSettings sessionSettings,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default,
        IHeaderCache? cache = null)
    {
        var files = roots.SelectMany(Enumerate).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var frames = new ConcurrentBag<FrameMetadata>();
        var completed = 0;
        var cacheHits = 0;
        var parsedFiles = 0;
        await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2), CancellationToken = cancellationToken }, async (path, token) =>
        {
            try
            {
                var info = new FileInfo(path);
                Dictionary<string, object?> headers;
                if (cache?.TryGet(path, info.Length, info.LastWriteTimeUtc.Ticks, out headers!) == true)
                    Interlocked.Increment(ref cacheHits);
                else
                {
                    headers = path.EndsWith(".xisf", StringComparison.OrdinalIgnoreCase)
                        ? await XisfHeaderReader.ReadAsync(path, token)
                        : await FitsHeaderReader.ReadAsync(path, token);
                    cache?.Put(path, info.Length, info.LastWriteTimeUtc.Ticks, headers);
                    Interlocked.Increment(ref parsedFiles);
                }
                frames.Add(FrameClassifier.Classify(path, headers, sessionSettings));
            }
            catch (Exception exception)
            {
                var frame = new FrameMetadata { Path = path, Kind = FrameKind.Unknown };
                frame.Issues.Add(new("image.unreadable", IssueSeverity.Error, exception.Message));
                frames.Add(frame);
            }
            var done = Interlocked.Increment(ref completed);
            progress?.Report(new(done, files.Length, System.IO.Path.GetFileName(path)));
        });
        if (cache is not null) await cache.SaveAsync(cancellationToken);
        LastCacheHits = cacheHits;
        LastParsedFiles = parsedFiles;
        return frames.OrderBy(frame => frame.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> Enumerate(string root)
    {
        if (File.Exists(root) && Extensions.Contains(System.IO.Path.GetExtension(root).ToLowerInvariant())) return [System.IO.Path.GetFullPath(root)];
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(path => Extensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant()));
    }
}
