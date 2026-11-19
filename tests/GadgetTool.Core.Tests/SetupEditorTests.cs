using GadgetTool.Core.Editing;
using GadgetTool.Core.Rules;
using GadgetTool.Core.Setups;
using static GadgetTool.Core.Tests.TestData;

namespace GadgetTool.Core.Tests;

public class SetupEditorTests
{
    private static readonly PlateRef P1A = new(1, 0);
    private static readonly PlateRef P1B = new(1, 1);
    private static readonly PlateRef P1J = new(1, 9);
    private static readonly PlateRef P2A = new(2, 0);

    [Fact]
    public void Copies_a_plate_to_another_preset_of_the_same_player()
    {
        var plate = new GadgetPlate([TwoA, OneA], superGadgetId: OneB);
        var save = Save().WithPlate(P1A, plate);

        var result = SetupEditor.Copy(save, P1A, P1B, Catalog);

        Assert.True(result.Applied);
        Assert.True(result.Save.Plate(P1B).SameGadgetsAs(plate));
        Assert.True(result.Save.Plate(P1A).SameGadgetsAs(plate));
    }

    [Fact]
    public void Pastes_one_plate_on_many_presets_and_players()
    {
        var plate = Plate(TwoA, OneA);

        var result = SetupEditor.Paste(Save(), plate, [P1A, P1J, P2A], Catalog);

        Assert.True(result.Applied);
        Assert.All([P1A, P1J, P2A], target => Assert.True(result.Save.Plate(target).SameGadgetsAs(plate)));
        Assert.Equal(3, result.Targets.Count);
    }

    [Fact]
    public void Strict_paste_refuses_a_gadget_the_target_player_does_not_own()
    {
        var save = Save();

        var result = SetupEditor.Paste(save, Plate(OneC), P2A, Catalog);

        Assert.False(result.Applied);
        Assert.Same(save, result.Save);
        var outcome = Assert.Single(result.Targets);
        Assert.Equal(new PlateIssue(PlateIssueKind.NotUnlocked, OneC), Assert.Single(outcome.Issues));
    }

    [Fact]
    public void A_paste_on_many_targets_is_all_or_nothing()
    {
        var save = Save();

        // P1 owns OneC, P2 does not: nothing is pasted, not even on P1.
        var result = SetupEditor.Paste(save, Plate(OneC), [P1A, P2A], Catalog);

        Assert.False(result.Applied);
        Assert.Same(save, result.Save);
        Assert.Empty(result.Targets.Single(t => t.Target == P1A).Issues);
        Assert.NotEmpty(result.Targets.Single(t => t.Target == P2A).Issues);
    }

    [Fact]
    public void Drop_missing_leaves_out_what_the_target_cannot_take()
    {
        // P2 owns OneA, OneB and TwoA, and has no Super Slot.
        var plate = new GadgetPlate([TwoA, OneC, OneA, Locked], superGadgetId: OneB);

        var result = SetupEditor.Paste(Save(), plate, P2A, Catalog, PasteMode.DropMissing);

        Assert.True(result.Applied);
        Assert.True(result.Save.Plate(P2A).SameGadgetsAs(Plate(TwoA, OneA)));
        Assert.Equal([OneC, Locked, OneB], Assert.Single(result.Targets).Dropped);
    }

    [Fact]
    public void Drop_missing_still_refuses_what_does_not_fit()
    {
        var lowPlayer = Player(3, new PlateLayout([3, 1], hasSuperSlot: false));
        var save = new SaveSetups("dev-1", [Player(1, PlateLayout.GrandMaster), lowPlayer]);

        var result = SetupEditor.Paste(save, Plate(TwoA, TwoB), new PlateRef(3, 0), Catalog, PasteMode.DropMissing);

        Assert.False(result.Applied);
        Assert.Equal(PlateIssueKind.DoesNotFit, Assert.Single(Assert.Single(result.Targets).Issues).Kind);
    }

    [Fact]
    public void Pasting_on_a_preset_that_does_not_exist_is_a_bug() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SetupEditor.Paste(Save(), Plate(OneA), new PlateRef(1, 10), Catalog));

    [Theory]
    [InlineData(0, "A")]
    [InlineData(9, "J")]
    public void Presets_are_lettered_like_in_the_game(int index, string letter) =>
        Assert.Equal(letter, PlayerSetups.PresetLetter(index));
}
