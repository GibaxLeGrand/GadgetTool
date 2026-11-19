using GadgetTool.Core.Gadgets;
using GadgetTool.Core.Setups;

namespace GadgetTool.Core.Rules;

public enum PlateIssueKind
{
    /// <summary>The id is not in the catalog (a newer game version, or a broken import).</summary>
    UnknownGadget,

    /// <summary>The player has not unlocked this gadget. The tool never unlocks anything.</summary>
    NotUnlocked,

    /// <summary>The same gadget is equipped twice on one plate (Super Slot included).</summary>
    Duplicate,

    /// <summary>The gadgets take more slots than the player's plate has.</summary>
    OverCapacity,

    /// <summary>Enough slots in total, but the gadgets cannot be laid out in the rows.</summary>
    DoesNotFit,

    /// <summary>A Super Slot gadget is set, but the player's plate has no Super Slot.</summary>
    NoSuperSlot,

    /// <summary>Only a 1-slot gadget can go in the Super Slot.</summary>
    SuperSlotNeedsOneSlotGadget,
}

/// <param name="GadgetId">The gadget concerned; null for the slot-count issues.</param>
/// <param name="SlotsUsed">For <see cref="PlateIssueKind.OverCapacity"/>: slots the gadgets would take.</param>
/// <param name="SlotCount">For <see cref="PlateIssueKind.OverCapacity"/>: slots the plate has.</param>
public sealed record PlateIssue(PlateIssueKind Kind, string? GadgetId = null, int SlotsUsed = 0, int SlotCount = 0)
{
    public override string ToString() => Kind switch
    {
        PlateIssueKind.UnknownGadget => $"Unknown gadget '{GadgetId}'.",
        PlateIssueKind.NotUnlocked => $"Gadget '{GadgetId}' is not unlocked.",
        PlateIssueKind.Duplicate => $"Gadget '{GadgetId}' is equipped twice.",
        PlateIssueKind.OverCapacity => $"The gadgets take {SlotsUsed} slots; the plate has {SlotCount}.",
        PlateIssueKind.DoesNotFit => "The gadgets cannot be laid out in the plate's rows.",
        PlateIssueKind.NoSuperSlot => $"Gadget '{GadgetId}' is in the Super Slot, but this plate has none.",
        PlateIssueKind.SuperSlotNeedsOneSlotGadget => $"Gadget '{GadgetId}' takes more than one slot; the Super Slot takes 1-slot gadgets only.",
        _ => Kind.ToString(),
    };
}

/// <summary>
/// The rules a plate must follow before it can go into a save.
/// </summary>
public static class PlateValidator
{
    /// <summary>Every rule the plate breaks for this player; empty when the plate is valid.</summary>
    public static IReadOnlyList<PlateIssue> Validate(GadgetPlate plate, PlayerSetups player, GadgetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(plate);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(catalog);

        var issues = new List<PlateIssue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var costs = new List<int>();

        foreach (var id in plate.GadgetIds)
        {
            if (CheckGadget(id, player, catalog, seen, issues) is { } gadget)
                costs.Add(gadget.SlotCost);
        }

        if (plate.SuperGadgetId is { } superId)
        {
            if (!player.Layout.HasSuperSlot)
                issues.Add(new PlateIssue(PlateIssueKind.NoSuperSlot, superId));
            if (CheckGadget(superId, player, catalog, seen, issues) is { SlotCost: > 1 })
                issues.Add(new PlateIssue(PlateIssueKind.SuperSlotNeedsOneSlotGadget, superId));
        }

        var slotsUsed = costs.Sum();
        if (slotsUsed > player.Layout.SlotCount)
            issues.Add(new PlateIssue(PlateIssueKind.OverCapacity, SlotsUsed: slotsUsed, SlotCount: player.Layout.SlotCount));
        else if (player.Layout.Arrange(costs) is null)
            issues.Add(new PlateIssue(PlateIssueKind.DoesNotFit));

        return issues;
    }

    /// <summary>Slots the known gadgets in the plate's rows take (unknown ids count for nothing).</summary>
    public static int SlotsUsed(GadgetPlate plate, GadgetCatalog catalog) =>
        plate.GadgetIds.Distinct(StringComparer.Ordinal)
            .Sum(id => catalog.TryGet(id, out var g) ? g.SlotCost : 0);

    /// <summary>Records the issues of one gadget; returns its definition when it counts toward the slots.</summary>
    private static GadgetDefinition? CheckGadget(
        string id, PlayerSetups player, GadgetCatalog catalog, HashSet<string> seen, List<PlateIssue> issues)
    {
        if (!seen.Add(id))
        {
            issues.Add(new PlateIssue(PlateIssueKind.Duplicate, id));
            return null;
        }

        if (!catalog.TryGet(id, out var gadget))
        {
            issues.Add(new PlateIssue(PlateIssueKind.UnknownGadget, id));
            return null;
        }

        if (!player.Unlocked.Contains(id))
            issues.Add(new PlateIssue(PlateIssueKind.NotUnlocked, id));

        return gadget;
    }
}
