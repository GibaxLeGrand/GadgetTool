using GadgetTool.Core.Gadgets;
using GadgetTool.Core.Setups;
using GadgetTool.Core.Sharing;
using static GadgetTool.Core.Tests.TestData;

namespace GadgetTool.Core.Tests;

public class ShareCodeTests
{
    private static GadgetPlate Decode(string code) => ShareCode.Decode(code, Catalog).Plate;

    [Fact]
    public void A_plate_survives_a_round_trip()
    {
        var plate = new GadgetPlate([TwoA, OneA, TwoB, OneB], superGadgetId: OneC);

        var decoded = ShareCode.Decode(ShareCode.Encode(plate, Catalog), Catalog);

        Assert.True(decoded.Plate.SameGadgetsAs(plate));
        Assert.True(decoded.Unchanged);
    }

    [Fact]
    public void A_plate_without_super_slot_gadget_survives_a_round_trip()
    {
        var plate = Plate(ThreeA, OneD);

        var decoded = Decode(ShareCode.Encode(plate, Catalog));

        Assert.True(decoded.SameGadgetsAs(plate));
        Assert.Null(decoded.SuperGadgetId);
    }

    [Theory]
    // Produced by web/js/rules.js for the same plate and catalog: both sides must agree.
    [InlineData(null, "GT1-AQIBAAAA06Lq_83AVX8UvFV8Fv2k")]
    [InlineData("2.0.0", "GT1-AQIBAgAA06Lq_83AVX8UvFV8Fv27")]
    public void Codes_match_the_web_ui(string? gameVersion, string fromWebUi)
    {
        var catalog = new GadgetCatalog(Catalog.Gadgets, gameVersion: GameVersion.Parse(gameVersion));
        var plate = new GadgetPlate([TwoA, OneA], superGadgetId: OneB);

        Assert.Equal(fromWebUi, ShareCode.Encode(plate, catalog));
        Assert.True(ShareCode.Decode(fromWebUi, catalog).Plate.SameGadgetsAs(plate));
    }

    [Fact]
    public void A_full_plate_gives_a_short_code()
    {
        var code = ShareCode.Encode(new GadgetPlate([OneA, OneB, OneC, OneD, "t.five", "t.six"], superGadgetId: "t.seven"), Catalog);

        Assert.StartsWith(ShareCode.Prefix, code);
        Assert.True(code.Length <= 56, $"{code} is {code.Length} characters long");
    }

    [Fact]
    public void Spaces_around_and_prefix_case_do_not_matter()
    {
        var code = ShareCode.Encode(Plate(OneA), Catalog);

        var decoded = Decode("  " + code.Replace("GT1-", "gt1-", StringComparison.Ordinal) + "\n");

        Assert.True(decoded.SameGadgetsAs(Plate(OneA)));
    }

    [Fact]
    public void A_typo_is_caught()
    {
        var code = ShareCode.Encode(Plate(TwoA, OneA), Catalog).ToCharArray();
        code[14] = code[14] == 'A' ? 'B' : 'A';

        Assert.Throws<FormatException>(() => Decode(new string(code)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void A_cut_code_is_caught(int missing)
    {
        var code = ShareCode.Encode(Plate(TwoA, OneA), Catalog);

        Assert.Throws<FormatException>(() => Decode(code[..^missing]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("GT1-")]
    [InlineData("GT1-!!!!")]
    [InlineData("GT9-AAAA")]
    public void Garbage_is_refused(string code) =>
        Assert.Throws<FormatException>(() => Decode(code));

    [Fact]
    public void A_gadget_this_version_does_not_know_is_left_out_and_counted()
    {
        var decoded = ShareCode.Decode(ShareCode.Encode(Plate(OneA, "t.not_in_this_catalog"), Catalog), Catalog);

        Assert.True(decoded.Plate.SameGadgetsAs(Plate(OneA)));
        Assert.Equal(1, decoded.Missing);
        Assert.False(decoded.Unchanged);
    }
}

public class GameVersionTests
{
    private static readonly GadgetCatalog V200 = Versioned("2.0.0", (TwoA, 2));
    private static readonly GadgetCatalog V210 = Versioned("2.1.0", (TwoA, 3));

    /// <summary>The test catalog at a game version, with some slot costs changed.</summary>
    private static GadgetCatalog Versioned(string version, params (string Id, int Cost)[] costs) =>
        new(Catalog.Gadgets.Select(g => costs.FirstOrDefault(c => c.Id == g.Id) is { Id: not null } c ? g with { SlotCost = c.Cost } : g),
            gameVersion: GameVersion.Parse(version));

    [Theory]
    [InlineData("2.0.0", 2, 0, 0)]
    [InlineData("1.4", 1, 4, 0)]
    [InlineData(" 2 ", 2, 0, 0)]
    public void Versions_are_read(string text, int major, int minor, int patch) =>
        Assert.Equal(new GameVersion((byte)major, (byte)minor, (byte)patch), GameVersion.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("dev")]
    [InlineData("1.2.3.4")]
    [InlineData("300.0.0")]
    [InlineData("0.0.0")]
    public void Anything_else_is_unknown(string? text) => Assert.False(GameVersion.Parse(text).IsKnown);

    [Theory]
    [InlineData("2.0.0", "2.0.0", ShareAge.Same)]
    [InlineData("1.4.1", "2.0.0", ShareAge.Older)]
    [InlineData("2.0.1", "2.0.0", ShareAge.Newer)]
    [InlineData("2.1.0", "2.0.9", ShareAge.Newer)]
    [InlineData(null, "2.0.0", ShareAge.Unknown)]
    [InlineData("2.0.0", null, ShareAge.Unknown)]
    public void A_share_s_age_against_ours(string? shared, string? ours, ShareAge age) =>
        Assert.Equal(age, GameVersion.AgeOf(GameVersion.Parse(shared), GameVersion.Parse(ours)));

    [Fact]
    public void An_older_code_whose_gadgets_did_not_change_is_compatible()
    {
        var decoded = ShareCode.Decode(ShareCode.Encode(Plate(OneA, OneB), V200), V210);

        Assert.Equal(ShareAge.Older, decoded.Age);
        Assert.True(decoded.Unchanged);
    }

    [Fact]
    public void An_older_code_reports_what_the_update_changed()
    {
        var decoded = ShareCode.Decode(ShareCode.Encode(Plate(TwoA, OneA), V200), V210);

        Assert.True(decoded.CanImport);
        var change = Assert.Single(decoded.Changes);
        Assert.Equal(new GadgetChange(TwoA, "Two A", 2, 3), change);
    }

    [Fact]
    public void A_code_from_a_newer_game_version_cannot_be_imported()
    {
        var decoded = ShareCode.Decode(ShareCode.Encode(Plate(TwoA), V210), V200);

        Assert.Equal(ShareAge.Newer, decoded.Age);
        Assert.False(decoded.CanImport);
    }

    [Fact]
    public void An_older_file_reports_changes_per_preset_only_for_its_gadgets()
    {
        var removedOneB = new GadgetCatalog(V210.Gadgets.Where(g => g.Id != OneB), gameVersion: V210.GameVersion);
        var file = SetupJson.Import(SetupJson.Export(
            [new("Changed", Plate(TwoA, OneB)), new("Untouched", Plate(OneA, ThreeA))], V200));

        Assert.Equal(ShareAge.Older, file.AgeAgainst(removedOneB));
        var changed = file.ChangesFor(file.Plates[0], removedOneB);
        Assert.Equal(2, changed.Count);
        Assert.Contains(changed, c => c.Id == OneB && c.Removed && c.Name == "One B");
        Assert.Empty(file.ChangesFor(file.Plates[1], removedOneB));
    }

    [Fact]
    public void A_file_from_a_newer_game_version_is_refused() =>
        Assert.Equal(ShareAge.Newer, SetupJson.Import(SetupJson.Export([new(null, Plate(OneA))], V210)).AgeAgainst(V200));
}

public class SetupJsonTests
{
    [Fact]
    public void Plates_survive_a_round_trip()
    {
        SharedPlate[] plates =
        [
            new("Time trial", new GadgetPlate([TwoA, OneA], superGadgetId: OneB), new PlateRef(2, 9)),
            new(null, GadgetPlate.Empty),
        ];

        var imported = SetupJson.Import(SetupJson.Export(plates, Catalog)).Plates;

        Assert.Equal(2, imported.Count);
        Assert.Equal("Time trial", imported[0].Name);
        Assert.Equal(new PlateRef(2, 9), imported[0].From);
        Assert.True(imported[0].Plate.SameGadgetsAs(plates[0].Plate));
        Assert.Null(imported[1].Name);
        Assert.Null(imported[1].From);
        Assert.Empty(imported[1].Plate.GadgetIds);
    }

    [Fact]
    public void The_export_is_readable_and_records_the_game()
    {
        var catalog = new GadgetCatalog(Catalog.Gadgets, gameVersion: GameVersion.Parse("2.0.0"));
        var json = SetupJson.Export([new("Time trial", Plate(TwoA), new PlateRef(1, 0))], catalog);

        Assert.Contains("\"format\": \"gadgettool.setups\"", json, StringComparison.Ordinal);
        Assert.Contains("\"gameVersion\": \"2.0.0\"", json, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"Time trial\"", json, StringComparison.Ordinal);
        Assert.Contains("\"preset\": \"A\"", json, StringComparison.Ordinal);
        Assert.Contains("\"slotCost\": 2", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"super\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "format": "gadgettool.setups", "version": 1, "plates": [ { "player": 5, "preset": "A", "gadgets": [] } ] }""")]
    [InlineData("""{ "format": "gadgettool.setups", "version": 1, "plates": [ { "player": 1, "preset": "AB", "gadgets": [] } ] }""")]
    [InlineData("""{ "format": "gadgettool.setups", "version": 1, "plates": [ { "player": 1, "gadgets": [] } ] }""")]
    public void A_bad_origin_is_refused(string json) =>
        Assert.Throws<FormatException>(() => SetupJson.Import(json));

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{ "format": "something.else", "version": 1, "plates": [] }""")]
    [InlineData("""{ "format": "gadgettool.setups", "version": 2, "plates": [] }""")]
    [InlineData("""{ "format": "gadgettool.setups", "version": 1 }""")]
    [InlineData("""{ "format": "gadgettool.setups", "version": 1, "plates": [ {} ] }""")]
    [InlineData("""{ "format": "gadgettool.setups", "version": 1, "plates": [ { "gadgets": [""] } ] }""")]
    public void Bad_files_are_refused(string json) =>
        Assert.Throws<FormatException>(() => SetupJson.Import(json));

    [Fact]
    public void Unknown_ids_are_imported_and_left_to_the_paste_to_judge()
    {
        var file = SetupJson.Import("""{ "format": "gadgettool.setups", "version": 1, "plates": [ { "gadgets": ["t.newer"] } ] }""");

        Assert.Equal(["t.newer"], Assert.Single(file.Plates).Plate.GadgetIds);
        Assert.Equal(ShareAge.Unknown, file.AgeAgainst(Catalog));
    }
}
