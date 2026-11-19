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
/// <param name="SuperEffect">What it does in the Super Slot, for display; null when it has no Super effect.</param>
/// <param name="CategoryColor">The game's colour for the category ("#RRGGBB"), for display; null when unknown.</param>
/// <param name="Base">The frame the game draws the icon on: "kit", "mini", or null for the usual one.</param>
public sealed record GadgetDefinition(
    string Id,
    string Name,
    int SlotCost,
    string Category = "",
    string? Description = null,
    string? CategoryId = null,
    string? SuperEffect = null,
    string? CategoryColor = null,
    string? Base = null)
{
    /// <summary>The category's key: the same in every language, for grouping and colours.</summary>
    public string CategoryKey => CategoryId ?? Category;
}
