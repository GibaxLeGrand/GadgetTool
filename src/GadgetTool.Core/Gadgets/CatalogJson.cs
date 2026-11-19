using System.Text.Json;
using System.Text.Json.Nodes;

namespace GadgetTool.Core.Gadgets;

/// <summary>
/// A gadget catalog as a JSON file. Used for the made-up catalog in <c>data/dev</c> and for
/// local test catalogs, until the catalog is read from the game's own files.
/// </summary>
/// <example>
/// <code>
/// { "format": "gadgettool.catalog", "version": 1, "source": "Made-up gadgets",
///   "gadgets": [ { "id": "dev.ring_magnet", "name": "Ring Magnet", "slotCost": 1,
///                  "category": "Rings", "description": "..." } ] }
/// </code>
/// </example>
public static class CatalogJson
{
    public const string FormatName = "gadgettool.catalog";
    public const int CurrentVersion = 1;

    /// <exception cref="FormatException">Not a catalog file this version can read.</exception>
    public static GadgetCatalog Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Not valid JSON: {e.Message}", e);
        }

        if (node is not JsonObject root || Text(root, "format") != FormatName)
            throw new FormatException($"Not a GadgetTool catalog (expected \"format\": \"{FormatName}\").");
        if (root["version"] is not JsonValue v || !v.TryGetValue(out int version) || version is < 1 or > CurrentVersion)
            throw new FormatException($"Unknown catalog version; this GadgetTool reads version {CurrentVersion}.");
        if (root["gadgets"] is not JsonArray list)
            throw new FormatException("The catalog has no \"gadgets\" list.");

        var gadgets = new List<GadgetDefinition>(list.Count);
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is not JsonObject g)
                throw new FormatException($"Gadget {i + 1} is not an object.");
            var id = Text(g, "id") ?? throw new FormatException($"Gadget {i + 1} has no id.");
            var name = Text(g, "name") ?? id;
            if (g["slotCost"] is not JsonValue c || !c.TryGetValue(out int cost))
                throw new FormatException($"Gadget '{id}' has no slot cost.");
            gadgets.Add(new GadgetDefinition(id, name, cost, Text(g, "category") ?? "", Text(g, "description")));
        }

        try
        {
            return new GadgetCatalog(gadgets, Text(root, "source") ?? "");
        }
        catch (ArgumentException e)
        {
            throw new FormatException(e.Message, e);
        }
    }

    private static string? Text(JsonObject obj, string name) =>
        obj[name] is JsonValue v && v.TryGetValue(out string? s) ? s : null;
}
