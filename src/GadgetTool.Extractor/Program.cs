using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GadgetTool.Core.Gadgets;
using GadgetTool.Extractor;

const string Usage = """
    GadgetTool.Extractor: writes a data pack (docs/DATA.md) from the game's files.

      --paks <folder>         the game's UNION\Content\Paks folder
      --aes-key <key|file>    the pak AES key (0x + 64 hex digits), or a file that holds it
      --mappings <file>       a .usmap made for this game version
      --oodle <file>          oo2core_9_win64.dll
      --game-version <x.y.z>  the game version the pack is for (e.g. 2.0.0)
      --out <folder>          writes <folder>\<version>\catalog.json
      --languages <a,b,...>   only these languages (default: every language the game has)
      --icons <true|false>    also write <folder>\<version>\icons\<id>.png (default: true)

    Keys, mappings and the pack itself never go into git.
    """;

try
{
    var options = ParseArgs(args);
    var version = GameVersion.Parse(options["game-version"]);
    if (!version.IsKnown)
        throw new ArgumentException("--game-version must look like 2.0.0.");

    using var game = new GameFiles(options["paks"], AesKey(options["aes-key"]), options["mappings"], options["oodle"]);
    var languages = options.TryGetValue("languages", out var only)
        ? [.. only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
        : game.Languages();
    const string defaultLanguage = "en";
    if (!languages.Contains(defaultLanguage))
        throw new ArgumentException($"The languages must include '{defaultLanguage}'.");

    var gadgets = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
    var categories = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
    var plates = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
    foreach (var language in languages)
    {
        Console.WriteLine($"Reading the tables in '{language}'...");
        gadgets[language] = game.TableRows(GameFiles.GadgetTable, language);
        categories[language] = game.TableRows(GameFiles.CategoryTable, language);
        plates[language] = game.TableRows(GameFiles.PlateTable, language);
    }

    var steamBuildId = SteamBuildId(options["paks"]);
    var catalog = PackBuilder.BuildCatalog(
        new GameTables(defaultLanguage, gadgets, categories, game.MiniGadgets(), plates),
        new PackInfo(version, steamBuildId));
    var text = catalog.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    // The app must be able to read what was written.
    var check = CatalogJson.Parse(text);

    var folder = Path.Combine(options["out"], version.ToString());
    Directory.CreateDirectory(folder);
    var file = Path.Combine(folder, "catalog.json");
    File.WriteAllText(file, text);

    var costs = check.For(defaultLanguage).Gadgets.GroupBy(g => g.SlotCost).OrderBy(g => g.Key).Select(g => $"{g.Key}-slot: {g.Count()}");
    Console.WriteLine($"Wrote {file}");
    Console.WriteLine($"  {check.For(defaultLanguage).Gadgets.Count} gadgets ({string.Join(", ", costs)}), {check.Languages.Count} languages: {string.Join(", ", check.Languages)}");

    var iconsOk = true;
    if (!options.TryGetValue("icons", out var icons) || !bool.TryParse(icons, out var writeIcons) || writeIcons)
        iconsOk = WriteIcons(game, catalog, Path.Combine(folder, "icons")) & WriteUi(game, Path.Combine(folder, "ui"));

    // index.json next to the version folders: this pack's files and hashes, other packs kept.
    var indexPath = Path.Combine(options["out"], "index.json");
    var index = File.Exists(indexPath) ? PackIndex.Parse(File.ReadAllText(indexPath)) : PackIndex.Empty;
    var entry = PackBuilder.IndexEntry(version, steamBuildId, folder);
    File.WriteAllText(indexPath, index.With(entry).ToJson());
    Console.WriteLine($"Wrote {indexPath} ({entry.Files.Count} files for {version}, Steam build {steamBuildId ?? "unknown"})");
    return iconsOk ? 0 : 1;
}
catch (Exception e) when (e is ArgumentException or KeyNotFoundException or IOException or PackException or InvalidOperationException or FormatException)
{
    Console.Error.WriteLine(e.Message);
    if (e is ArgumentException or KeyNotFoundException)
        Console.Error.WriteLine(Usage);
    return 1;
}

static Dictionary<string, string> ParseArgs(string[] args)
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            throw new ArgumentException($"Unexpected argument '{args[i]}'.");
        options[args[i][2..]] = args[++i];
    }
    foreach (var required in new[] { "paks", "aes-key", "mappings", "oodle", "game-version", "out" })
    {
        if (!options.ContainsKey(required))
            throw new ArgumentException($"--{required} is missing.");
    }
    return options;
}

// Each gadget's icon as icons\<id>.png; true when every gadget got one.
static bool WriteIcons(GameFiles game, JsonObject catalog, string folder)
{
    Directory.CreateDirectory(folder);
    var written = 0;
    var problems = new List<string>();
    foreach (var gadget in catalog["gadgets"]!.AsArray().OfType<JsonObject>())
    {
        var id = (string)gadget["id"]!;
        if (gadget["icon"] is not JsonValue icon || !icon.TryGetValue(out string? path))
        {
            problems.Add($"'{id}' has no icon.");
            continue;
        }
        try
        {
            var (width, height, rgba) = game.TextureRgba(PackBuilder.PackagePath(path));
            // Centred on its canvas, so it sits in the middle of its base (Gibax).
            File.WriteAllBytes(Path.Combine(folder, id + ".png"), PngWriter.Encode(width, height, PngWriter.CenterDrawing(width, height, rgba)));
            written++;
        }
        catch (Exception e)
        {
            problems.Add($"'{id}' ({path}): {e.Message}");
        }
    }

    Console.WriteLine($"Wrote {written} icons to {folder}");
    foreach (var problem in problems)
        Console.Error.WriteLine("  " + problem);
    return problems.Count == 0;
}

// The game's icon bases as ui\<name>.png; true when every one was written.
static bool WriteUi(GameFiles game, string folder)
{
    Directory.CreateDirectory(folder);
    var ok = true;
    foreach (var (name, texture) in PackBuilder.UiTextures)
    {
        try
        {
            // The game's bases sit on bigger transparent canvases (the 2-slot one on a 3-slot
            // canvas): written cut to what is drawn.
            var full = game.TextureRgba(texture);
            var (width, height, rgba) = PngWriter.TrimTransparent(full.Width, full.Height, full.Rgba);
            File.WriteAllBytes(Path.Combine(folder, name), PngWriter.Encode(width, height, rgba));
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"  {name} ({texture}): {e.Message}");
            ok = false;
        }
    }
    Console.WriteLine($"Wrote {PackBuilder.UiTextures.Count} icon bases and marks to {folder}");

    // The plates: each 2048x1024 picture of the atlas, cut to what is drawn.
    try
    {
        var atlas = game.TextureRgba(PackBuilder.PlateAtlas);
        var (cx, cy, cw, ch) = PackBuilder.PlateCrop;
        for (var i = 0; i < PackBuilder.PlateFrames; i++)
        {
            var fx = i % PackBuilder.PlateColumns * PackBuilder.PlateFrameWidth;
            var fy = i / PackBuilder.PlateColumns * PackBuilder.PlateFrameHeight;
            var cut = PngWriter.Crop(atlas.Width, atlas.Height, atlas.Rgba, fx + cx, fy + cy, cw, ch);
            File.WriteAllBytes(Path.Combine(folder, $"plate-{i}.png"), PngWriter.Encode(cw, ch, cut));
        }
        Console.WriteLine($"Wrote {PackBuilder.PlateFrames} plates to {folder}");
    }
    catch (Exception e)
    {
        Console.Error.WriteLine($"  plates ({PackBuilder.PlateAtlas}): {e.Message}");
        ok = false;
    }
    return ok;
}

// The key itself, or the first key found in a file (so it stays out of the command line and logs).
static string AesKey(string value)
{
    var text = File.Exists(value) ? File.ReadAllText(value) : value;
    var match = Regex.Match(text, "0x[0-9A-Fa-f]{64}");
    return match.Success ? match.Value : throw new ArgumentException("No AES key (0x + 64 hex digits) in --aes-key.");
}

// The Steam build id from steamapps\appmanifest_2486820.acf above the pak folder, if there is one.
static string? SteamBuildId(string paksFolder)
{
    for (var dir = new DirectoryInfo(paksFolder); dir is not null; dir = dir.Parent)
    {
        var manifest = Path.Combine(dir.FullName, "appmanifest_2486820.acf");
        if (File.Exists(manifest))
            return SteamInstall.BuildId(File.ReadAllText(manifest));
    }
    return null;
}
