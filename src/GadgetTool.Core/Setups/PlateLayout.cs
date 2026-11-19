namespace GadgetTool.Core.Setups;

/// <summary>
/// The shape of a player's gadget plate: rows of up to 3 slots (the Master plate is a 3x2
/// grid), plus the Super Slot on the Grand Master plate (game version 2.0), which takes one
/// 1-slot gadget and amplifies its effect.
/// </summary>
/// <remarks>
/// Assumption to confirm on real data: a 2- or 3-slot gadget lies within one row. How the
/// lower plates (2 to 5 slots) spread over the rows is up to the save format to report.
/// </remarks>
public sealed class PlateLayout
{
    public const int MaxRowSize = 3;

    public PlateLayout(IEnumerable<int> rowSizes, bool hasSuperSlot)
    {
        ArgumentNullException.ThrowIfNull(rowSizes);
        RowSizes = [.. rowSizes];
        if (RowSizes.Any(size => size is < 0 or > MaxRowSize))
            throw new ArgumentException($"A row holds 0 to {MaxRowSize} slots.", nameof(rowSizes));
        HasSuperSlot = hasSuperSlot;
    }

    /// <summary>Master plate: 6 slots in two rows of 3.</summary>
    public static PlateLayout Master { get; } = new([3, 3], hasSuperSlot: false);

    /// <summary>Grand Master plate: the Master plate plus the Super Slot.</summary>
    public static PlateLayout GrandMaster { get; } = new([3, 3], hasSuperSlot: true);

    public IReadOnlyList<int> RowSizes { get; }

    public bool HasSuperSlot { get; }

    /// <summary>Slots in the rows (the Super Slot not counted).</summary>
    public int SlotCount => RowSizes.Sum();

    public bool SameAs(PlateLayout other) => HasSuperSlot == other.HasSuperSlot && RowSizes.SequenceEqual(other.RowSizes);

    /// <summary>
    /// Finds a row for each gadget, given their slot costs (in plate order), or returns null
    /// when they cannot all fit. For six slots this is a handful of tries.
    /// </summary>
    public int[]? Arrange(IReadOnlyList<int> slotCosts)
    {
        ArgumentNullException.ThrowIfNull(slotCosts);
        if (slotCosts.Any(cost => cost < 1))
            throw new ArgumentException("A gadget takes at least one slot.", nameof(slotCosts));
        if (slotCosts.Sum() > SlotCount)
            return null;

        // Biggest gadgets first: they have the fewest rows to choose from.
        var order = Enumerable.Range(0, slotCosts.Count).OrderByDescending(i => slotCosts[i]).ToArray();
        var free = RowSizes.ToArray();
        var rows = new int[slotCosts.Count];
        return Place(0) ? rows : null;

        bool Place(int k)
        {
            if (k == order.Length)
                return true;

            var gadget = order[k];
            for (var row = 0; row < free.Length; row++)
            {
                if (free[row] < slotCosts[gadget])
                    continue;

                free[row] -= slotCosts[gadget];
                rows[gadget] = row;
                if (Place(k + 1))
                    return true;
                free[row] += slotCosts[gadget];
            }
            return false;
        }
    }

    public override string ToString() => $"[{string.Join("+", RowSizes)}]{(HasSuperSlot ? " + Super Slot" : "")}";
}
