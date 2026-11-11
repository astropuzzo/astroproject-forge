namespace AstroForge.Core.IO;

/// <summary>Filesystem path equality must follow the host OS, not metadata rules.</summary>
public static class PathIdentity
{
    public static StringComparer Comparer { get; } = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static StringComparison Comparison { get; } = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static bool Equals(string? left, string? right) => string.Equals(left, right, Comparison);

    public static bool IsWithin(string candidate, string parent)
    {
        candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        // A drive root keeps its separator ("E:\"): adding another one would make it contain nothing.
        var prefix = Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar;
        return Equals(candidate, parent) || candidate.StartsWith(prefix, Comparison);
    }
}
