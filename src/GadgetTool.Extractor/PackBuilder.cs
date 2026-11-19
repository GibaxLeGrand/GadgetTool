using System.Text.Json.Nodes;
using GadgetTool.Core.Gadgets;

namespace GadgetTool.Extractor;

/// <summary>
/// The game's gadget tables as CUE4Parse writes them in JSON (the <c>Rows</c> object of a
/// DataTable), once per language: the tables are the same in every language, only the
/// resolved texts (<c>LocalizedString</c>) change.
/// </summary>
/// <param name="DefaultLanguage">The language whose rows give everything but the texts.</param>
/// <param name="GadgetRows">Language → rows of <c>DT_GadgetData</c>.</param>
/// <param name="CategoryRows">Language → rows of <c>DT_GadgetCustomCategoryData</c>.</param>
internal sealed record GameTables(
    string DefaultLanguage,
    IReadOnlyDictionary<string, JsonObject> GadgetRows,
    IReadOnlyDictionary<string, JsonObject> CategoryRows);

/// <summary>What the pack says about where it comes from.</summary>
internal sealed record PackInfo(GameVersion GameVersion, string? SteamBuildId);

/// <summary>The tables break a rule the app relies on; nothing is written.</summary>
internal sealed class PackException(IReadOnlyList<string> problems)
    : Exception("The game's gadget data is not as expected:" + Environment.NewLine + string.Join(Environment.NewLine, problems.Select(p => " - " + p)))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>
/// Turns the game's gadget tables into <c>catalog.json</c> (docs/DATA.md). No file access and
/// no CUE4Parse here: <see cref="GameFiles"/> reads the tables, this decides what goes in.
/// </summary>
/// <remarks>
/// Rules (game 2.0.0, read on 2026-11-19):
/// <list type="bullet">
/// <item>Only rows with <c>bIsEnabled</c>; <c>bInvisibleGadgetCustom</c> rows stay in, as saves use them.</item>
/// <item>The id is <c>ID</c> without <c>EGadgetId::</c>, as the save stores it (the save may
/// use another letter case: Unreal names ignore case). Ids must be unique ignoring case.</item>
/// <item>The slot cost is <c>SizeX</c> (1 to 3); <c>SizeY</c> must be 1.</item>
/// <item>The category id is <c>Category</c> without <c>EGadgetCategory::</c>; categories are
/// ordered by <c>SortID</c>, gadgets by category, then <c>GadgetCustomSortId</c>, then id.</item>
/// <item>A text is kept when <c>LocalizedString</c> is set and is not the string table key
/// (an unresolved key). Every gadget needs a name in the default language.</item>
/// <item>Texts are kept as the game has them: descriptions may hold placeholders filled in by
/// the game (<c>{GetRing}</c>) and rich-text tags (<c>&lt;SpStyle&gt;...&lt;/&gt;</c>).</item>
/// <item>Only categories that hold a gadget are listed.</item>
/// </list>
/// </remarks>
internal static class PackBuilder
{
    public const string GadgetEnumPrefix = "EGadgetId::";
    public const string CategoryEnumPrefix = "EGadgetCategory::";
    public const string PlateEnumPrefix = "EGadgetPlateId::";

    public static JsonObject BuildCatalog(GameTables tables, PackInfo info)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(info);
        if (!tables.GadgetRows.TryGetValue(tables.DefaultLanguage, out var gadgetRows)
            || !tables.CategoryRows.TryGetValue(tables.DefaultLanguage, out var categoryRows))
            throw new PackException([$"No tables in the default language '{tables.DefaultLanguage}'."]);

        var problems = new List<string>();
        var languages = tables.GadgetRows.Keys
            .OrderBy(l => l.Equals(tables.DefaultLanguage, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(l => l, StringComparer.Ordinal)
            .ToList();

        var categories = new List<(string Id, int Sort, JsonObject Json)>();
        foreach (var (rowName, node) in categoryRows)
        {
            var row = node as JsonObject;
            if (row is null)
                continue;
            var id = Strip(rowName, CategoryEnumPrefix);
            var json = new JsonObject
            {
                ["id"] = id,
                ["sort"] = Int(row, "SortID"),
                ["color"] = (row["CategoryColor"] as JsonObject)?["Hex"] is JsonValue hex ? "#" + hex.GetValue<string>() : null,
                ["name"] = Texts(tables.CategoryRows, rowName, "CategoryName", languages),
            };
            categories.Add((id, Int(row, "SortID"), json));
        }
        var categorySort = categories.ToDictionary(c => c.Id, c => c.Sort, StringComparer.Ordinal);

        var gadgets = new List<(string CategoryId, int Sort, string Id, JsonObject Json)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rowName, node) in gadgetRows)
        {
            if (node is not JsonObject row || !Bool(row, "bIsEnabled"))
                continue;

            var id = Strip(Str(row, "ID") ?? rowName, GadgetEnumPrefix);
            if (!seen.Add(id))
                problems.Add($"Gadget id '{id}' appears twice (ignoring case).");

            var sizeX = Int(row, "SizeX");
            var sizeY = Int(row, "SizeY");
            if (sizeX is < 1 or > 3)
                problems.Add($"Gadget '{id}' takes {sizeX} slots; GadgetTool knows 1 to 3.");
            if (sizeY != 1)
                problems.Add($"Gadget '{id}' is {sizeY} rows high; GadgetTool knows 1.");

            var categoryId = Strip(Str(row, "Category") ?? "", CategoryEnumPrefix);
            if (!categorySort.ContainsKey(categoryId))
                problems.Add($"Gadget '{id}' is in category '{categoryId}', which the category table does not have.");

            var name = Texts(tables.GadgetRows, rowName, "Name", languages);
            if (name[tables.DefaultLanguage] is null)
                problems.Add($"Gadget '{id}' has no name in '{tables.DefaultLanguage}'.");

            var json = new JsonObject
            {
                ["id"] = id,
                ["slotCost"] = sizeX,
                ["categoryId"] = categoryId,
                ["name"] = name,
                ["category"] = categories.FirstOrDefault(c => c.Id == categoryId).Json?["name"]?.DeepClone(),
                ["description"] = Texts(tables.GadgetRows, rowName, "Explanation", languages),
                ["superEffect"] = Texts(tables.GadgetRows, rowName, "ExplanationSpecial", languages),
                ["plate"] = Strip(Str(row, "plateId") ?? "", PlateEnumPrefix),
                ["icon"] = AssetPath((row["IconTexture"] as JsonObject)?["AssetPathName"]),
            };
            RemoveEmpty(json);
            gadgets.Add((categoryId, Int(row, "GadgetCustomSortId"), id, json));
        }

        if (problems.Count > 0)
            throw new PackException(problems);

        var ordered = gadgets
            .OrderBy(g => categorySort.GetValueOrDefault(g.CategoryId, int.MaxValue))
            .ThenBy(g => g.Sort)
            .ThenBy(g => g.Id, StringComparer.Ordinal)
            .Select(g => (JsonNode)g.Json);

        var catalog = new JsonObject
        {
            ["format"] = CatalogJson.FormatName,
            ["version"] = CatalogJson.CurrentVersion,
            ["source"] = $"Sonic Racing CrossWorlds {info.GameVersion} (extracted)",
            ["gameVersion"] = info.GameVersion.ToString(),
            ["steamBuildId"] = info.SteamBuildId,
            ["defaultLanguage"] = tables.DefaultLanguage,
            ["languages"] = new JsonArray([.. languages.Select(l => (JsonNode)JsonValue.Create(l))]),
            // The table also has categories no gadget uses (an "InValid" one, for instance).
            ["categories"] = new JsonArray([.. categories
                .Where(c => gadgets.Any(g => g.CategoryId == c.Id))
                .OrderBy(c => c.Sort)
                .ThenBy(c => c.Id, StringComparer.Ordinal)
                .Select(c => (JsonNode)c.Json)]),
            ["gadgets"] = new JsonArray([.. ordered]),
        };
        RemoveEmpty(catalog);
        return catalog;
    }

    /// <summary>The <c>buildid</c> of a Steam <c>appmanifest_*.acf</c>, or null.</summary>
    public static string? SteamBuildId(string appManifest)
    {
        foreach (var line in appManifest.Split('\n'))
        {
            var parts = line.Trim().Split('"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(p => p.Length > 0).ToArray();
            if (parts.Length == 2 && parts[0].Equals("buildid", StringComparison.OrdinalIgnoreCase)
                && parts[1].All(char.IsAsciiDigit))
                return parts[1];
        }
        return null;
    }

    /// <summary>One text field of a row in every language that resolves it.</summary>
    private static JsonObject Texts(IReadOnlyDictionary<string, JsonObject> rowsByLanguage, string rowName, string field, IEnumerable<string> languages)
    {
        var texts = new JsonObject();
        foreach (var language in languages)
        {
            if (rowsByLanguage.TryGetValue(language, out var rows)
                && rows[rowName] is JsonObject row
                && Resolved(row[field]) is { } text)
                texts[language] = text;
        }
        return texts;
    }

    /// <summary>The text of a CUE4Parse FText, or null when it is empty or an unresolved key.</summary>
    internal static string? Resolved(JsonNode? text)
    {
        if (text is not JsonObject t)
            return null;
        var localized = Str(t, "LocalizedString") ?? Str(t, "CultureInvariantString");
        if (string.IsNullOrWhiteSpace(localized))
            return null;
        var key = Str(t, "Key");
        return key is not null && localized == key ? null : localized;
    }

    private static string? AssetPath(JsonNode? path)
    {
        var text = path is JsonValue v && v.TryGetValue(out string? s) ? s : null;
        if (string.IsNullOrEmpty(text))
            return null;
        var dot = text.LastIndexOf('.');
        return dot > text.LastIndexOf('/') ? text[..dot] : text;
    }

    private static void RemoveEmpty(JsonObject json)
    {
        foreach (var key in json.Where(p => p.Value is null || p.Value is JsonObject { Count: 0 } || (p.Value is JsonValue v && v.TryGetValue(out string? s) && s.Length == 0))
                     .Select(p => p.Key).ToList())
            json.Remove(key);
    }

    private static string Strip(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.Ordinal) ? value[prefix.Length..] : value;

    private static string? Str(JsonObject obj, string name) =>
        obj[name] is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    private static int Int(JsonObject obj, string name) =>
        obj[name] is JsonValue v && v.TryGetValue(out int i) ? i
        : obj[name] is JsonValue d && d.TryGetValue(out double x) ? (int)x
        : 0;

    private static bool Bool(JsonObject obj, string name) =>
        obj[name] is JsonValue v && v.TryGetValue(out bool b) && b;
}
