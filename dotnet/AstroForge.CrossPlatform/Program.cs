using Avalonia;
using System;

namespace AstroForge.CrossPlatform;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // The smoke test and a capture run load the demo project and save state: give them a data folder of their own so they never touch the user's.
        // Each run starts from an empty one, so what an earlier run confirmed or saved never changes what the next one finds.
        if ((args.Contains(MainWindow.CaptureArgument) || args.Contains(MainWindow.SmokeTestArgument))
            && Environment.GetEnvironmentVariable(AstroForge.Core.IO.AppDataPaths.DataFolderVariable) is null)
        {
            var folder = Path.Combine(Path.GetTempPath(), "AstroProjectForge-" + (args.Contains(MainWindow.CaptureArgument) ? "capture" : "smoke"));
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            Environment.SetEnvironmentVariable(AstroForge.Core.IO.AppDataPaths.DataFolderVariable, folder);
        }
        // The smoke test never goes to the network: the sky it checks is served from a stub or is simply not there.
        if (args.Contains(MainWindow.SmokeTestArgument) && Environment.GetEnvironmentVariable(AstroForge.Core.Analysis.SkyImageClient.OfflineVariable) is null)
            Environment.SetEnvironmentVariable(AstroForge.Core.Analysis.SkyImageClient.OfflineVariable, "1");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
