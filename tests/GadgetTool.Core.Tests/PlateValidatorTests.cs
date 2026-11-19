using GadgetTool.Core.Rules;
using GadgetTool.Core.Setups;
using static GadgetTool.Core.Tests.TestData;

namespace GadgetTool.Core.Tests;

public class PlateValidatorTests
{
    private static readonly PlayerSetups GrandMaster = Player(1, PlateLayout.GrandMaster);
    private static readonly PlayerSetups Master = Player(2, PlateLayout.Master);

    private static IReadOnlyList<PlateIssue> Check(GadgetPlate plate, PlayerSetups player) =>
        PlateValidator.Validate(plate, player, Catalog);

    [Fact]
    public void A_full_grand_master_plate_is_valid() =>
        Assert.Empty(Check(new GadgetPlate([TwoA, OneA, TwoB, OneB], superGadgetId: OneC), GrandMaster));

    [Fact]
    public void An_empty_plate_is_valid() => Assert.Empty(Check(GadgetPlate.Empty, Master));

    [Fact]
    public void Too_many_slots()
    {
        var issue = Assert.Single(Check(Plate(ThreeA, TwoA, TwoB), Master));
        Assert.Equal(PlateIssueKind.OverCapacity, issue.Kind);
        Assert.Equal(7, issue.SlotsUsed);
        Assert.Equal(6, issue.SlotCount);
    }

    [Fact]
    public void Enough_slots_but_no_room_in_the_rows()
    {
        var issue = Assert.Single(Check(Plate(TwoA, TwoB, TwoC), Master));
        Assert.Equal(PlateIssueKind.DoesNotFit, issue.Kind);
    }

    [Fact]
    public void A_gadget_the_player_does_not_own()
    {
        var issue = Assert.Single(Check(Plate(Locked), Master));
        Assert.Equal(new PlateIssue(PlateIssueKind.NotUnlocked, Locked), issue);
    }

    [Fact]
    public void A_gadget_the_catalog_does_not_know()
    {
        var issue = Assert.Single(Check(Plate("t.from_a_newer_patch"), Master));
        Assert.Equal(PlateIssueKind.UnknownGadget, issue.Kind);
    }

    [Fact]
    public void The_same_gadget_twice()
    {
        var issue = Assert.Single(Check(Plate(OneA, OneA), Master));
        Assert.Equal(new PlateIssue(PlateIssueKind.Duplicate, OneA), issue);
    }

    [Fact]
    public void The_same_gadget_in_a_row_and_in_the_super_slot()
    {
        var issue = Assert.Single(Check(new GadgetPlate([OneA], superGadgetId: OneA), GrandMaster));
        Assert.Equal(new PlateIssue(PlateIssueKind.Duplicate, OneA), issue);
    }

    [Fact]
    public void A_super_slot_gadget_on_a_plate_without_one()
    {
        var issue = Assert.Single(Check(new GadgetPlate([], superGadgetId: OneA), Master));
        Assert.Equal(new PlateIssue(PlateIssueKind.NoSuperSlot, OneA), issue);
    }

    [Fact]
    public void A_two_slot_gadget_in_the_super_slot()
    {
        var issue = Assert.Single(Check(new GadgetPlate([], superGadgetId: TwoA), GrandMaster));
        Assert.Equal(new PlateIssue(PlateIssueKind.SuperSlotNeedsOneSlotGadget, TwoA), issue);
    }

    [Fact]
    public void The_super_slot_does_not_take_a_row_slot() =>
        Assert.Empty(Check(new GadgetPlate([ThreeA, TwoA, OneD], superGadgetId: OneC), GrandMaster));

    [Fact]
    public void Every_issue_is_reported_at_once()
    {
        var kinds = Check(Plate(Locked, Locked, "t.unknown", ThreeA, ThreeA), Master).Select(i => i.Kind).ToList();

        Assert.Contains(PlateIssueKind.NotUnlocked, kinds);
        Assert.Contains(PlateIssueKind.Duplicate, kinds);
        Assert.Contains(PlateIssueKind.UnknownGadget, kinds);
    }

    [Fact]
    public void Slots_used_counts_known_gadgets_once() =>
        Assert.Equal(3, PlateValidator.SlotsUsed(Plate(TwoA, OneA, OneA, "t.unknown"), Catalog));
}
