using AstroForge.Core.Demo;
using AstroForge.Core.Models;

// Usage: dotnet run --project dotnet/AstroForge.DemoData -- [cartella di destinazione]
var destination = args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "AstroForge-Demo-CygnusLoop");
if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
{
    Console.Error.WriteLine($"La cartella {destination} non è vuota: scegline una nuova.");
    return 1;
}
var dataset = await DemoDatasetGenerator.GenerateAsync(destination);
var bytes = dataset.Frames.Sum(frame => new FileInfo(dataset.PathOf(frame)).Length);
Console.WriteLine($"Progetto demo {DemoDatasetGenerator.Target} creato in {dataset.Root}");
foreach (var group in dataset.Frames.GroupBy(frame => (frame.Software, frame.Kind)))
    Console.WriteLine($"  {group.Key.Software,-8} {group.Key.Kind,-5} {group.Count(),3} file");
Console.WriteLine($"  Totale {dataset.Frames.Count} file, {bytes / 1024d / 1024d:0.0} MB");
Console.WriteLine($"  Nella ruota N.I.N.A. '{DemoDatasetGenerator.CustomFilterName}' è il filtro SII ({DemoDatasetGenerator.CustomFilterCatalogId}).");
return dataset.Frames.Any(frame => frame.Kind == FrameKind.Light) ? 0 : 1;
