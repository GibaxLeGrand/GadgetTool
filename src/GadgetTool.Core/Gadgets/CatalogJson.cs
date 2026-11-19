using System.Text.Json;
using System.Text.Json.Nodes;

namespace GadgetTool.Core.Gadgets;

/// <summary>
/// The gadget list as a JSON file: what the extractor writes for a game version (texts in
/// every language the game has), the made-up list in <c>data/dev</c>, and local test lists.
/// A text is either a string (in <c>defaultLanguage</c>) or an object by language code.
/// See docs/DATA.md.
/// </summary>
/// <example>
/// <code>
/// { "format": "gadgettool.catalog", "version": 1, "source": "...", "gameVersion": "2.0.0",
///   "defaultLanguage": "en",
///   "gadgets": [ { "id": "...", "slotCost": 1, "categoryId": "ring_gain",
///                  "name": { "en": "Ring Magnet", "fr": "Aimant à anneaux" },
///                  "category": { "en": "Rings", "fr": "Anneaux" },
///                  "description": { "en": "...", "fr": "..." } } ] }
/// </code>
/// </example>
public static class CatalogJson
{
    public const string FormatName = "gadgettool.catalog";
    public const int CurrentVersion = 1;

    /// <exception cref="FormatException">Not a catalog file this version can read.</exception>
    public static LocalizedCatalog Parse(string json)
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

        var defaultLanguage = Text(root, "defaultLanguage") ?? "en";
        var gadgets = new List<LocalizedGadget>(list.Count);
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is not JsonObject g)
                throw new FormatException($"Gadget {i + 1} is not an object.");
            var id = Text(g, "id") ?? throw new FormatException($"Gadget {i + 1} has no id.");
            if (g["slotCost"] is not JsonValue c || !c.TryGetValue(out int cost))
                throw new FormatException($"Gadget '{id}' has no slot cost.");

            var category = Texts(g["category"], defaultLanguage, $"gadget '{id}' category");
            var categoryId = Text(g, "categoryId") ?? category.Get(defaultLanguage, defaultLanguage) ?? "";
            gadgets.Add(new LocalizedGadget(
                id, cost,
                Texts(g["name"], defaultLanguage, $"gadget '{id}' name"),
                categoryId,
                category,
                Texts(g["description"], defaultLanguage, $"gadget '{id}' description"),
                Texts(g["superEffect"], defaultLanguage, $"gadget '{id}' Super effect")));
        }

        // Optional: the game's category colours ({ "id", "color" }), written by the extractor.
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var category in (root["categories"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (Text(category, "id") is { } categoryId && Text(category, "color") is { } color
                && System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$"))
                colors[categoryId] = color;
        }

        try
        {
            return new LocalizedCatalog(gadgets, Text(root, "source") ?? "", GameVersion.Parse(Text(root, "gameVersion")), defaultLanguage, colors);
        }
        catch (ArgumentException e)
        {
            throw new FormatException(e.Message, e);
        }
    }

    /// <summary>A string (in the default language) or an object by language code; missing is empty.</summary>
    private static LocalizedText Texts(JsonNode? node, string defaultLanguage, string what) => node switch
    {
        null => LocalizedText.Empty,
        JsonValue v when v.TryGetValue(out string? s) => new LocalizedText(new Dictionary<string, string> { [defaultLanguage] = s }),
        JsonObject o => new LocalizedText(o.ToDictionary(
            p => p.Key,
            p => p.Value is JsonValue t && t.TryGetValue(out string? s) ? s : throw new FormatException($"The {what} in '{p.Key}' is not text."))),
        _ => throw new FormatException($"The {what} is neither text nor texts by language."),
    };

    private static string? Text(JsonObject obj, string name) =>
        obj[name] is JsonValue v && v.TryGetValue(out string? s) ? s : null;
}
