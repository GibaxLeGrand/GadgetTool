using GadgetTool.Core.Setups;

namespace GadgetTool.Core.Tests;

public class PlateLayoutTests
{
    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 2, 1, 2, 1 })]
    [InlineData(new[] { 3, 3 })]
    [InlineData(new[] { 3, 2, 1 })]
    [InlineData(new[] { 1, 1, 1, 1, 1, 1 })]
    [InlineData(new[] { 1, 2, 2, 1 })]
    public void Master_plate_takes(int[] costs) => Assert.NotNull(PlateLayout.Master.Arrange(costs));

    [Theory]
    [InlineData(new[] { 2, 2, 2 })] // 6 slots, but a row of 3 cannot take two 2-slot gadgets
    [InlineData(new[] { 3, 2, 2 })]
    [InlineData(new[] { 1, 1, 1, 1, 1, 1, 1 })]
    public void Master_plate_refuses(int[] costs) => Assert.Null(PlateLayout.Master.Arrange(costs));

    [Fact]
    public void Each_gadget_gets_a_row_with_room_for_it()
    {
        int[] costs = [1, 2, 1, 2];
        var rows = PlateLayout.Master.Arrange(costs);

        Assert.NotNull(rows);
        for (var row = 0; row < 2; row++)
            Assert.True(costs.Where((_, i) => rows[i] == row).Sum() <= 3);
    }

    [Fact]
    public void A_lower_plate_follows_its_own_rows()
    {
        var silver = new PlateLayout([3, 1], hasSuperSlot: false);

        Assert.Equal(4, silver.SlotCount);
        Assert.NotNull(silver.Arrange([2, 1, 1]));
        Assert.Null(silver.Arrange([2, 2]));
    }

    [Fact]
    public void The_super_slot_is_not_counted_in_the_rows()
    {
        Assert.Equal(6, PlateLayout.GrandMaster.SlotCount);
        Assert.True(PlateLayout.GrandMaster.HasSuperSlot);
        Assert.False(PlateLayout.Master.HasSuperSlot);
    }

    [Fact]
    public void A_row_holds_at_most_three_slots() =>
        Assert.Throws<ArgumentException>(() => new PlateLayout([4], hasSuperSlot: false));
}
