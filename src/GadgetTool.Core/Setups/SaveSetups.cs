namespace GadgetTool.Core.Setups;

/// <summary>Points at one plate: a player (1 to 4) and the plate's index (0-based).</summary>
public readonly record struct PlateRef(int Player, int Index)
{
    public override string ToString() => $"P{Player} preset {PlayerSetups.PresetLetter(Index)}";
}

/// <summary>
/// The gadget part of a save, as the editor sees it. Everything else in the save stays in the
/// save format's hands and is written back untouched.
/// </summary>
/// <param name="SaveVersion">The save's version as its format reports it.</param>
/// <param name="Players">The local players the save holds, each with a distinct number.</param>
public sealed record SaveSetups(string SaveVersion, IReadOnlyList<PlayerSetups> Players)
{
    public PlayerSetups Player(int number) =>
        Players.FirstOrDefault(p => p.PlayerNumber == number)
        ?? throw new ArgumentOutOfRangeException(nameof(number), number, "The save has no such player.");

    public bool Contains(PlateRef plate) =>
        Players.Any(p => p.PlayerNumber == plate.Player && plate.Index >= 0 && plate.Index < p.Plates.Count);

    public GadgetPlate Plate(PlateRef plate) => Player(plate.Player).Plates[plate.Index];

    /// <summary>
    /// The same setups with every gadget id spelled as <paramref name="catalog"/> spells it
    /// (ids ignore letter case, and a save may spell them differently); ids the catalog does
    /// not have stay as they are.
    /// </summary>
    public SaveSetups WithCatalogSpelling(Gadgets.GadgetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return this with
        {
            Players = [.. Players.Select(p => p with
            {
                Unlocked = p.Unlocked.Select(catalog.Canonical).ToHashSet(Gadgets.GadgetIds.Comparer),
                Plates = [.. p.Plates.Select(plate => new GadgetPlate(
                    plate.GadgetIds.Select(catalog.Canonical),
                    plate.SuperGadgetId is null ? null : catalog.Canonical(plate.SuperGadgetId)))],
            })],
        };
    }

    public SaveSetups WithPlate(PlateRef target, GadgetPlate plate)
    {
        var players = Players.ToArray();
        var i = Array.FindIndex(players, p => p.PlayerNumber == target.Player);
        if (i < 0)
            throw new ArgumentOutOfRangeException(nameof(target), target, "The save has no such player.");

        players[i] = players[i].WithPlate(target.Index, plate);
        return this with { Players = players };
    }
}
