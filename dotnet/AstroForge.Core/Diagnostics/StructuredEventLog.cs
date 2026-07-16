using System.Text.Json;
using System.Text.RegularExpressions;

namespace AstroForge.Core.Diagnostics;

public sealed record StructuredLogEvent(DateTimeOffset Timestamp, string Level, string Code, string Message, string? ExceptionType = null);

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
        ? Directory.EnumerateFiles(_directory, "events*.jsonl").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()
        : [];

    public void Write(string level, string code, string message, Exception? exception = null)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            RotateIfNeeded();
            var safeMessage = AstroFile.Replace(WindowsPath.Replace(message, "[PATH]"), "[ASTRO-FILE]");
            var entry = new StructuredLogEvent(DateTimeOffset.UtcNow, level, code, safeMessage, exception?.GetType().FullName);
            File.AppendAllText(CurrentPath, JsonSerializer.Serialize(entry, _json) + Environment.NewLine);
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
