using GadgetTool.Core.Setups;
using GadgetTool.Core.Sharing;
using static GadgetTool.Core.Tests.TestData;

namespace GadgetTool.Core.Tests;

public class ShareCodeTests
{
    [Fact]
    public void A_plate_survives_a_round_trip()
    {
        var plate = new GadgetPlate([TwoA, OneA, TwoB, OneB], superGadgetId: OneC);

        var decoded = ShareCode.Decode(ShareCode.Encode(plate), Catalog);

        Assert.True(decoded.SameGadgetsAs(plate));
    }

    [Fact]
    public void A_plate_without_super_slot_gadget_survives_a_round_trip()
    {
        var plate = Plate(ThreeA, OneD);

        var decoded = ShareCode.Decode(ShareCode.Encode(plate), Catalog);

        Assert.True(decoded.SameGadgetsAs(plate));
        Assert.Null(decoded.SuperGadgetId);
    }

    [Fact]
    public void Codes_match_the_web_ui()
    {
        // Produced by web/js/backend-mock.js for the same plate: both sides must agree.
        const string fromWebUi = "GT1-AQIB06Lq_83AVX8UvFV8vPs";
        var plate = new GadgetPlate([TwoA, OneA], superGadgetId: OneB);

        Assert.Equal(fromWebUi, ShareCode.Encode(plate));
        Assert.True(ShareCode.Decode(fromWebUi, Catalog).SameGadgetsAs(plate));
    }

    [Fact]
    public void A_full_plate_gives_a_short_code()
    {
        var code = ShareCode.Encode(new GadgetPlate([OneA, OneB, OneC, OneD, "t.five", "t.six"], superGadgetId: "t.seven"));

        Assert.StartsWith(ShareCode.Prefix, code);
        Assert.True(code.Length <= 48, $"{code} is {code.Length} characters long");
    }

    [Fact]
    public void Spaces_around_and_prefix_case_do_not_matter()
    {
        var code = ShareCode.Encode(Plate(OneA));

        var decoded = ShareCode.Decode("  " + code.Replace("GT1-", "gt1-", StringComparison.Ordinal) + "\n", Catalog);

        Assert.True(decoded.SameGadgetsAs(Plate(OneA)));
    }

    [Fact]
    public void A_typo_is_caught()
    {
        var code = ShareCode.Encode(Plate(TwoA, OneA)).ToCharArray();
        code[10] = code[10] == 'A' ? 'B' : 'A';

        Assert.Throws<FormatException>(() => ShareCode.Decode(new string(code), Catalog));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void A_cut_code_is_caught(int missing)
    {
        var code = ShareCode.Encode(Plate(TwoA, OneA));

        Assert.Throws<FormatException>(() => ShareCode.Decode(code[..^missing], Catalog));
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("GT1-")]
    [InlineData("GT1-!!!!")]
    [InlineData("GT9-AAAA")]
    public void Garbage_is_refused(string code) =>
        Assert.Throws<FormatException>(() => ShareCode.Decode(code, Catalog));

    [Fact]
    public void A_gadget_from_a_newer_game_version_is_refused() =>
        Assert.Throws<FormatException>(() => ShareCode.Decode(ShareCode.Encode(Plate("t.not_in_this_catalog")), Catalog));
}

public class SetupJsonTests
{
    [Fact]
    public void Plates_survive_a_round_trip()
    {
        SharedPlate[] plates =
        [
            new("Time trial", new GadgetPlate([TwoA, OneA], superGadgetId: OneB)),
            new(null, GadgetPlate.Empty),
        ];

        var imported = SetupJson.Import(SetupJson.Export(plates));

        Assert.Equal(2, imported.Count);
        Assert.Equal("Time trial", imported[0].Label);
        Assert.True(imported[0].Plate.SameGadgetsAs(plates[0].Plate));
        Assert.Null(imported[1].Label);
        Assert.Empty(imported[1].Plate.GadgetIds);
    }

    [Fact]
    public void The_export_is_readable_and_tagged()
    {
        var json = SetupJson.Export([new("P1 A", Plate(OneA))]);

        Assert.Contains("\"format\": \"gadgettool.setups\"", json, StringComparison.Ordinal);
        Assert.Contains("\"gadgets\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"super\"", json, StringComparison.Ordinal);
    }

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
        var imported = SetupJson.Import("""{ "format": "gadgettool.setups", "version": 1, "plates": [ { "gadgets": ["t.newer"] } ] }""");

        Assert.Equal(["t.newer"], Assert.Single(imported).Plate.GadgetIds);
    }
}
