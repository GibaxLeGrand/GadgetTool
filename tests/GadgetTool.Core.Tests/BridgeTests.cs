using System.Text.Json.Nodes;
using GadgetTool.Core.Bridge;
using GadgetTool.Core.Gadgets;
using GadgetTool.Core.Saves;
using GadgetTool.Core.Setups;
using static GadgetTool.Core.Tests.TestData;

namespace GadgetTool.Core.Tests;

internal sealed class FakeUi : IHostUi
{
    public string? SaveToOpen { get; set; }
    public string? ExportPath { get; set; }
    public string? ImportPath { get; set; }
    public string? LastSuggestedName { get; private set; }

    public string? PickSaveToOpen() => SaveToOpen;

    public string? PickExportPath(string suggestedName)
    {
        LastSuggestedName = suggestedName;
        return ExportPath;
    }

    public string? PickFileToImport() => ImportPath;
}

public sealed class AppBridgeTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FakeUi _ui = new();
    private bool _gameRunning;
    private readonly AppBridge _bridge;

    public AppBridgeTests()
    {
        File.WriteAllText(SavePath, DevSaveJson);
        File.WriteAllText(_temp.File("sample.json"), DevSaveJson);
        _bridge = new AppBridge(new AppBridgeOptions(
            Catalog, new DevJsonSaveFormat(), _ui, new Probe(() => _gameRunning),
            new FixedTime(new DateTimeOffset(2026, 11, 19, 16, 0, 0, TimeSpan.Zero)),
            _temp.File("backups"), _temp.File("sample.json"), _temp.File("data/sample-copy.json"),
            new PresetNameStore(_temp.File("data/names.json"))));
    }

    private string SavePath => _temp.File("save.json");

    public void Dispose() => _temp.Dispose();

    private sealed class Probe(Func<bool> running) : IGameProcessProbe
    {
        public bool IsGameRunning() => running();
    }

    private JsonObject Call(string method, JsonObject? args = null, int id = 7)
    {
        var request = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = args ?? [] };
        var reply = JsonNode.Parse(_bridge.Handle(request.ToJsonString()))!.AsObject();
        Assert.Equal(id, reply["id"]!.GetValue<int>());
        return reply;
    }

    private static JsonObject Result(JsonObject reply) =>
        reply["result"]?.AsObject() ?? throw new Xunit.Sdk.XunitException("Error: " + reply["error"]);

    private static string ErrorKind(JsonObject reply) =>
        reply["error"]?["kind"]?.GetValue<string>() ?? throw new Xunit.Sdk.XunitException("No error: " + reply);

    private JsonObject Open() => Result(Call("openSave", new JsonObject { ["path"] = SavePath }));

    private static JsonObject SaveRequest(JsonObject opened, Action<JsonArray> editPlayer1Plates)
    {
        var players = new JsonArray();
        foreach (var p in opened["players"]!.AsArray())
        {
            var plates = p!["plates"]!.DeepClone().AsArray();
            if (p["number"]!.GetValue<int>() == 1)
                editPlayer1Plates(plates);
            players.Add(new JsonObject { ["number"] = p["number"]!.GetValue<int>(), ["plates"] = plates });
        }
        return new JsonObject { ["players"] = players };
    }

    [Fact]
    public void Sends_the_catalog()
    {
        var gadgets = Result(Call("catalog"))["gadgets"]!.AsArray();

        Assert.Equal(Catalog.Gadgets.Count, gadgets.Count);
        Assert.Equal(TwoA, gadgets[4]!["id"]!.GetValue<string>());
        Assert.Equal(2, gadgets[4]!["slotCost"]!.GetValue<int>());
    }

    [Fact]
    public void Opens_a_save_and_describes_every_player()
    {
        var save = Open();

        Assert.Equal("save.json", save["fileName"]!.GetValue<string>());
        var p1 = save["players"]![0]!;
        Assert.Equal("Grand Master", p1["level"]!.GetValue<string>());
        Assert.True(p1["superSlot"]!.GetValue<bool>());
        Assert.Equal(OneB, p1["plates"]![0]!["super"]!.GetValue<string>());
        var p2 = save["players"]![1]!;
        Assert.Equal("Silver", p2["level"]!.GetValue<string>());
        Assert.Null(p2["plates"]![0]!["super"]);
    }

    [Fact]
    public void A_cancelled_file_dialog_is_not_an_error()
    {
        _ui.SaveToOpen = null;

        Assert.True(Result(Call("openSave"))["cancelled"]!.GetValue<bool>());
    }

    [Fact]
    public void Saves_the_edited_plates_after_a_backup()
    {
        var opened = Open();

        var reply = Result(Call("save", SaveRequest(opened, plates => plates[1] = new JsonObject { ["gadgets"] = new JsonArray(ThreeA) })));

        Assert.Equal(1, reply["saved"]!.GetValue<int>());
        Assert.Equal(DevSaveJson, File.ReadAllText(reply["backup"]!.GetValue<string>()));
        var written = new DevJsonSaveFormat().Read(File.ReadAllBytes(SavePath));
        Assert.True(written.Plate(new PlateRef(1, 1)).SameGadgetsAs(Plate(ThreeA)));
    }

    [Fact]
    public void Refuses_to_save_while_the_game_runs()
    {
        var opened = Open();
        _gameRunning = true;

        var reply = Call("save", SaveRequest(opened, plates => plates[1] = new JsonObject { ["gadgets"] = new JsonArray(ThreeA) }));

        Assert.Equal("gameRunning", ErrorKind(reply));
        Assert.Equal(DevSaveJson, File.ReadAllText(SavePath));
        Assert.True(Result(Call("gameRunning"))["running"]!.GetValue<bool>());
    }

    [Fact]
    public void Checks_the_rules_again_before_writing()
    {
        var opened = Open();

        // TwoB is not in P1's unlocked list of the dev save.
        var reply = Call("save", SaveRequest(opened, plates => plates[1] = new JsonObject { ["gadgets"] = new JsonArray(TwoB) }));

        Assert.Equal("invalid", ErrorKind(reply));
        Assert.Equal(DevSaveJson, File.ReadAllText(SavePath));
    }

    [Fact]
    public void Reports_a_save_changed_on_disk_and_reloads_it()
    {
        var opened = Open();
        File.WriteAllText(SavePath, DevSaveJson.Replace("\"races\": 120", "\"races\": 121", StringComparison.Ordinal));

        var reply = Call("save", SaveRequest(opened, plates => plates[1] = new JsonObject { ["gadgets"] = new JsonArray(ThreeA) }));
        Assert.Equal("saveChanged", ErrorKind(reply));

        Assert.Equal("save.json", Result(Call("reloadSave"))["fileName"]!.GetValue<string>());
    }

    [Fact]
    public void Saving_without_an_open_save_is_an_error() =>
        Assert.Equal("noSave", ErrorKind(Call("save", new JsonObject { ["players"] = new JsonArray() })));

    [Fact]
    public void Opens_a_copy_of_the_sample()
    {
        var sample = Result(Call("openSample"));
        var opened = sample["path"]!.GetValue<string>();

        Assert.Equal(_temp.File("data/sample-copy.json"), opened);
        Result(Call("save", SaveRequest(sample, plates => plates[1] = new JsonObject { ["gadgets"] = new JsonArray(ThreeA) })));
        Assert.Equal(DevSaveJson, File.ReadAllText(_temp.File("sample.json")));
    }

    [Fact]
    public void Names_presets_and_sends_the_names_with_the_save()
    {
        Open();

        var reply = Result(Call("renamePreset", new JsonObject { ["player"] = 1, ["index"] = 1, ["name"] = "  Time\ttrial  " }));
        Assert.Equal("Time trial", reply["name"]!.GetValue<string>());

        var reopened = Open();
        Assert.Equal("Time trial", reopened["names"]!["1:1"]!.GetValue<string>());
        Assert.Equal(DevSaveJson, File.ReadAllText(SavePath));
    }

    [Fact]
    public void A_blank_name_removes_it()
    {
        Open();
        Call("renamePreset", new JsonObject { ["player"] = 2, ["index"] = 0, ["name"] = "Grand Prix" });

        Assert.Null(Result(Call("renamePreset", new JsonObject { ["player"] = 2, ["index"] = 0, ["name"] = " " }))["name"]);
        Assert.Empty(Open()["names"]!.AsObject());
    }

    [Fact]
    public void Naming_a_preset_that_does_not_exist_is_refused()
    {
        Open();

        Assert.Equal("badRequest", ErrorKind(Call("renamePreset", new JsonObject { ["player"] = 1, ["index"] = 10, ["name"] = "X" })));
    }

    [Fact]
    public void Exports_where_the_user_picks()
    {
        _ui.ExportPath = _temp.File("out.json");

        var reply = Result(Call("exportFile", new JsonObject { ["suggestedName"] = "gadgettool-P1A.json", ["content"] = "{}" }));

        Assert.Equal("out.json", reply["fileName"]!.GetValue<string>());
        Assert.Equal("gadgettool-P1A.json", _ui.LastSuggestedName);
        Assert.Equal("{}", File.ReadAllText(_temp.File("out.json")));
    }

    [Fact]
    public void Imports_a_file_and_refuses_a_huge_one()
    {
        File.WriteAllText(_temp.File("in.json"), "{ \"format\": \"gadgettool.setups\" }");
        _ui.ImportPath = _temp.File("in.json");
        Assert.Contains("gadgettool.setups", Result(Call("importFile"))["content"]!.GetValue<string>(), StringComparison.Ordinal);

        File.WriteAllBytes(_temp.File("huge.json"), new byte[AppBridge.MaxImportBytes + 1]);
        _ui.ImportPath = _temp.File("huge.json");
        Assert.Equal("tooLarge", ErrorKind(Call("importFile")));
    }

    [Theory]
    [InlineData("""{ "id": 7, "method": "formatDisk" }""", "unknownMethod")]
    [InlineData("""{ "id": 7 }""", "badRequest")]
    [InlineData("""{ "id": 7, "method": "save", "params": { "players": [ { "number": 9, "plates": [] } ] } }""", "noSave")]
    public void Bad_requests_get_an_error(string request, string kind)
    {
        var reply = JsonNode.Parse(_bridge.Handle(request))!.AsObject();

        Assert.Equal(7, reply["id"]!.GetValue<int>());
        Assert.Equal(kind, ErrorKind(reply));
    }

    [Fact]
    public void Garbage_gets_an_error_too() =>
        Assert.Equal("badRequest", JsonNode.Parse(_bridge.Handle("not json"))!["error"]!["kind"]!.GetValue<string>());
}

public class PresetNameStoreTests
{
    [Fact]
    public void Names_survive_a_restart_and_follow_the_save_path_whatever_its_case()
    {
        using var temp = new TempFolder();
        var file = temp.File("names.json");
        var save = temp.File("Save.sav");

        new PresetNameStore(file).Set(save, new PlateRef(3, 9), "Mirror cups");
        var names = new PresetNameStore(file).NamesFor(save.ToUpperInvariant());

        Assert.Equal("Mirror cups", names["3:9"]);
        Assert.Empty(new PresetNameStore(file).NamesFor(temp.File("other.sav")));
    }

    [Theory]
    [InlineData("  Time   trial ", "Time trial")]
    [InlineData("Line\nbreak", "Line break")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Names_are_cleaned(string? name, string? kept) => Assert.Equal(kept, PresetNameStore.Clean(name));

    [Fact]
    public void Names_are_cut_to_the_maximum_length() =>
        Assert.Equal(PresetNameStore.MaxLength, PresetNameStore.Clean(new string('x', 100))!.Length);

    [Fact]
    public void A_damaged_names_file_means_no_names_not_a_crash()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.File("names.json"), "{ not json");

        var store = new PresetNameStore(temp.File("names.json"));

        Assert.Empty(store.NamesFor(temp.File("save.sav")));
        Assert.Equal("Fresh", store.Set(temp.File("save.sav"), new PlateRef(1, 0), "Fresh"));
    }
}

public class CatalogJsonTests
{
    [Fact]
    public void Reads_a_catalog()
    {
        var catalog = CatalogJson.Parse("""
            { "format": "gadgettool.catalog", "version": 1, "source": "Test",
              "gadgets": [ { "id": "a", "name": "A", "slotCost": 2, "category": "Rings", "description": "Does a." } ] }
            """);

        Assert.Equal("Test", catalog.Source);
        Assert.True(catalog.TryGet("a", out var a));
        Assert.Equal(new GadgetDefinition("a", "A", 2, "Rings", "Does a."), a);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("""{ "format": "other", "version": 1, "gadgets": [] }""")]
    [InlineData("""{ "format": "gadgettool.catalog", "version": 2, "gadgets": [] }""")]
    [InlineData("""{ "format": "gadgettool.catalog", "version": 1, "gadgets": [ { "id": "a" } ] }""")]
    [InlineData("""{ "format": "gadgettool.catalog", "version": 1, "gadgets": [ { "id": "a", "slotCost": 1 }, { "id": "a", "slotCost": 1 } ] }""")]
    public void Refuses_a_bad_catalog(string json) =>
        Assert.Throws<FormatException>(() => CatalogJson.Parse(json));
}

public class DevDataTests
{
    private static string RepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GadgetTool.slnx")))
                return Path.Combine(dir.FullName, relative);
        }
        throw new InvalidOperationException("The repository root was not found.");
    }

    [Fact]
    public void The_sample_save_matches_the_made_up_catalog()
    {
        var catalog = CatalogJson.Parse(File.ReadAllText(RepoFile("data/dev/catalog.json")));
        var save = new DevJsonSaveFormat().Read(File.ReadAllBytes(RepoFile("data/dev/save.json")));
        var players = save.Players;

        Assert.Equal([1, 2, 3, 4], players.Select(p => p.PlayerNumber));
        Assert.All(players, p => Assert.Equal(10, p.Plates.Count));
        Assert.All(players.SelectMany(p => p.Unlocked), id => Assert.True(catalog.TryGet(id, out _), id));

        // Every preset is valid but one, left broken on purpose to show how the tool reports it.
        var broken = (from p in players
                      from i in Enumerable.Range(0, p.Plates.Count)
                      where Rules.PlateValidator.Validate(p.Plates[i], p, catalog).Count > 0
                      select new PlateRef(p.PlayerNumber, i)).ToList();
        Assert.Equal([new PlateRef(2, 9)], broken);
    }

    [Theory]
    [InlineData(new[] { 3, 3 }, true, "Grand Master")]
    [InlineData(new[] { 3, 3 }, false, "Master")]
    [InlineData(new[] { 3, 2 }, false, "Gold")]
    [InlineData(new[] { 3, 1 }, false, "Silver")]
    [InlineData(new[] { 3, 0 }, false, "Bronze")]
    [InlineData(new[] { 2, 0 }, false, "Normal")]
    public void Plate_levels_have_the_game_s_names(int[] rows, bool superSlot, string name) =>
        Assert.Equal(name, new PlateLayout(rows, superSlot).LevelName);
}
