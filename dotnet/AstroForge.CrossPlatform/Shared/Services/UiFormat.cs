using System.Globalization;

namespace AstroForge.App.Services;

/// <summary>Numbers, sizes and durations written one way everywhere, in the culture of the interface language.</summary>
public static class UiFormat
{
    private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-GB");

    public static bool English { get; private set; } = true;
    public static CultureInfo Culture => English ? EnglishCulture : ItalianCulture;

    public static void SetLanguage(string language) => English = UiLocalization.NormalizeLanguage(language) == UiLocalization.English;

    public static string Number(double value, string format = "0.##") => value.ToString(format, Culture);

    /// <summary>A count of frames by type: "4 Light" in Italian, "4 Lights" in English. "Bias" has no plural.</summary>
    public static string Frames(int count, string kind) => English && count != 1 && kind != "Bias" ? $"{count} {kind}s" : $"{count} {kind}";

    /// <summary>A share already expressed in percent: "100%".</summary>
    public static string Percent(double percent) => $"{Number(percent, "0")}%";

    /// <summary>"45 min", "3 h", "1 h 20 min".</summary>
    public static string Duration(double hours)
    {
        var minutes = (int)Math.Round(Math.Max(0, hours) * 60);
        if (minutes < 60) return $"{minutes} min";
        return minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60:00} min";
    }

    /// <summary>"640 KB", "6,1 MB", "210 GB": one decimal only while it still says something.</summary>
    public static string Size(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{Number(value, unit < 2 || value >= 100 ? "0" : "0.#")} {units[unit]}";
    }
}
