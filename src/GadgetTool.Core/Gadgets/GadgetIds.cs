namespace GadgetTool.Core.Gadgets;

/// <summary>
/// Gadget ids are Unreal Engine names, and Unreal names ignore letter case: the game's own
/// files spell one id in different ways ("Run45_..." in a table's row names, "run45_..." in
/// its id field and in saves). Every comparison of gadget ids goes through here. Inside the
/// tool, ids are spelled as the catalog spells them (see <see cref="GadgetCatalog.Canonical"/>).
/// </summary>
public static class GadgetIds
{
    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    public static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
