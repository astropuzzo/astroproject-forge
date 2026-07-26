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
            "Aggiungi solo le righe mostrate sopra. Imposta Pre su ON e Post su OFF.",
            "WBPP separa già filtro, binning ed esposizione: non aggiungerli.",
            "Non usare DATE-OBS: dividerebbe i file della stessa notte osservativa."
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
        if (flatChoices.Values.Any(values => values.Count > 1)) recommendations.Add(new("FLATSET", true, false, "Separa i Light che usano Flat diversi."));
        if (darkChoices.Values.Any(values => values.Count > 1)) recommendations.Add(new("DARKSET", true, false, "Separa i Light che richiedono Dark diversi."));
        if (biasChoices.Values.Any(values => values.Count > 1)) recommendations.Add(new("BIASSET", true, false, "Separa i gruppi che richiedono Bias diversi."));
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
