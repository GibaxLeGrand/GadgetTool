namespace GadgetTool.Core.Gadgets;

/// <summary>
/// One gadget as the game defines it, with its texts in one language.
/// </summary>
/// <param name="Id">The internal identifier the save uses. Opaque: never shown to players.</param>
/// <param name="Name">The display name, in the player's language.</param>
/// <param name="SlotCost">How many plate slots the gadget takes (1 or more).</param>
/// <param name="Category">The group the game lists it under (Rings, Items...), for display.</param>
/// <param name="Description">What it does, for display.</param>
/// <param name="CategoryId">A key for the category that does not change with the language; null means <paramref name="Category"/>.</param>
public sealed record GadgetDefinition(
    string Id,
    string Name,
    int SlotCost,
    string Category = "",
    string? Description = null,
    string? CategoryId = null)
{
    /// <summary>The category's key: the same in every language, for grouping and colours.</summary>
    public string CategoryKey => CategoryId ?? Category;
}
