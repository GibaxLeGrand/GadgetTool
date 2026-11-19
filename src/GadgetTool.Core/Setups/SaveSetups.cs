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
