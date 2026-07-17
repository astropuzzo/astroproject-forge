using System.Reflection;

namespace AstroForge.App.Services;

public static class ReleaseIdentity
{
    private static readonly Assembly Assembly = typeof(ReleaseIdentity).Assembly;
    public static string Version => Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public static string Channel => Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(item => item.Key == "ReleaseChannel")?.Value ?? "Beta";
    public static string Display => $"v{Version} · {Channel}";
}
