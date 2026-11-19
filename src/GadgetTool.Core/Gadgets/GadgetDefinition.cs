namespace GadgetTool.Core.Gadgets;

/// <summary>
/// One gadget as the game defines it.
/// </summary>
/// <param name="Id">The internal identifier the save uses. Opaque: never shown to players.</param>
/// <param name="Name">The display name, in the player's language.</param>
/// <param name="SlotCost">How many plate slots the gadget takes (1 or more).</param>
public sealed record GadgetDefinition(string Id, string Name, int SlotCost);
