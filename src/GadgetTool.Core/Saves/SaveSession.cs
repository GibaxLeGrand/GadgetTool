using GadgetTool.Core.Gadgets;
using GadgetTool.Core.Rules;
using GadgetTool.Core.Setups;

namespace GadgetTool.Core.Saves;

/// <summary>A plate and what is wrong with it.</summary>
public sealed record PlateProblem(PlateRef Plate, IReadOnlyList<PlateIssue> Issues);

/// <summary>
/// One open save, as the app uses it: open, edit the plates, save. It holds the rules that
/// keep a save safe: only plates can change (never unlocks, plate level or presets), and a
/// changed plate that breaks a rule is never written.
/// </summary>
public sealed class SaveSession
{
    private readonly ISaveFormat _format;
    private readonly GadgetCatalog _catalog;
    private byte[] _savedBytes;

    private SaveSession(string path, ISaveFormat format, GadgetCatalog catalog, byte[] bytes, SaveSetups setups)
    {
        Path = path;
        _format = format;
        _catalog = catalog;
        _savedBytes = bytes;
        Saved = setups;
        Current = setups;
    }

    /// <exception cref="SaveFormatException">The file is not a save this format can read.</exception>
    public static SaveSession Open(string path, ISaveFormat format, GadgetCatalog catalog)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(catalog);

        var bytes = File.ReadAllBytes(path);
        if (!format.CanRead(bytes))
            throw new SaveFormatException($"'{System.IO.Path.GetFileName(path)}' is not a {format.Name} save.");
        return new SaveSession(path, format, catalog, bytes, format.Read(bytes));
    }

    public string Path { get; }

    /// <summary>The setups as they are in the file.</summary>
    public SaveSetups Saved { get; private set; }

    /// <summary>The setups with the edits not saved yet.</summary>
    public SaveSetups Current { get; private set; }

    public IEnumerable<PlateRef> ChangedPlates =>
        from player in Current.Players
        let saved = Saved.Player(player.PlayerNumber)
        from index in Enumerable.Range(0, player.Plates.Count)
        where !player.Plates[index].SameGadgetsAs(saved.Plates[index])
        select new PlateRef(player.PlayerNumber, index);

    public bool HasChanges => ChangedPlates.Any();

    /// <summary>Replaces the current setups with an edited version (from <c>SetupEditor</c> or an undo).</summary>
    /// <exception cref="InvalidOperationException">Something other than plates changed.</exception>
    public void Update(SaveSetups edited)
    {
        ArgumentNullException.ThrowIfNull(edited);
        EnsureOnlyPlatesDiffer(edited);
        Current = edited;
    }

    /// <summary>Every plate that breaks a rule, changed or not (a game patch can make an old plate invalid).</summary>
    public IReadOnlyList<PlateProblem> Problems() =>
        [.. from player in Current.Players
            from index in Enumerable.Range(0, player.Plates.Count)
            let issues = PlateValidator.Validate(player.Plates[index], player, _catalog)
            where issues.Count > 0
            select new PlateProblem(new PlateRef(player.PlayerNumber, index), issues)];

    /// <summary>Writes the changes, after a backup. Returns the backup's path.</summary>
    /// <exception cref="InvalidOperationException">A changed plate breaks a rule.</exception>
    /// <exception cref="GameRunningException">The game is running.</exception>
    /// <exception cref="SaveChangedException">The file changed since it was opened.</exception>
    public string Save(SafeSaveWriter writer, string backupDirectory)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var changed = ChangedPlates.ToHashSet();
        var blocking = Problems().Where(p => changed.Contains(p.Plate)).ToList();
        if (blocking.Count > 0)
            throw new InvalidOperationException(
                "Not saved: " + string.Join(" ", blocking.Select(p => $"{p.Plate}: {string.Join(" ", p.Issues)}")));

        var bytes = _format.Write(_savedBytes, Current);
        var backup = writer.Write(Path, _savedBytes, bytes, backupDirectory);
        _savedBytes = bytes;
        Saved = Current;
        return backup;
    }

    private void EnsureOnlyPlatesDiffer(SaveSetups edited)
    {
        if (edited.SaveVersion != Saved.SaveVersion || edited.Players.Count != Saved.Players.Count)
            throw new InvalidOperationException("Only plates can be edited.");

        foreach (var player in edited.Players)
        {
            var saved = Saved.Players.FirstOrDefault(p => p.PlayerNumber == player.PlayerNumber)
                ?? throw new InvalidOperationException($"Player {player.PlayerNumber} is not in the save.");
            if (!player.Layout.SameAs(saved.Layout))
                throw new InvalidOperationException($"Player {player.PlayerNumber}'s plate level cannot be edited.");
            if (!player.Unlocked.SetEquals(saved.Unlocked))
                throw new InvalidOperationException($"Player {player.PlayerNumber}'s unlocked gadgets cannot be edited.");
            if (player.Plates.Count != saved.Plates.Count)
                throw new InvalidOperationException($"Player {player.PlayerNumber}'s number of presets cannot be edited.");
        }
    }
}
