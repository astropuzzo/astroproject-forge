namespace AstroForge.Core.IO;

/// <summary>Where a dragged selection is going: the captures of the project or the Master Library.</summary>
public enum DropTarget { Captures, MasterLibrary }

/// <summary>What a drop turns into: the folders and files to link, and what was left out (and why it matters to say so).</summary>
public sealed record DropPlan(DropTarget Target, IReadOnlyList<string> Folders, IReadOnlyList<string> Files, IReadOnlyList<string> Ignored)
{
    public int Accepted => Folders.Count + Files.Count;
    public IEnumerable<string> Paths => Folders.Concat(Files);
}

/// <summary>
/// Sorts whatever was dragged onto the window. Folders are linked as they are; a single FITS or XISF file is a capture,
/// and in the Master Library it stands for the folder it lives in. Anything else is left out, never guessed at.
/// </summary>
public static class DroppedItems
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".fit", ".fits", ".fts", ".xisf" };

    public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path));

    public static DropPlan Plan(IEnumerable<string> dropped, DropTarget target)
    {
        var folders = new List<string>();
        var files = new List<string>();
        var ignored = new List<string>();
        foreach (var raw in dropped.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            string path;
            try { path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raw)); }
            catch (Exception) { ignored.Add(raw); continue; }

            if (Directory.Exists(path)) folders.Add(path);
            else if (File.Exists(path) && IsImage(path))
            {
                // A Master Library is a folder: a dropped Master means "the folder this one is in".
                if (target == DropTarget.MasterLibrary && Path.GetDirectoryName(path) is { Length: > 0 } parent) folders.Add(parent);
                else files.Add(path);
            }
            else ignored.Add(path);
        }

        // One entry per place: no repeats, and nothing inside something that is already coming in whole.
        var uniqueFolders = folders.Distinct(PathIdentity.Comparer).ToList();
        var wholeFolders = uniqueFolders.Where(folder => !uniqueFolders.Any(other => !PathIdentity.Equals(other, folder) && PathIdentity.IsWithin(folder, other))).ToList();
        var looseFiles = files.Distinct(PathIdentity.Comparer)
            .Where(file => !wholeFolders.Any(folder => PathIdentity.IsWithin(file, folder))).ToList();
        return new DropPlan(target, wholeFolders, looseFiles, ignored);
    }
}
