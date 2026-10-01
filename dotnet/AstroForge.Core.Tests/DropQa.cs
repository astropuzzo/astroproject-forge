using AstroForge.Core.IO;

internal static class DropQa
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "AstroForge-drop-" + Guid.NewGuid().ToString("N"));
        try
        {
            var night = Directory.CreateDirectory(Path.Combine(root, "Cygnus", "2026-08-14")).FullName;
            var cygnus = Path.GetDirectoryName(night)!;
            var masters = Directory.CreateDirectory(Path.Combine(root, "Masters")).FullName;
            string Touch(string folder, string name) { var path = Path.Combine(folder, name); File.WriteAllText(path, "x"); return path; }
            var light = Touch(night, "light-001.fits");
            var xisf = Touch(masters, "masterDark.XISF");
            var loose = Touch(root, "notes.txt");
            var missing = Path.Combine(root, "gone.fits");

            // Captures: a folder is linked whole, an image is linked as itself, the rest is left out and counted.
            var plan = DroppedItems.Plan([cygnus, light, xisf, loose, missing], DropTarget.Captures);
            Assert(plan.Folders.SequenceEqual([cygnus]) && plan.Files.SequenceEqual([xisf]) && plan.Ignored.Count == 2 && plan.Accepted == 2,
                $"Il drop di cartelle e file non è stato smistato: {string.Join(" | ", plan.Paths)}.");
            Assert(!plan.Paths.Contains(light), "Un file dentro una cartella già trascinata non va collegato due volte.");

            // The same folder dragged twice, with a trailing separator or another case, is one folder.
            var twice = DroppedItems.Plan([cygnus, cygnus + Path.DirectorySeparatorChar, cygnus.ToUpperInvariant()], DropTarget.Captures);
            Assert(twice.Folders.Count == (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? 1 : 2), "La stessa cartella trascinata due volte deve contare una volta.");

            // A folder and one of its subfolders: the parent already brings the other.
            var nested = DroppedItems.Plan([night, cygnus], DropTarget.Captures);
            Assert(nested.Folders.SequenceEqual([cygnus]), "Una sottocartella di una cartella già trascinata non va aggiunta.");

            // Master Library: a folder is a library; a Master file stands for the folder it lives in; a capture folder dropped next to it is not mixed in.
            var library = DroppedItems.Plan([masters, xisf], DropTarget.MasterLibrary);
            Assert(library.Folders.SequenceEqual([masters]) && library.Files.Count == 0, "In Libreria Master un file vale la cartella che lo contiene.");
            var fromFile = DroppedItems.Plan([xisf], DropTarget.MasterLibrary);
            Assert(fromFile.Folders.SequenceEqual([masters]), "Un solo Master trascinato deve collegare la sua cartella.");

            // Nothing usable: nothing is linked, and the person is told why.
            var nothing = DroppedItems.Plan([loose, missing, ""], DropTarget.Captures);
            Assert(nothing.Accepted == 0 && nothing.Ignored.Count == 2, "Un drop senza immagini non deve collegare niente.");
            Assert(DroppedItems.IsImage("a.FITS") && DroppedItems.IsImage("a.fts") && DroppedItems.IsImage("a.xisf") && !DroppedItems.IsImage("a.jpg") && !DroppedItems.IsImage("a.fits.txt"), "Le estensioni accettate sono FITS e XISF.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }

        Console.WriteLine("PASS: trascinamento di cartelle e file (smistamento, doppioni, sottocartelle, Libreria Master) verificato.");
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
