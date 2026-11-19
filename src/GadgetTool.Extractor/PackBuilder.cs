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
/// <param name="MiniGadgets">The ids of <c>DA_MiniGadgetList</c> (with or without the enum prefix).</param>
/// <param name="PlateRows">Language → rows of <c>DT_GadgetPlateData</c> (the plate levels), if read.</param>
internal sealed record GameTables(
    string DefaultLanguage,
    IReadOnlyDictionary<string, JsonObject> GadgetRows,
    IReadOnlyDictionary<string, JsonObject> CategoryRows,
    IReadOnlyCollection<string>? MiniGadgets = null,
    IReadOnlyDictionary<string, JsonObject>? PlateRows = null);

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
/// <item>Ids hold only ASCII letters, digits and '_': each names its icon file, <c>icons/&lt;id&gt;.png</c>.</item>
/// <item>The icon's base (the frame the game draws it on): <c>mini</c> for the gadgets of
/// <c>DA_MiniGadgetList</c>, <c>kit</c> for the categories whose id ends with "Kit" (all
/// 3-slot in 2.0.0; Gibax checked in the game that the advanced kits too, Spin Dash kit
/// included, have the kit frame), none for the others. The game picks it in native code
/// (<c>EquipmentGadgetIcon::SetIconBaseShape</c>); this rule matches its material lists.</item>
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
        var mini = (tables.MiniGadgets ?? []).Select(id => Strip(id, GadgetEnumPrefix)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (rowName, node) in gadgetRows)
        {
            if (node is not JsonObject row || !Bool(row, "bIsEnabled"))
                continue;

            var id = Strip(Str(row, "ID") ?? rowName, GadgetEnumPrefix);
            if (!seen.Add(id))
                problems.Add($"Gadget id '{id}' appears twice (ignoring case).");
            if (id.Length == 0 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
                problems.Add($"Gadget id '{id}' has characters other than letters, digits and '_' (it names the icon file).");

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
                ["base"] = mini.Contains(id) ? "mini"
                    : categoryId.EndsWith("Kit", StringComparison.Ordinal) ? "kit"
                    : null,
                ["icon"] = AssetPath((row["IconTexture"] as JsonObject)?["AssetPathName"]),
            };
            RemoveEmpty(json);
            gadgets.Add((categoryId, Int(row, "GadgetCustomSortId"), id, json));
        }

        // The plate levels: slots, Super Slot, the plate's picture (its frame in the atlas) and name.
        var plates = new JsonArray();
        if (tables.PlateRows is { } plateRows && plateRows.TryGetValue(tables.DefaultLanguage, out var plateDefaults))
        {
            foreach (var (rowName, node) in plateDefaults)
            {
                if (node is not JsonObject row)
                    continue;
                var id = Strip(Str(row, "ID") ?? rowName, PlateEnumPrefix);
                var frame = Int(row, "BaseIndex");
                if (frame < 0 || frame >= PlateFrames)
                    problems.Add($"Plate '{id}' uses picture {frame}; the atlas has {PlateFrames}.");
                var plate = new JsonObject
                {
                    ["id"] = id,
                    ["slots"] = Int(row, "SlotNum"),
                    ["superSlot"] = Bool(row, "bSpSlotOpen"),
                    ["presets"] = Int(row, "PresetCount"),
                    ["image"] = $"ui/plate-{frame}.png",
                    ["name"] = Texts(plateRows, rowName, "Name", languages),
                };
                RemoveEmpty(plate);
                plates.Add(plate);
            }
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
            ["plates"] = plates.Count > 0 ? plates : null,
            ["plateGeometry"] = plates.Count > 0 ? PlateGeometry() : null,
        };
        RemoveEmpty(catalog);
        return catalog;
    }

    /// <summary>The plates' atlas: 7 pictures of 2048x1024, two per row, in <c>BaseIndex</c> order.</summary>
    public const string PlateAtlas = "UNION/Content/01_Union/UI/Garage/GadgetCustom/Texture/UI_GadgetCustom_GadgetPlate";
    public const int PlateFrames = 7, PlateFrameWidth = 2048, PlateFrameHeight = 1024, PlateColumns = 2;

    /// <summary>What is drawn of every plate inside its 2048x1024 frame (the same box for all seven).</summary>
    public static (int X, int Y, int Width, int Height) PlateCrop { get; } = (296, 88, 1488, 852);

    /// <summary>
    /// Where the game puts things on a plate picture cut to <see cref="PlateCrop"/>, in its
    /// pixels: from <c>WBP_GadgetCustom_Sub_GadgetPlate</c>, whose gadget icons (512 pixels at a
    /// scale of 0.6, so 307) sit 374 pixels apart across, 316 down, the Super Slot's 1035 across
    /// and 151 down from the first; the first cell's centre and the title band measured on the
    /// picture. Cells are numbered row by row, 3 per row.
    /// </summary>
    public static JsonObject PlateGeometry() => new()
    {
        ["width"] = PlateCrop.Width,
        ["height"] = PlateCrop.Height,
        ["cell"] = 307,
        ["first"] = new JsonArray(222, 348),
        ["step"] = new JsonArray(374, 316),
        ["super"] = new JsonArray(222 + 1035, 348 + 151),
        ["title"] = new JsonArray(25, 85, 1400, 175),
    };

    /// <summary>
    /// The game's icon bases written to <c>ui/</c>: file name → texture. The page picks one per
    /// gadget from its slot cost and base (see docs/DATA.md).
    /// </summary>
    public static IReadOnlyDictionary<string, string> UiTextures { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["base-1.png"] = IconBases + "UI_GadgetIcon_Base_01",
        ["base-2.png"] = IconBases + "UI_GadgetIcon_Base_02",
        ["base-3.png"] = IconBases + "UI_GadgetIcon_Base_03",
        ["base-kit-1.png"] = IconBases + "UI_GadgetIcon_Base_01_Kit_001",
        ["base-kit-3.png"] = IconBases + "UI_GadgetIcon_Base_03_Kit_001",
        ["base-mini.png"] = IconBases + "UI_GadgetIcon_Base_01_Mini",
        ["base-super.png"] = IconBases + "UI_GadgetIcon_Base_01_Super_01",
        ["lock.png"] = ScreenTextures + "UI_GudgetCustom_LockIcon",
        ["plus.png"] = ScreenTextures + "UI_GudgetCustom_PlusMark",
    };

    private const string ScreenTextures = "UNION/Content/01_Union/UI/Garage/GadgetCustom/Texture/";

    private const string IconBases = "UNION/Content/01_Union/UI/Common/GadgetIcon/Texture/";

    /// <summary>The pak path of a game asset: "/Game/01_Union/X" → "UNION/Content/01_Union/X".</summary>
    public static string PackagePath(string gamePath)
    {
        const string game = "/Game/";
        return gamePath.StartsWith(game, StringComparison.Ordinal)
            ? "UNION/Content/" + gamePath[game.Length..]
            : throw new ArgumentException($"'{gamePath}' is not under /Game/.", nameof(gamePath));
    }

    /// <summary>
    /// The pack's entry for <c>index.json</c>: every file of the version folder with its SHA-256
    /// (lower-case hex), paths with forward slashes.
    /// </summary>
    public static PackEntry IndexEntry(GameVersion version, string? steamBuildId, string folder)
    {
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToDictionary(
                f => Path.GetRelativePath(folder, f).Replace('\\', '/'),
                f => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f))),
                StringComparer.Ordinal);
        return new PackEntry(version, steamBuildId is null ? [] : [steamBuildId], version.ToString(), files);
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
