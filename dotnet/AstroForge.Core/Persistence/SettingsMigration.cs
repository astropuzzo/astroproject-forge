using System.Text.Json;
using System.Text.Json.Nodes;

namespace AstroForge.Core.Persistence;

public static class SettingsMigration
{
    public const int CurrentSchema = 2;

    public static string Migrate(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("Impostazioni non valide.");
        var schema = root["SchemaVersion"]?.GetValue<int?>() ?? root["schemaVersion"]?.GetValue<int?>() ?? 1;
        if (schema > CurrentSchema) throw new InvalidDataException($"Schema impostazioni futuro non supportato: {schema}.");
        if (schema < 2)
        {
            root["CheckForUpdates"] ??= false;
            root["UpdateChannel"] ??= "Beta";
        }
        root["SchemaVersion"] = CurrentSchema;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
