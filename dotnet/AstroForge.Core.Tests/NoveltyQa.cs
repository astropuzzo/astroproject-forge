using AstroForge.Core.Analysis;
using AstroForge.Core.Export;
using AstroForge.Core.IO;
using AstroForge.Core.Models;
using AstroForge.Core.Parsing;
using System.Text.Json;

internal static class NoveltyQa
{
    public static void Run()
    {
        FrameMetadata Frame(string name, string kind, string filter, string night, bool master = false)
        {
            var frame = FrameClassifier.Classify(Path.Combine(Path.GetTempPath(), "novelty", name),
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["IMAGETYP"] = kind, ["INSTRUME"] = "ZWO ASI2600MM Pro", ["FILTER"] = filter, ["EXPTIME"] = 300.0, ["GAIN"] = 100.0,
                    ["DATE-OBS"] = $"{night}T23:30:00"
                }, new(TimeZoneInfo.Utc, new TimeOnly(12, 0)));
            frame.IsMaster = master;
            return frame;
        }

        var nightOne = new[] { Frame("a1.fits", "Light", "Ha", "2026-08-14"), Frame("a2.fits", "Light", "Ha", "2026-08-14"), Frame("a3.fits", "Flat", "Ha", "2026-08-14") };
        var nightTwo = new[] { Frame("b1.fits", "Light", "OIII", "2026-08-20"), Frame("b2.fits", "Light", "OIII", "2026-08-20"), Frame("b3.fits", "Flat", "OIII", "2026-08-20") };
        var master = Frame("masterDark.fits", "Master Dark", "", "2026-05-01", master: true);
        var known = nightOne.Select(frame => frame.Path).ToHashSet(PathIdentity.Comparer);
        var all = nightOne.Concat(nightTwo).Append(master).ToList();

        // A whole new night, in a filter the project did not have, with its Flat: the Master is not data.
        var news = ProjectNovelty.Compute(all, known);
        Assert(news is { NewLights: 2, NewFlats: 1, NewFiles: 3, WholeNights: 1 } && news.NewIntegrationSeconds == 600 && news.NewFilters.SequenceEqual(["OIII"])
            && news.Nights is [{ Night: "2026-08-20", Lights: 2, WholeNight: true }] && news.NewPaths.Count == 3 && !news.NewPaths.Contains(master.Path),
            $"La notte nuova non è stata riconosciuta: {news}.");

        // More frames on a night the project already has: the night grew, nothing else is new.
        var extra = Frame("a4.fits", "Light", "Ha", "2026-08-14");
        var grown = ProjectNovelty.Compute(nightOne.Append(extra), known);
        Assert(grown is { NewLights: 1, WholeNights: 0 } && grown.NewFilters.Count == 0 && grown.Nights is [{ WholeNight: false }], "Frame in più su una notte esistente: la notte cresce, non è nuova.");

        // Nothing known (never exported, never analysed) or nothing new: no novelty at all.
        Assert(ProjectNovelty.Compute(all, new HashSet<string>()) is null, "Senza una base nota non ci sono novità.");
        Assert(ProjectNovelty.Compute(nightOne, known) is null, "Se tutto è già noto non ci sono novità.");

        // The manifest of an exported project says which source files it already carries.
        var root = Path.Combine(Path.GetTempPath(), "AstroForge-novelty-" + Guid.NewGuid().ToString("N"));
        try
        {
            var control = Path.Combine(root, "Cygnus Loop", "_AstroForge");
            Directory.CreateDirectory(control);
            var files = nightOne.Select((frame, index) => new { source = frame.Path, destination = $"Light/{index}.fits", sha256 = new string('a', 64), bytes = 10 }).ToArray();
            File.WriteAllText(Path.Combine(control, "manifest.json"), JsonSerializer.Serialize(new { application = "AstroProject Forge", project_name = "Cygnus Loop", files }));
            var sources = ProjectExportPreflight.ExportedSources(root, "Cygnus Loop");
            Assert(sources.Count == 3 && nightOne.All(frame => sources.Contains(frame.Path)) && !sources.Contains(nightTwo[0].Path), "Il manifest deve elencare i file sorgente già nel progetto.");
            Assert(ProjectNovelty.Compute(all, sources) is { NewLights: 2, WholeNights: 1 }, "Le novità si calcolano rispetto al manifest.");
            Assert(ProjectExportPreflight.ExportedSources(root, "Altro progetto").Count == 0, "Un progetto con un altro nome non è questo.");
            Assert(ProjectExportPreflight.ExportedSources(Path.Combine(root, "manca"), "Cygnus Loop").Count == 0 && ProjectExportPreflight.ExportedSources("", "x").Count == 0, "Senza cartella non ci sono file esportati.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }

        Console.WriteLine("PASS: novità di un progetto (notti nuove, notti cresciute, filtri nuovi, Master esclusi) e file già nel progetto dal manifest verificati.");
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
