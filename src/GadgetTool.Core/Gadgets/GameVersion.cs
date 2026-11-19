using System.Globalization;

namespace GadgetTool.Core.Gadgets;

/// <summary>A share's game version against ours.</summary>
public enum ShareAge
{
    /// <summary>Same version: nothing to check.</summary>
    Same,

    /// <summary>Made with an older version: its gadgets are checked against ours.</summary>
    Older,

    /// <summary>Made with a newer version than ours: not compatible, refused.</summary>
    Newer,

    /// <summary>One of the versions is unknown: its gadgets are checked.</summary>
    Unknown,
}

/// <summary>
/// The game's version, "major.minor.patch" as the game's updates are numbered (1.3.1, 1.4.1,
/// 2.0.0...). Shares carry it, so an import from another version can say so: a rebalance can
/// change a gadget's slot cost or remove it. The default value means "unknown".
/// </summary>
public readonly record struct GameVersion(byte Major, byte Minor, byte Patch) : IComparable<GameVersion>
{
    public static GameVersion Unknown => default;

    public bool IsKnown => this != default;

    public int CompareTo(GameVersion other) =>
        Major != other.Major ? Major.CompareTo(other.Major)
        : Minor != other.Minor ? Minor.CompareTo(other.Minor)
        : Patch.CompareTo(other.Patch);

    /// <summary>
    /// How a share's game version stands against ours. A share from a newer version cannot be
    /// imported (it may hold gadgets or costs this version does not have); an older one is
    /// checked gadget by gadget; without both versions, the gadgets are checked too.
    /// </summary>
    public static ShareAge AgeOf(GameVersion shared, GameVersion ours) =>
        !shared.IsKnown || !ours.IsKnown ? ShareAge.Unknown
        : shared.CompareTo(ours) switch { < 0 => ShareAge.Older, 0 => ShareAge.Same, _ => ShareAge.Newer };

    /// <summary>"2.0.0", "2.0" or "2"; anything else is <see cref="Unknown"/>.</summary>
    public static GameVersion Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Unknown;

        var parts = text.Trim().Split('.');
        if (parts.Length > 3)
            return Unknown;

        var numbers = new byte[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!byte.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return Unknown;
        }
        return new GameVersion(numbers[0], numbers[1], numbers[2]);
    }

    public override string ToString() => IsKnown ? $"{Major}.{Minor}.{Patch}" : "unknown";
}
