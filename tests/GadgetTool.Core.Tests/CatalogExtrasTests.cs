using System.Text.Json.Nodes;
using GadgetTool.Core.Bridge;
using GadgetTool.Core.Gadgets;
using GadgetTool.Core.Saves;

namespace GadgetTool.Core.Tests;

/// <summary>What the extractor writes beyond the first catalog format: Super effects and category colours.</summary>
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
            { "id": "t.ring", "slotCost": 1, "categoryId": "rings", "name": { "en": "Ring", "fr": "Anneau" },
              "superEffect": { "en": "Even more rings.", "fr": "Encore plus d'anneaux." } },
            { "id": "t.odd", "slotCost": 2, "categoryId": "odd", "name": { "en": "Odd" } }
          ]
        }
        """;

    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Reads_super_effects_by_language_and_valid_category_colours()
    {
        var catalog = CatalogJson.Parse(Pack);

        Assert.True(catalog.For("fr").TryGet("t.ring", out var ring));
        Assert.Equal("Encore plus d'anneaux.", ring.SuperEffect);
        Assert.Equal("#FEF7B2", ring.CategoryColor);

        Assert.True(catalog.For("en").TryGet("t.odd", out var odd));
        Assert.Null(odd.SuperEffect);
        Assert.Null(odd.CategoryColor); // "yellow" is not #RRGGBB
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
            new PresetNameStore(_temp.File("names.json")), new SeenGameStore(_temp.File("seen.json"))));

        var answer = JsonNode.Parse(bridge.Handle("""{ "id": 1, "method": "catalog", "params": { "language": "fr" } }"""))!;
        var ring = answer["result"]!["gadgets"]!.AsArray().Single(g => (string?)g!["id"] == "t.ring")!;

        Assert.Equal("Encore plus d'anneaux.", (string?)ring["superEffect"]);
        Assert.Equal("#FEF7B2", (string?)ring["categoryColor"]);
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
