using Avalonia;
using System;

namespace AstroForge.CrossPlatform;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // The smoke test and a capture run load the demo project and save state: give them a data folder of their own so they never touch the user's.
        if ((args.Contains(MainWindow.CaptureArgument) || args.Contains(MainWindow.SmokeTestArgument))
            && Environment.GetEnvironmentVariable(AstroForge.Core.IO.AppDataPaths.DataFolderVariable) is null)
            Environment.SetEnvironmentVariable(AstroForge.Core.IO.AppDataPaths.DataFolderVariable, Path.Combine(Path.GetTempPath(), "AstroProjectForge-" + (args.Contains(MainWindow.CaptureArgument) ? "capture" : "smoke")));
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
