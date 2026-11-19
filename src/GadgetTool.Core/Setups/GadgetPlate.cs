namespace GadgetTool.Core.Setups;

/// <summary>
/// One gadget setup (a preset): the gadgets in the plate's rows, in order, and the gadget in
/// the Super Slot if any. Immutable, so a plate can be copied, kept on a clipboard or in an
/// undo history without being changed behind its back.
/// </summary>
public sealed class GadgetPlate
{
    public static GadgetPlate Empty { get; } = new([]);

    public GadgetPlate(IEnumerable<string> gadgetIds, string? superGadgetId = null)
    {
        ArgumentNullException.ThrowIfNull(gadgetIds);
        GadgetIds = [.. gadgetIds];
        SuperGadgetId = string.IsNullOrEmpty(superGadgetId) ? null : superGadgetId;
    }

    /// <summary>The gadgets in the rows.</summary>
    public IReadOnlyList<string> GadgetIds { get; }

    /// <summary>The gadget in the Super Slot, or null.</summary>
    public string? SuperGadgetId { get; }

    public GadgetPlate WithSuperGadget(string? superGadgetId) => new(GadgetIds, superGadgetId);

    public bool SameGadgetsAs(GadgetPlate other) =>
        Gadgets.GadgetIds.Same(SuperGadgetId, other.SuperGadgetId)
        && GadgetIds.SequenceEqual(other.GadgetIds, Gadgets.GadgetIds.Comparer);

    public override string ToString() =>
        $"[{string.Join(", ", GadgetIds)}]{(SuperGadgetId is null ? "" : $" super: {SuperGadgetId}")}";
}
