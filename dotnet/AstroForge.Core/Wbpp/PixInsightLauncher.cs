using System.Diagnostics;

namespace AstroForge.Core.Wbpp;

public static class PixInsightLauncher
{
    public static void Open(string instancePath)
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { @"C:\Program Files\PixInsight\bin\PixInsight.exe", @"C:\Program Files\PixInsight\PixInsight.exe" }
            : OperatingSystem.IsMacOS()
                ? new[] { "/Applications/PixInsight/PixInsight.app/Contents/MacOS/PixInsight", "/Applications/PixInsight.app/Contents/MacOS/PixInsight" }
                : new[] { "/opt/PixInsight/bin/PixInsight", "/usr/local/PixInsight/bin/PixInsight" };
        var executable = candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("PixInsight not found. Open the generated XPSM in PixInsight, then apply the Script instance globally.", instancePath);
        var launcher = WbppInstanceGenerator.LauncherPath(instancePath);
        if (!File.Exists(launcher)) throw new FileNotFoundException("WBPP launcher not found. Generate the instance again.", launcher);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        var running = Process.GetProcessesByName("PixInsight");
        try { start.ArgumentList.Add((running.Length > 0 ? "--execute=" : "--run=") + Path.GetFullPath(launcher)); }
        finally { foreach (var process in running) process.Dispose(); }
        Process.Start(start);
    }
}
