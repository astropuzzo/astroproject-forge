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
        // Enumeration already carries size and write time (on Windows for free from the directory listing): no stat per file.
        var files = roots.SelectMany(Enumerate).DistinctBy(file => file.FullName, StringComparer.OrdinalIgnoreCase).ToArray();
        var frames = new FrameMetadata[files.Length];
        var completed = 0;
        var cacheHits = 0;
        var parsedFiles = 0;
        // Thousands of files would flood the UI thread with one report each: report about every 0.5%, always the last one.
        var progressStep = Math.Max(1, files.Length / 200);
        // Header reads are small and synchronous: the bounded parallel loop keeps a fixed number of workers busy without
        // paying the async overhead per file.
        await Parallel.ForEachAsync(Enumerable.Range(0, files.Length), new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2), CancellationToken = cancellationToken }, (index, token) =>
        {
            var info = files[index];
            var path = info.FullName;
            try
            {
                Dictionary<string, object?> headers;
                if (cache?.TryGet(path, info.Length, info.LastWriteTimeUtc.Ticks, out headers!) == true)
                    Interlocked.Increment(ref cacheHits);
                else
                {
                    headers = path.EndsWith(".xisf", StringComparison.OrdinalIgnoreCase)
                        ? XisfHeaderReader.Read(path, token)
                        : FitsHeaderReader.Read(path, token);
                    cache?.Put(path, info.Length, info.LastWriteTimeUtc.Ticks, headers);
                    Interlocked.Increment(ref parsedFiles);
                }
                frames[index] = FrameClassifier.Classify(path, headers, sessionSettings);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var frame = new FrameMetadata { Path = path, Kind = FrameKind.Unknown };
                frame.Issues.Add(new("image.unreadable", IssueSeverity.Error, exception.Message));
                frames[index] = frame;
            }
            var done = Interlocked.Increment(ref completed);
            if (done % progressStep == 0 || done == files.Length)
                progress?.Report(new(done, files.Length, info.Name));
            return ValueTask.CompletedTask;
        });
        if (cache is not null) await cache.SaveAsync(cancellationToken);
        LastCacheHits = cacheHits;
        LastParsedFiles = parsedFiles;
        Array.Sort(frames, (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path));
        return frames;
    }

    private static IEnumerable<FileInfo> Enumerate(string root)
    {
        if (File.Exists(root) && Extensions.Contains(System.IO.Path.GetExtension(root).ToLowerInvariant())) return [new FileInfo(System.IO.Path.GetFullPath(root))];
        if (!Directory.Exists(root)) return [];
        return new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories).Where(file => Extensions.Contains(file.Extension.ToLowerInvariant()));
    }
}
