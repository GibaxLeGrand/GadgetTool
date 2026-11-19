namespace GadgetTool.Core.Gadgets;

/// <summary>
/// One gadget as the game defines it.
/// </summary>
/// <param name="Id">The internal identifier the save uses. Opaque: never shown to players.</param>
/// <param name="Name">The display name, in the player's language.</param>
/// <param name="SlotCost">How many plate slots the gadget takes (1 or more).</param>
/// <param name="Category">The group the game lists it under (Rings, Items...), for display.</param>
/// <param name="Description">What it does, for display.</param>
public sealed record GadgetDefinition(string Id, string Name, int SlotCost, string Category = "", string? Description = null);
