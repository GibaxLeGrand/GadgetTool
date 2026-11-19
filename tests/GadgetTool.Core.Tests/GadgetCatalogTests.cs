using GadgetTool.Core.Gadgets;

namespace GadgetTool.Core.Tests;

public class GadgetCatalogTests
{
    [Fact]
    public void Finds_a_gadget_by_id()
    {
        Assert.True(TestData.Catalog.TryGet(TestData.TwoA, out var gadget));
        Assert.Equal(2, gadget.SlotCost);
        Assert.False(TestData.Catalog.TryGet("t.nope", out _));
    }

    [Fact]
    public void Refuses_a_repeated_id() =>
        Assert.Throws<ArgumentException>(() => new GadgetCatalog([new("x", "X", 1), new("x", "X again", 2)]));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Refuses_a_gadget_taking_no_slot(int cost) =>
        Assert.Throws<ArgumentException>(() => new GadgetCatalog([new("x", "X", cost)]));

    [Fact]
    public void Refuses_an_empty_id() =>
        Assert.Throws<ArgumentException>(() => new GadgetCatalog([new(" ", "Blank", 1)]));
}
