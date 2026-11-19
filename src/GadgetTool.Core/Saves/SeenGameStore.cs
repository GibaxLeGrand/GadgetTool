using System.Text.Json;
using System.Text.Json.Nodes;
using GadgetTool.Core.Gadgets;
using GadgetTool.Core.Setups;
using GadgetTool.Core.Sharing;

namespace GadgetTool.Core.Saves;

/// <summary>What a game update changed for the presets of a save.</summary>
/// <param name="From">The game version GadgetTool last saw this save with.</param>
/// <param name="To">The game version now.</param>
/// <param name="Changes">Gadgets used in the save's presets whose slot cost changed, or that are gone.</param>
/// <param name="Presets">The presets that hold one of those gadgets.</param>
public sealed record GameUpdate(GameVersion From, GameVersion To, IReadOnlyList<GadgetChange> Changes, IReadOnlyList<PlateRef> Presets);

/// <summary>
/// Remembers, for each save, the game (version and gadgets) GadgetTool last saw it with, so
/// that opening it after a rebalance can say which presets it touched. Only gadgets the save's
/// presets use count: a preset whose gadgets did not change stays valid and is not flagged.
/// Kept in a file of its own, never in the save.
/// </summary>
public sealed class SeenGameStore
{
    private const string FormatName = "gadgettool.seen";

    private readonly string _file;
    private readonly Dictionary<string, (GameVersion Version, Dictionary<string, GadgetSnapshot> Gadgets)> _saves =
        new(StringComparer.OrdinalIgnoreCase);

    /// <param name="file">The file. Missing or unreadable means nothing seen yet.</param>
    public SeenGameStore(string file)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        _file = file;
        Load();
    }

    /// <summary>
    /// What changed since this save was last seen, for the gadgets its presets use; null when
    /// it was never seen or nothing it uses changed.
    /// </summary>
    public GameUpdate? Check(string savePath, SaveSetups setups, GadgetCatalog now)
    {
        ArgumentNullException.ThrowIfNull(setups);
        ArgumentNullException.ThrowIfNull(now);
        if (!_saves.TryGetValue(Key(savePath), out var seen))
            return null;

        var used = setups.Players
            .SelectMany(p => p.Plates.SelectMany(IdsOf))
            .Distinct(GadgetIds.Comparer)
            .Where(seen.Gadgets.ContainsKey)
            .Select(id => (id, (string?)seen.Gadgets[id].Name, seen.Gadgets[id].SlotCost));
        var changes = GameChanges.Compare(used, now);
        if (changes.Count == 0)
            return null;

        var changed = changes.Select(c => c.Id).ToHashSet(GadgetIds.Comparer);
        var presets = (from p in setups.Players
                       from i in Enumerable.Range(0, p.Plates.Count)
                       where IdsOf(p.Plates[i]).Any(changed.Contains)
                       select new PlateRef(p.PlayerNumber, i)).ToList();
        return new GameUpdate(seen.Version, now.GameVersion, changes, presets);
    }

    /// <summary>Records the game as it is now for this save (after the user saw what changed).</summary>
    public void Remember(string savePath, GadgetCatalog now)
    {
        ArgumentNullException.ThrowIfNull(now);
        _saves[Key(savePath)] = (now.GameVersion,
            now.Gadgets.ToDictionary(g => g.Id, g => new GadgetSnapshot(g.Name, g.SlotCost), GadgetIds.Comparer));
        Persist();
    }

    public bool HasSeen(string savePath) => _saves.ContainsKey(Key(savePath));

    private static IEnumerable<string> IdsOf(GadgetPlate plate) =>
        plate.SuperGadgetId is { } super ? plate.GadgetIds.Append(super) : plate.GadgetIds;

    private static string Key(string savePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(savePath);
        return Path.GetFullPath(savePath);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_file) || JsonNode.Parse(File.ReadAllText(_file)) is not JsonObject root
                || root["format"]?.GetValue<string>() != FormatName || root["saves"] is not JsonObject saves)
                return;

            foreach (var (path, value) in saves)
            {
                if (value is not JsonObject save || save["gadgets"] is not JsonObject list)
                    continue;
                var gadgets = new Dictionary<string, GadgetSnapshot>(GadgetIds.Comparer);
                foreach (var (id, g) in list)
                {
                    if (g is JsonObject o && o["slotCost"] is JsonValue c && c.TryGetValue(out int cost) && cost >= 1)
                        gadgets[id] = new GadgetSnapshot(o["name"] is JsonValue n && n.TryGetValue(out string? name) ? name : id, cost);
                }
                var version = save["gameVersion"] is JsonValue v && v.TryGetValue(out string? text) ? GameVersion.Parse(text) : GameVersion.Unknown;
                _saves[path] = (version, gadgets);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            _saves.Clear();
        }
    }

    private void Persist()
    {
        var saves = new JsonObject();
        foreach (var (path, seen) in _saves.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase))
        {
            var gadgets = new JsonObject();
            foreach (var (id, g) in seen.Gadgets.OrderBy(g => g.Key, StringComparer.Ordinal))
                gadgets[id] = new JsonObject { ["name"] = g.Name, ["slotCost"] = g.SlotCost };
            saves[path] = new JsonObject { ["gameVersion"] = seen.Version.ToString(), ["gadgets"] = gadgets };
        }

        var root = new JsonObject { ["format"] = FormatName, ["version"] = 1, ["saves"] = saves };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_file))!);
        var temp = _file + ".tmp";
        File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _file, overwrite: true);
    }
}
