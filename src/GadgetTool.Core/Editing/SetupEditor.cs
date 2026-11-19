using GadgetTool.Core.Gadgets;
using GadgetTool.Core.Rules;
using GadgetTool.Core.Setups;

namespace GadgetTool.Core.Editing;

public enum PasteMode
{
    /// <summary>Refuse the paste if the plate breaks a rule for any target.</summary>
    Strict,

    /// <summary>
    /// Leave out, for each target, the gadgets its player cannot equip (not unlocked, unknown,
    /// repeated, or a Super Slot gadget the target's plate cannot take), then paste what is
    /// left. Still refused if what is left does not fit.
    /// </summary>
    DropMissing,
}

/// <param name="Target">The plate pasted to.</param>
/// <param name="Issues">Why this target refuses the plate; empty when it accepts it.</param>
/// <param name="Dropped">Gadgets left out for this target (<see cref="PasteMode.DropMissing"/> only).</param>
public sealed record PasteOutcome(PlateRef Target, IReadOnlyList<PlateIssue> Issues, IReadOnlyList<string> Dropped);

/// <param name="Applied">True when every target accepted the plate.</param>
/// <param name="Save">The save after the paste; the unchanged save when <see cref="Applied"/> is false.</param>
/// <param name="Targets">One outcome per distinct target, in the order given.</param>
public sealed record PasteResult(bool Applied, SaveSetups Save, IReadOnlyList<PasteOutcome> Targets);

/// <summary>
/// Copy and paste of plates. A copy is just a <see cref="GadgetPlate"/> (immutable); a paste
/// puts it on one or many plates, of any player, all or nothing.
/// </summary>
public static class SetupEditor
{
    public static PasteResult Paste(
        SaveSetups save, GadgetPlate plate, PlateRef target, GadgetCatalog catalog, PasteMode mode = PasteMode.Strict) =>
        Paste(save, plate, [target], catalog, mode);

    public static PasteResult Paste(
        SaveSetups save, GadgetPlate plate, IEnumerable<PlateRef> targets, GadgetCatalog catalog, PasteMode mode = PasteMode.Strict)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(plate);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(catalog);

        var outcomes = new List<PasteOutcome>();
        var updated = save;

        foreach (var target in targets.Distinct())
        {
            if (!save.Contains(target))
                throw new ArgumentOutOfRangeException(nameof(targets), target, "The save has no such plate.");

            var player = save.Player(target.Player);
            var (toPaste, dropped) = mode == PasteMode.DropMissing
                ? KeepEquippable(plate, player, catalog)
                : (plate, []);

            var issues = PlateValidator.Validate(toPaste, player, catalog);
            outcomes.Add(new PasteOutcome(target, issues, dropped));

            if (issues.Count == 0)
                updated = updated.WithPlate(target, toPaste);
        }

        var applied = outcomes.All(o => o.Issues.Count == 0);
        return new PasteResult(applied, applied ? updated : save, outcomes);
    }

    /// <summary>Copies one plate onto another (same player or not).</summary>
    public static PasteResult Copy(
        SaveSetups save, PlateRef from, PlateRef to, GadgetCatalog catalog, PasteMode mode = PasteMode.Strict) =>
        Paste(save, save.Plate(from), [to], catalog, mode);

    private static (GadgetPlate Plate, IReadOnlyList<string> Dropped) KeepEquippable(
        GadgetPlate plate, PlayerSetups player, GadgetCatalog catalog)
    {
        var kept = new List<string>();
        var dropped = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in plate.GadgetIds)
        {
            if (seen.Add(id) && catalog.TryGet(id, out _) && player.Unlocked.Contains(id))
                kept.Add(id);
            else
                dropped.Add(id);
        }

        var super = plate.SuperGadgetId;
        if (super is not null
            && !(player.Layout.HasSuperSlot
                 && seen.Add(super)
                 && catalog.TryGet(super, out var gadget)
                 && gadget.SlotCost == 1
                 && player.Unlocked.Contains(super)))
        {
            dropped.Add(super);
            super = null;
        }

        return (dropped.Count == 0 ? plate : new GadgetPlate(kept, super), dropped);
    }
}
