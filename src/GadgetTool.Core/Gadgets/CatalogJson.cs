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
                Texts(g["superEffect"], defaultLanguage, $"gadget '{id}' Super effect"),
                Text(g, "base") is "kit" or "mini" ? Text(g, "base") : null));
        }

        // Optional: the game's category colours ({ "id", "color" }), written by the extractor.
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var category in (root["categories"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (Text(category, "id") is { } categoryId && Text(category, "color") is { } color
                && System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$"))
                colors[categoryId] = color;
        }

        // Optional: the plate levels and where things go on their pictures, written by the extractor.
        var plates = new List<PlateDefinition>();
        foreach (var plate in (root["plates"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var image = Text(plate, "image");
            if (Text(plate, "id") is not { } plateId || image is null
                || !System.Text.RegularExpressions.Regex.IsMatch(image, "^ui/[A-Za-z0-9_.-]+\\.png$") || image.Contains("..", StringComparison.Ordinal)
                || plate["slots"] is not JsonValue s || !s.TryGetValue(out int slots))
                throw new FormatException("A plate has no id, slot count or picture (ui/<name>.png).");
            var super = plate["superSlot"] is JsonValue b && b.TryGetValue(out bool sp) && sp;
            plates.Add(new PlateDefinition(plateId, slots, super, image, Texts(plate["name"], defaultLanguage, $"plate '{plateId}' name")));
        }
        var geometry = root["plateGeometry"] is JsonObject geo ? ReadGeometry(geo) : null;

        try
        {
            return new LocalizedCatalog(gadgets, Text(root, "source") ?? "", GameVersion.Parse(Text(root, "gameVersion")), defaultLanguage, colors)
            {
                Plates = plates,
                PlateGeometry = geometry,
            };
        }
        catch (ArgumentException e)
        {
            throw new FormatException(e.Message, e);
        }
    }

    private static PlateGeometry ReadGeometry(JsonObject geo)
    {
        int One(string name) => geo[name] is JsonValue v && v.TryGetValue(out int i) && i >= 0 ? i
            : throw new FormatException($"The plate geometry has no '{name}'.");
        int[] Pair(string name, int count) => geo[name] is JsonArray a && a.Count == count
            && a.All(x => x is JsonValue v && v.TryGetValue(out int i) && i >= 0)
            ? [.. a.Select(x => x!.GetValue<int>())]
            : throw new FormatException($"The plate geometry's '{name}' is not {count} numbers.");
        var first = Pair("first", 2);
        var step = Pair("step", 2);
        var super = Pair("super", 2);
        var title = Pair("title", 4);
        return new PlateGeometry(One("width"), One("height"), One("cell"), first[0], first[1], step[0], step[1],
            super[0], super[1], title[0], title[1], title[2], title[3]);
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
