namespace GadgetTool.Core.Setups;

/// <summary>
/// What the save holds for one local player.
/// </summary>
/// <param name="PlayerNumber">1 to 4.</param>
/// <param name="Layout">The player's plate (it grows with the plate's level).</param>
/// <param name="Unlocked">Ids of the gadgets this player owns. Nothing else may be equipped.</param>
/// <param name="Plates">
/// The player's presets, by index (0-based). How many there are comes from the save (the wiki
/// says 5; Gibax remembers 10, A to J, which may be the 2.0 count).
/// </param>
public sealed record PlayerSetups(
    int PlayerNumber,
    PlateLayout Layout,
    IReadOnlySet<string> Unlocked,
    IReadOnlyList<GadgetPlate> Plates)
{
    public const int MinPlayer = 1;
    public const int MaxPlayer = 4;

    public PlayerSetups WithPlate(int index, GadgetPlate plate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Plates.Count);
        ArgumentNullException.ThrowIfNull(plate);

        var plates = Plates.ToArray();
        plates[index] = plate;
        return this with { Plates = plates };
    }

    /// <summary>The preset's letter as players see it: A for index 0, B for 1...</summary>
    public static string PresetLetter(int index) =>
        index is >= 0 and < 26 ? ((char)('A' + index)).ToString() : (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
