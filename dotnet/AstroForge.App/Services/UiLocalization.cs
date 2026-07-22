using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AstroForge.App.Services;

public static partial class UiLocalization
{
    public const string Italian = "Italiano";
    public const string English = "English";
    private static readonly IReadOnlyDictionary<string, string> ItToEn = Load();
    private static readonly IReadOnlyDictionary<string, string> EnToIt = ItToEn
        .GroupBy(pair => pair.Value, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);

    public static IReadOnlyList<string> Languages { get; } = [Italian, English];

    public static string NormalizeLanguage(string? value) => value == English ? English : Italian;

    public static void ApplyCulture(string language)
    {
        var culture = CultureInfo.GetCultureInfo(NormalizeLanguage(language) == English ? "en-US" : "it-IT");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public static string Translate(string? value, string language)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";
        var english = NormalizeLanguage(language) == English;
        var direct = english ? ItToEn : EnToIt;
        if (direct.TryGetValue(value, out var translated)) return translated;

        var normalized = english && EnToIt.ContainsKey(value) || !english && ItToEn.ContainsKey(value)
            ? value
            : TranslateDynamic(value, english);
        return normalized;
    }

    private static string TranslateDynamic(string value, bool english)
    {
        if (!english) return TranslateDynamicToItalian(value);
        var match = LinkedSources().Match(value);
        if (match.Success) return $"{match.Groups[1].Value} linked source{(match.Groups[1].Value == "1" ? "" : "s")}";
        match = ReadyForWbpp().Match(value);
        if (match.Success) return $"Ready for WBPP · {match.Groups[1].Value} Light frames with Flat, Dark and Bias assigned";
        match = NeedsResolution().Match(value);
        if (match.Success) return $"Review required · {match.Groups[1].Value} missing or ambiguous calibration assignments";
        match = FilesAnalyzed().Match(value);
        if (match.Success) return $"{match.Groups[1].Value} files analyzed · {match.Groups[2].Value} cached · {match.Groups[3].Value} read · {match.Groups[4].Value} warnings";
        match = FileCount().Match(value);
        if (match.Success) return $"{match.Groups[1].Value} files";
        match = WarningCount().Match(value);
        if (match.Success) return $"{match.Groups[1].Value} warnings";
        match = ReviewCount().Match(value);
        if (match.Success) return $"{match.Groups[1].Value} to review";
        match = ModifiedFrames().Match(value);
        if (match.Success) return $"{match.Groups[1].Value} modified frames";
        match = ConfiguredLibraries().Match(value);
        if (match.Success) return $"{match.Groups[1].Value} configured librar{(match.Groups[1].Value == "1" ? "y" : "ies")}";
        match = FolderCount().Match(value);
        if (match.Success) return $"{match.Groups[1].Value} folder{(match.Groups[1].Value == "1" ? "" : "s")}";
        match = Priority().Match(value);
        if (match.Success) return $"Priority {match.Groups[1].Value}";
        match = NightCount().Match(value);
        if (match.Success) return $"{match.Groups[1].Value} night{(match.Groups[1].Value == "1" ? "" : "s")}";
        match = ConfigurationSessionCount().Match(value);
        if (match.Success) return $"{match.Groups[1].Value} configuration session{(match.Groups[1].Value == "1" ? "" : "s")}";
        return value;
    }

    private static string TranslateDynamicToItalian(string value)
    {
        var match = EnglishLinkedSources().Match(value);
        if (match.Success) return $"{match.Groups[1].Value} sorgent{(match.Groups[1].Value == "1" ? "e collegata" : "i collegate")}";
        match = EnglishReadyForWbpp().Match(value);
        if (match.Success) return $"Pronto per WBPP · {match.Groups[1].Value} Light con Flat, Dark e Bias assegnati";
        match = EnglishNeedsResolution().Match(value);
        if (match.Success) return $"Da risolvere · {match.Groups[1].Value} assegnazioni di calibrazione mancanti o ambigue";
        match = EnglishFilesAnalyzed().Match(value);
        if (match.Success) return $"{match.Groups[1].Value} file analizzati · {match.Groups[2].Value} da cache · {match.Groups[3].Value} letti · {match.Groups[4].Value} segnalazioni";
        match = EnglishFileCount().Match(value);
        if (match.Success) return $"FILE {match.Groups[1].Value}";
        match = EnglishWarningCount().Match(value);
        if (match.Success) return $"AVVISI {match.Groups[1].Value}";
        match = EnglishReviewCount().Match(value);
        if (match.Success) return $"{match.Groups[1].Value} da verificare";
        match = EnglishModifiedFrames().Match(value);
        if (match.Success) return $"{match.Groups[1].Value} frame modificati";
        match = EnglishConfiguredLibraries().Match(value);
        if (match.Success) return $"{match.Groups[1].Value} configurate";
        match = EnglishFolderCount().Match(value);
        if (match.Success) return $"{match.Groups[1].Value} cartell{(match.Groups[1].Value == "1" ? "a" : "e")}";
        match = EnglishPriority().Match(value);
        if (match.Success) return $"Priorità {match.Groups[1].Value}";
        match = EnglishNightCount().Match(value);
        if (match.Success) return $"{match.Groups[1].Value} nott{(match.Groups[1].Value == "1" ? "e" : "i")}";
        match = EnglishConfigurationSessionCount().Match(value);
        if (match.Success) return $"{match.Groups[1].Value} session{(match.Groups[1].Value == "1" ? "e" : "i")} configurazione";
        return value;
    }

    private static IReadOnlyDictionary<string, string> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AstroForge.Localization.en.json")
            ?? throw new InvalidOperationException("Embedded localization resource AstroForge.Localization.en.json is missing.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException("English localization resource is invalid.");
    }

    [GeneratedRegex("^(\\d+) sorgent(?:e|i) collegat(?:a|e)(?:.*)?$", RegexOptions.IgnoreCase)] private static partial Regex LinkedSources();
    [GeneratedRegex("^Pronto per WBPP · (\\d+) Light con Flat, Dark e Bias assegnati$")] private static partial Regex ReadyForWbpp();
    [GeneratedRegex("^Da risolvere · (\\d+) assegnazioni di calibrazione mancanti o ambigue$")] private static partial Regex NeedsResolution();
    [GeneratedRegex("^(\\d+) file analizzati · (\\d+) da cache · (\\d+) letti · (\\d+) segnalazioni$")] private static partial Regex FilesAnalyzed();
    [GeneratedRegex("^FILE (\\d+)$")] private static partial Regex FileCount();
    [GeneratedRegex("^AVVISI (\\d+)$")] private static partial Regex WarningCount();
    [GeneratedRegex("^(\\d+) da verificare$")] private static partial Regex ReviewCount();
    [GeneratedRegex("^(\\d+) frame modificati$")] private static partial Regex ModifiedFrames();
    [GeneratedRegex("^(\\d+) configurate$")] private static partial Regex ConfiguredLibraries();
    [GeneratedRegex("^(\\d+) cartell(?:a|e)$")] private static partial Regex FolderCount();
    [GeneratedRegex("^Priorità (\\d+)$")] private static partial Regex Priority();
    [GeneratedRegex("^(\\d+) nott(?:e|i)$")] private static partial Regex NightCount();
    [GeneratedRegex("^(\\d+) session(?:e|i) configurazione$")] private static partial Regex ConfigurationSessionCount();
    [GeneratedRegex("^(\\d+) linked sources?$")] private static partial Regex EnglishLinkedSources();
    [GeneratedRegex("^Ready for WBPP · (\\d+) Light frames with Flat, Dark and Bias assigned$")] private static partial Regex EnglishReadyForWbpp();
    [GeneratedRegex("^Review required · (\\d+) missing or ambiguous calibration assignments$")] private static partial Regex EnglishNeedsResolution();
    [GeneratedRegex("^(\\d+) files analyzed · (\\d+) cached · (\\d+) read · (\\d+) warnings$")] private static partial Regex EnglishFilesAnalyzed();
    [GeneratedRegex("^(\\d+) files$")] private static partial Regex EnglishFileCount();
    [GeneratedRegex("^(\\d+) warnings$")] private static partial Regex EnglishWarningCount();
    [GeneratedRegex("^(\\d+) to review$")] private static partial Regex EnglishReviewCount();
    [GeneratedRegex("^(\\d+) modified frames$")] private static partial Regex EnglishModifiedFrames();
    [GeneratedRegex("^(\\d+) configured librar(?:y|ies)$")] private static partial Regex EnglishConfiguredLibraries();
    [GeneratedRegex("^(\\d+) folders?$")] private static partial Regex EnglishFolderCount();
    [GeneratedRegex("^Priority (\\d+)$")] private static partial Regex EnglishPriority();
    [GeneratedRegex("^(\\d+) nights?$")] private static partial Regex EnglishNightCount();
    [GeneratedRegex("^(\\d+) configuration sessions?$")] private static partial Regex EnglishConfigurationSessionCount();
}
