using GadgetTool.Core.Gadgets;
using GadgetTool.Core.Rules;
using GadgetTool.Core.Saves;
using GadgetTool.Core.Setups;
using GadgetTool.Core.Sharing;
using static GadgetTool.Core.Tests.TestData;

namespace GadgetTool.Core.Tests;

/// <summary>Gadget ids are Unreal names: letter case does not tell two gadgets apart.</summary>
public class GadgetIdTests
{
    private static string Shout(string id) => id.ToUpperInvariant();

    [Fact]
    public void The_catalog_finds_a_gadget_whatever_the_case_and_gives_its_spelling()
    {
        Assert.True(Catalog.TryGet(Shout(TwoA), out var gadget));
        Assert.Equal(TwoA, gadget.Id);
        Assert.Equal(TwoA, Catalog.Canonical(Shout(TwoA)));
        Assert.Equal("t.not_there", Catalog.Canonical("t.not_there"));
    }

    [Fact]
    public void A_catalog_refuses_ids_that_differ_only_by_case()
    {
        var e = Assert.Throws<ArgumentException>(() => new GadgetCatalog([new("t.same", "A", 1), new("T.SAME", "B", 1)]));
        Assert.Contains("ignore letter case", e.Message);
    }

    [Fact]
    public void A_plate_is_unchanged_when_only_the_case_differs() =>
        Assert.True(new GadgetPlate([OneA, TwoA], OneB).SameGadgetsAs(new GadgetPlate([Shout(OneA), Shout(TwoA)], Shout(OneB))));

    [Fact]
    public void The_same_gadget_twice_in_another_case_is_a_duplicate()
    {
        var player = Player(1, PlateLayout.GrandMaster);
        var issues = PlateValidator.Validate(new GadgetPlate([OneA, Shout(OneA)]), player, Catalog);
        Assert.Contains(issues, i => i.Kind == PlateIssueKind.Duplicate);
    }

    [Fact]
    public void Opening_a_save_spells_ids_as_the_catalog_does()
    {
        using var temp = new TempFolder();
        var path = temp.File("save.json");
        System.IO.File.WriteAllText(path, DevSaveJson.Replace(OneA, Shout(OneA), StringComparison.Ordinal));

        var session = SaveSession.Open(path, new DevJsonSaveFormat(), Catalog);

        var p1 = session.Current.Player(1);
        Assert.Contains(OneA, p1.Unlocked);
        Assert.DoesNotContain(Shout(OneA), p1.Unlocked, StringComparer.Ordinal);
        Assert.Equal([TwoA, OneA], p1.Plates[0].GadgetIds);
        Assert.Empty(session.Problems());
        Assert.False(session.HasChanges);
    }

    [Fact]
    public void A_share_code_does_not_depend_on_the_case_of_the_ids()
    {
        var plate = new GadgetPlate([OneA, TwoA], OneB);
        var shouted = new GadgetPlate([Shout(OneA), Shout(TwoA)], Shout(OneB));

        Assert.Equal(ShareCode.Encode(plate, Catalog), ShareCode.Encode(shouted, Catalog));
    }
}
