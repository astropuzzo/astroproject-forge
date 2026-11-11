using System.Runtime.InteropServices;

namespace AstroForge.App.Services;

/// <summary>What the operating system says about the person using it, read once for a first run.</summary>
internal static class SystemPreferences
{
    private const uint GetClientAreaAnimation = 0x1042;

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out int value, uint update);

    /// <summary>True when Windows has "Animation effects" turned off. Other systems give no answer through Avalonia, so they start with motion on.</summary>
    public static bool PrefersReducedMotion()
    {
        try
        {
            return OperatingSystem.IsWindows() && SystemParametersInfo(GetClientAreaAnimation, 0, out var animations, 0) && animations == 0;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }
}
