using System.Text.Json.Nodes;
using GadgetTool.Core.Bridge;
using GadgetTool.Core.Gadgets;
using GadgetTool.Core.Saves;

namespace GadgetTool.Core.Tests;

/// <summary>What the extractor writes beyond the first catalog format: Super effects, category colours, plates.</summary>
public sealed class CatalogExtrasTests : IDisposable
{
    private const string Pack = """
        {
          "format": "gadgettool.catalog", "version": 1, "gameVersion": "2.0.0", "defaultLanguage": "en",
          "categories": [
            { "id": "rings", "sort": 1, "color": "#FEF7B2", "name": { "en": "Rings" } },
            { "id": "odd", "sort": 2, "color": "yellow", "name": { "en": "Odd" } }
          ],
          "gadgets": [
            { "id": "t.ring", "slotCost": 1, "categoryId": "rings", "name": { "en": "Ring", "fr": "Anneau" }, "base": "mini",
              "superEffect": { "en": "Even more rings.", "fr": "Encore plus d'anneaux." } },
            { "id": "t.odd", "slotCost": 2, "categoryId": "odd", "name": { "en": "Odd" }, "base": "round" }
          ]
        }
        """;

    /// <summary>Made-up gadgets with the game's plate levels and where things go on their pictures.</summary>
    private const string PlatesPack = """
        {
          "format": "gadgettool.catalog", "version": 1, "gameVersion": "2.0.0", "defaultLanguage": "en",
          "categories": [ { "id": "rings", "sort": 1, "name": { "en": "Rings" } } ],
          "gadgets": [
            { "id": "t.a", "slotCost": 1, "categoryId": "rings", "name": { "en": "A", "fr": "A fr" } },
            { "id": "t.b", "slotCost": 1, "categoryId": "rings", "name": { "en": "B" } },
            { "id": "t.c", "slotCost": 2, "categoryId": "rings", "name": { "en": "C" } },
            { "id": "t.d", "slotCost": 3, "categoryId": "rings", "name": { "en": "D" } }
          ],
          "plates": [
            { "id": "Rank0", "slots": 2, "superSlot": false, "image": "ui/plate-0.png", "name": { "en": "Normal Plate", "fr": "Plaque normale" } },
            { "id": "Rank7", "slots": 6, "superSlot": true, "image": "ui/plate-6.png", "name": { "en": "Top Plate" } }
          ],
          "plateGeometry": { "width": 1488, "height": 852, "cell": 307, "first": [222, 348], "step": [374, 316],
                             "super": [1257, 499], "title": [25, 85, 1400, 175] }
        }
        """;

    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private AppBridge Bridge(string pack, bool sampleFromCatalog = false) => new(new AppBridgeOptions(
        CatalogJson.Parse(pack), new DevJsonSaveFormat(), new FakeUi(), new NotRunning(), TimeProvider.System,
        _temp.File("backups"), null, _temp.File("sample.json"),
        new PresetNameStore(_temp.File("names.json")), new SeenGameStore(_temp.File("seen.json")))
    {
        PackUrl = "https://pack.test",
        SampleFromCatalog = sampleFromCatalog,
    });

    private static JsonObject Reply(AppBridge bridge, string method, string language = "en") =>
        JsonNode.Parse(bridge.Handle(new JsonObject
        {
            ["id"] = 1,
            ["method"] = method,
            ["params"] = new JsonObject { ["language"] = language },
        }.ToJsonString()))!.AsObject();

    private static JsonObject Result(AppBridge bridge, string method, string language = "en") =>
        Reply(bridge, method, language)["result"]?.AsObject() ?? throw new Xunit.Sdk.XunitException("No result for " + method);

    [Fact]
    public void Reads_the_plate_levels_and_where_things_go_on_their_pictures()
    {
        var catalog = CatalogJson.Parse(PlatesPack);

        Assert.Equal(["Rank0", "Rank7"], catalog.Plates.Select(p => p.Id));
        Assert.Equal([2, 6], catalog.Plates.Select(p => p.Slots));
        Assert.Equal([false, true], catalog.Plates.Select(p => p.SuperSlot));
        Assert.Equal(["ui/plate-0.png", "ui/plate-6.png"], catalog.Plates.Select(p => p.Image));
        Assert.Equal("Plaque normale", catalog.Plates[0].Name.Get("fr", "en"));
        Assert.Equal("Top Plate", catalog.Plates[1].Name.Get("fr", "en"));
        Assert.Equal(new PlateGeometry(1488, 852, 307, 222, 348, 374, 316, 1257, 499, 25, 85, 1400, 175), catalog.PlateGeometry);
    }

    [Fact]
    public void A_catalog_without_plates_has_none()
    {
        var catalog = CatalogJson.Parse(Pack);
        Assert.Empty(catalog.Plates);
        Assert.Null(catalog.PlateGeometry);
    }

    [Theory]
    [InlineData("../plate-0.png")]
    [InlineData("ui/../../plate-0.png")]
    [InlineData("ui/..png")]
    [InlineData("https://elsewhere.test/plate-0.png")]
    [InlineData("ui/plate-0.svg")]
    [InlineData("ui/sub/plate-0.png")]
    public void Refuses_a_plate_picture_outside_the_pack_s_ui_folder(string image) =>
        Assert.Throws<FormatException>(() => CatalogJson.Parse(PlatesPack.Replace("ui/plate-0.png", image, StringComparison.Ordinal)));

    [Theory]
    [InlineData("\"first\": [222, 348]", "\"first\": [222]")]
    [InlineData("\"cell\": 307", "\"cell\": -1")]
    [InlineData("\"title\": [25, 85, 1400, 175]", "\"title\": [25, 85]")]
    public void Refuses_a_plate_geometry_with_missing_numbers(string good, string bad) =>
        Assert.Throws<FormatException>(() => CatalogJson.Parse(PlatesPack.Replace(good, bad, StringComparison.Ordinal)));

    [Fact]
    public void The_page_gets_the_plates_in_its_language_and_their_geometry()
    {
        var result = Result(Bridge(PlatesPack), "catalog", "fr");
        var plates = result["plates"]!.AsArray();

        Assert.Equal(["Plaque normale", "Top Plate"], plates.Select(p => (string?)p!["name"]));
        Assert.Equal("ui/plate-6.png", (string?)plates[1]!["image"]);
        Assert.True((bool)plates[1]!["superSlot"]!);
        Assert.Equal(1488, (int)result["plateGeometry"]!["width"]!);
        Assert.Equal([222, 348], result["plateGeometry"]!["first"]!.AsArray().Select(n => (int)n!));
        Assert.Equal([1257, 499], result["plateGeometry"]!["super"]!.AsArray().Select(n => (int)n!));
    }

    [Fact]
    public void With_the_game_s_data_the_sample_is_made_from_its_gadgets()
    {
        var opened = Result(Bridge(PlatesPack, sampleFromCatalog: true), "openSample");
        var unlocked = opened["players"]!.AsArray().SelectMany(p => p!["unlocked"]!.AsArray()).Select(n => (string)n!).ToHashSet();

        Assert.Equal(_temp.File("sample.json"), (string?)opened["path"]);
        Assert.True((bool)opened["sample"]!);
        Assert.Equal(new HashSet<string> { "t.a", "t.b", "t.c", "t.d" }, unlocked);
    }

    [Fact]
    public void Without_the_game_s_data_or_a_bundled_sample_there_is_no_sample() =>
        Assert.Equal("noSample", (string?)Reply(Bridge(PlatesPack), "openSample")["error"]!["kind"]);

    [Fact]
    public void Reads_super_effects_by_language_and_valid_category_colours()
    {
        var catalog = CatalogJson.Parse(Pack);

        Assert.True(catalog.For("fr").TryGet("t.ring", out var ring));
        Assert.Equal("Encore plus d'anneaux.", ring.SuperEffect);
        Assert.Equal("#FEF7B2", ring.CategoryColor);

        Assert.Equal("mini", ring.Base);

        Assert.True(catalog.For("en").TryGet("t.odd", out var odd));
        Assert.Null(odd.SuperEffect);
        Assert.Null(odd.CategoryColor); // "yellow" is not #RRGGBB
        Assert.Null(odd.Base);          // only "kit" and "mini" are known
    }

    [Fact]
    public void A_catalog_without_extras_still_reads() =>
        Assert.True(CatalogJson.Parse(System.IO.File.ReadAllText(RepoFile("data/dev/catalog.json"))).For("en").Gadgets.Count > 0);

    [Fact]
    public void The_page_gets_super_effects_and_category_colours()
    {
        var bridge = new AppBridge(new AppBridgeOptions(
            CatalogJson.Parse(Pack), new DevJsonSaveFormat(), new FakeUi(), new NotRunning(), TimeProvider.System,
            _temp.File("backups"), null, _temp.File("sample.json"),
            new PresetNameStore(_temp.File("names.json")), new SeenGameStore(_temp.File("seen.json")))
        {
            PackUrl = "https://pack.test",
        });

        var answer = JsonNode.Parse(bridge.Handle("""{ "id": 1, "method": "catalog", "params": { "language": "fr" } }"""))!;
        var ring = answer["result"]!["gadgets"]!.AsArray().Single(g => (string?)g!["id"] == "t.ring")!;

        Assert.Equal("https://pack.test", (string?)answer["result"]!["packUrl"]);
        Assert.Equal("Encore plus d'anneaux.", (string?)ring["superEffect"]);
        Assert.Equal("#FEF7B2", (string?)ring["categoryColor"]);
        Assert.Equal("mini", (string?)ring["base"]);
    }

    private static string RepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, relative);
            if (System.IO.File.Exists(path))
                return path;
        }
        throw new FileNotFoundException(relative);
    }

    private sealed class NotRunning : IGameProcessProbe
    {
        public bool IsGameRunning() => false;
    }
}
