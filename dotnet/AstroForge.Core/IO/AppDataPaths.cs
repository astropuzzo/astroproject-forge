namespace AstroForge.Core.IO;

/// <summary>
/// Per-user folder for state, caches, logs and downloads. Never resolves to a relative path:
/// on Linux GetFolderPath returns "" when ~/.local/share is missing, which used to put state in the cwd.
/// </summary>
public static class AppDataPaths
{
    public const string AppFolderName = "AstroProjectForge";

    /// <summary>Environment variable that points the app at another data folder (portable installs, tests, screenshots).</summary>
    public const string DataFolderVariable = "ASTROFORGE_DATA_DIR";

    private static readonly Lazy<string> LazyRoot = new(DefaultRoot);

    public static string Root => LazyRoot.Value;

    // The override names the data folder itself, not a base the app folder goes under.
    private static string DefaultRoot()
    {
        var folder = Environment.GetEnvironmentVariable(DataFolderVariable);
        return !string.IsNullOrWhiteSpace(folder) && Path.IsPathFullyQualified(folder) && IsWritableDirectory(folder)
            ? Path.TrimEndingDirectorySeparator(folder)
            : ResolveRoot(DefaultCandidates());
    }

    public static string Combine(params string[] parts) => Path.Combine([Root, .. parts]);

    /// <summary>Returns the app folder under the first candidate base that is absolute and writable.</summary>
    public static string ResolveRoot(IEnumerable<string?> baseCandidates)
    {
        foreach (var candidate in baseCandidates)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathFullyQualified(candidate)) continue;
            var root = Path.Combine(candidate, AppFolderName);
            if (IsWritableDirectory(root)) return root;
        }
        // Last resort: still absolute, so nothing ever lands next to the executable or in the cwd.
        return Path.Combine(Path.GetTempPath(), $"{AppFolderName}-{SafeUserName()}");
    }

    private static IEnumerable<string?> DefaultCandidates()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        if (OperatingSystem.IsWindows()) yield break;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(home) || !Path.IsPathFullyQualified(home)) yield break;
        yield return OperatingSystem.IsMacOS()
            ? Path.Combine(home, "Library", "Application Support")
            : Path.Combine(home, ".local", "share");
        yield return Path.Combine(home, ".cache");
    }

    private static bool IsWritableDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static string SafeUserName()
    {
        var name = Environment.UserName;
        var safe = new string(name.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_').ToArray());
        return string.IsNullOrEmpty(safe) ? "user" : safe;
    }
}
