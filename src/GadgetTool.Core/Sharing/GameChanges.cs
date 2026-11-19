using GadgetTool.Core.Gadgets;

namespace GadgetTool.Core.Sharing;

/// <summary>A gadget as a share saw it, compared with the game now.</summary>
/// <param name="Id">The gadget's id.</param>
/// <param name="Name">Its name: today's, or the one the share recorded, or null if neither is known.</param>
/// <param name="SlotCostThen">Its slot cost when shared, or null if the share did not record it.</param>
/// <param name="SlotCostNow">Its slot cost now, or null when the game no longer has it.</param>
public sealed record GadgetChange(string Id, string? Name, int? SlotCostThen, int? SlotCostNow)
{
    public bool Removed => SlotCostNow is null;

    public override string ToString() => Removed
        ? $"{Name ?? Id} is not in this game version."
        : $"{Name ?? Id}: {SlotCostThen} slot{(SlotCostThen == 1 ? "" : "s")} when shared, {SlotCostNow} now.";
}

/// <summary>
/// What a game update changed for the gadgets of a share (a rebalance can change a slot cost
/// or remove a gadget). The paste still checks every rule; this says why it may refuse.
/// </summary>
public static class GameChanges
{
    /// <param name="then">Gadget ids with their slot cost (and name, if known) as the share recorded them.</param>
    /// <param name="now">Today's catalog.</param>
    public static IReadOnlyList<GadgetChange> Compare(IEnumerable<(string Id, string? Name, int SlotCost)> then, GadgetCatalog now)
    {
        ArgumentNullException.ThrowIfNull(then);
        ArgumentNullException.ThrowIfNull(now);

        var changes = new List<GadgetChange>();
        foreach (var (id, name, cost) in then.DistinctBy(g => g.Id, GadgetIds.Comparer))
        {
            if (!now.TryGet(id, out var gadget))
                changes.Add(new GadgetChange(id, name, cost, null));
            else if (gadget.SlotCost != cost)
                changes.Add(new GadgetChange(id, gadget.Name, cost, gadget.SlotCost));
        }
        return changes;
    }
}
