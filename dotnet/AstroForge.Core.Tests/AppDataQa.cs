using AstroForge.Core.IO;

internal static class AppDataQa
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AstroForge-QA-AppData-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            // A regular file blocks directory creation even for root, unlike chmod.
            var blocker = Path.Combine(root, "not-a-directory");
            File.WriteAllText(blocker, "");
            var writable = Path.Combine(root, "missing", "share");

            var resolved = AppDataPaths.ResolveRoot(["", null, "relative/share", Path.Combine(blocker, "share"), writable]);
            Assert(resolved == Path.Combine(writable, AppDataPaths.AppFolderName), $"Cartella dati scelta male: {resolved}.");
            Assert(Directory.Exists(resolved) && !Directory.EnumerateFileSystemEntries(resolved).Any(), "La cartella dati deve essere creata e restare pulita.");

            var fallback = AppDataPaths.ResolveRoot(["", "relative", Path.Combine(blocker, "share")]);
            Assert(Path.IsPathFullyQualified(fallback) && PathIdentity.IsWithin(fallback, Path.GetTempPath()), $"Senza candidati validi serve un fallback assoluto nella temp: {fallback}.");
            Assert(Path.IsPathFullyQualified(AppDataPaths.Root), $"La cartella dati predefinita non deve mai essere relativa: {AppDataPaths.Root}.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
