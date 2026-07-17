using System.Text.Json;
using System.Text.RegularExpressions;

namespace AstroForge.Core.Diagnostics;

public sealed record StructuredLogEvent(
    DateTimeOffset Timestamp,
    string Level,
    string Code,
    string Message,
    string? ExceptionType = null,
    string? OperationId = null,
    string? Operation = null);

public sealed class StructuredEventLog
{
    private static readonly Regex WindowsPath = new(@"(?i)(?:[a-z]:\\|\\\\)[^\""\r\n]+", RegexOptions.Compiled);
    private static readonly Regex AstroFile = new(@"(?i)\b[^\s\""']+\.(?:fit|fits|fts|xisf)\b", RegexOptions.Compiled);
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly long _maximumBytes;
    private readonly int _retainedFiles;
    private readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public StructuredEventLog(string? directory = null, long maximumBytes = 1024 * 1024, int retainedFiles = 5)
    {
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AstroProjectForge", "Logs");
        _maximumBytes = Math.Max(4096, maximumBytes);
        _retainedFiles = Math.Max(1, retainedFiles);
    }

    public IReadOnlyList<string> Files => Directory.Exists(_directory)
        ? Directory.EnumerateFiles(_directory, "events*.jsonl").OrderBy(File.GetLastWriteTimeUtc).ThenBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()
        : [];

    public StructuredLogOperation BeginOperation(string operation, string startCode, string message, string? operationId = null)
    {
        var id = string.IsNullOrWhiteSpace(operationId) ? Guid.NewGuid().ToString("N") : operationId;
        Write("Information", startCode, message, operationId: id, operation: operation);
        return new(this, id, operation);
    }

    public void Write(string level, string code, string message, Exception? exception = null, string? operationId = null, string? operation = null)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                RotateIfNeeded();
                var safeMessage = AstroFile.Replace(WindowsPath.Replace(message, "[PATH]"), "[ASTRO-FILE]");
                var entry = new StructuredLogEvent(DateTimeOffset.UtcNow, level, code, safeMessage, exception?.GetType().FullName, operationId, operation);
                File.AppendAllText(CurrentPath, JsonSerializer.Serialize(entry, _json) + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public IReadOnlyList<StructuredLogEvent> ReadRecent(int maximumEvents = 250)
    {
        if (maximumEvents <= 0) return [];
        lock (_gate)
        {
            var events = new List<StructuredLogEvent>();
            foreach (var path in Files)
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    while (reader.ReadLine() is { } line)
                    {
                        try
                        {
                            var item = JsonSerializer.Deserialize<StructuredLogEvent>(line, _json);
                            if (item is not null) events.Add(item);
                        }
                        catch (JsonException) { }
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return events.OrderByDescending(item => item.Timestamp).Take(maximumEvents).ToArray();
        }
    }

    private string CurrentPath => Path.Combine(_directory, "events.jsonl");

    private void RotateIfNeeded()
    {
        if (!File.Exists(CurrentPath) || new FileInfo(CurrentPath).Length < _maximumBytes) return;
        var oldest = Path.Combine(_directory, $"events.{_retainedFiles}.jsonl");
        if (File.Exists(oldest)) File.Delete(oldest);
        for (var index = _retainedFiles - 1; index >= 1; index--)
        {
            var source = Path.Combine(_directory, $"events.{index}.jsonl");
            if (File.Exists(source)) File.Move(source, Path.Combine(_directory, $"events.{index + 1}.jsonl"), true);
        }
        File.Move(CurrentPath, Path.Combine(_directory, "events.1.jsonl"), true);
    }
}

public sealed class StructuredLogOperation : IDisposable
{
    private readonly StructuredEventLog _log;
    private int _finished;

    internal StructuredLogOperation(StructuredEventLog log, string id, string operation)
    {
        _log = log;
        Id = id;
        Operation = operation;
    }

    public string Id { get; }
    public string Operation { get; }

    public void Complete(string code, string message)
    {
        if (Interlocked.Exchange(ref _finished, 1) != 0) return;
        _log.Write("Information", code, message, operationId: Id, operation: Operation);
    }

    public void Fail(string code, string message, Exception exception)
    {
        if (Interlocked.Exchange(ref _finished, 1) != 0) return;
        _log.Write("Error", code, message, exception, Id, Operation);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _finished, 1) != 0) return;
        _log.Write("Warning", "AF-OP-ABANDONED", "Operazione terminata senza esito registrato", operationId: Id, operation: Operation);
    }
}
