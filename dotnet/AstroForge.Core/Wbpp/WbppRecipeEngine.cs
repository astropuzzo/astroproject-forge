using AstroForge.Core.Analysis;

namespace AstroForge.Core.Wbpp;

public sealed record GroupingKeywordRecommendation(string Keyword, bool Pre, bool Post, string Reason);
public sealed record WbppRecipe(IReadOnlyList<GroupingKeywordRecommendation> Keywords, IReadOnlyList<string> Notes)
{
    public bool Contains(string keyword) => Keywords.Any(item => item.Keyword == keyword);
}

public static class WbppRecipeEngine
{
    public static WbppRecipe Recommend(ProjectAnalysis analysis)
    {
        var recommendations = new List<GroupingKeywordRecommendation>();
        var notes = new List<string>
        {
            "FILTER, binning ed esposizione sono gestiti nativamente da WBPP.",
            "Non usare DATE-OBS: identifica i singoli frame e può attraversare la mezzanotte.",
            "Le notti osservative sono sotto-livelli della sessione di configurazione e non devono separare l'integrazione.",
            "FLATSET rappresenta la sessione ottica; DARKSET e BIASSET rappresentano la configurazione sensore."
        };
        var flatChoices = new Dictionary<string, HashSet<string>>();
        var darkChoices = new Dictionary<string, HashSet<string>>();
        var biasChoices = new Dictionary<string, HashSet<string>>();
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in analysis.Lights)
        {
            var light = item.Light;
            if (light.ObjectName.Value is { Length: > 0 } target) targets.Add(target.Trim());
            Add(flatChoices, $"{light.XBin.Value}|{light.YBin.Value}|{light.Width.Value}|{light.Height.Value}|{light.FilterName.Value}|{light.BayerPattern.Value}", item.FlatGroup?.Id);
            Add(darkChoices, $"{light.XBin.Value}|{light.YBin.Value}|{light.Width.Value}|{light.Height.Value}|{light.ExposureSeconds.Value:0.###}", item.Dark.Selected?.Frame.Path);
            Add(biasChoices, $"{light.XBin.Value}|{light.YBin.Value}|{light.Width.Value}|{light.Height.Value}", item.Bias.Selected?.Frame.Path);
        }
        if (flatChoices.Values.Any(values => values.Count > 1)) recommendations.Add(new("FLATSET", true, false, "Lo stesso filtro usa più set Flat."));
        if (darkChoices.Values.Any(values => values.Count > 1)) recommendations.Add(new("DARKSET", true, false, "Light equivalenti per WBPP richiedono Dark diversi per gain, temperatura o offset."));
        if (biasChoices.Values.Any(values => values.Count > 1)) recommendations.Add(new("BIASSET", true, false, "La stessa geometria richiede Bias diversi per gain o offset."));
        if (targets.Count > 1) recommendations.Add(new("TARGET", false, true, "Target diversi devono restare separati in registrazione e integrazione."));
        if (recommendations.Count == 0) notes.Insert(0, "Lasciare vuota la tabella Grouping Keywords.");
        return new(recommendations, notes);
    }

    private static void Add(Dictionary<string, HashSet<string>> map, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!map.TryGetValue(key, out var values)) map[key] = values = new(StringComparer.OrdinalIgnoreCase);
        values.Add(value);
    }
}
