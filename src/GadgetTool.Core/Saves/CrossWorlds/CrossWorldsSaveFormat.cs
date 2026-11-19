using GadgetTool.Core.Gadgets;
using GadgetTool.Core.Saves.Gvas;
using GadgetTool.Core.Setups;

namespace GadgetTool.Core.Saves.CrossWorlds;

/// <summary>
/// The game's own save (<c>app.sav</c>): an encrypted GVAS file (see <see cref="SaveCipher"/>)
/// whose gadget setups live in <c>_UserMachineAssemblyData</c>. Read only for now: writing
/// comes once reading has been checked against real saves.
/// </summary>
/// <remarks>
/// What the save holds (game 2.0.0, read on 2026-11-19):
/// <list type="bullet">
/// <item><c>GadgetPresetDataLists</c>: one entry per local player (4), each a <c>list</c> of
/// presets (10, A to J), each a <c>GadgetDataList</c> of { <c>GadgetID</c>, <c>SlotIndex</c> }.
/// <c>SlotIndex</c> is the gadget's first cell: 0 to 2 on the top row, 3 to 5 on the bottom one.
/// An empty cell may be listed as <c>NonGadget</c>.</item>
/// <item><c>SpGadgetPresetDataLists</c>: per player, <c>Presets</c> (20, the first 10 in use),
/// each with <c>EquippedSpGadgets</c>: the Super Slot's gadget, at <c>SlotIndex</c> 6.</item>
/// <item><c>GadgetList</c>: every gadget the save knows, shared by all players, with its
/// <c>LockType</c>; <c>ELockType::None</c> means unlocked.</item>
/// <item><c>GadgetSlotNum</c>: the plate's slots, Super Slot included (7 on Grand Master), shared
/// by all players.</item>
/// </list>
/// </remarks>
public sealed class CrossWorldsSaveFormat : ISaveFormat
{
    public const string SaveGameClass = "/Script/UnionSystem.AppSaveGame";

    /// <summary>The enum prefix the save puts before each gadget id; ids in GadgetTool go without it.</summary>
    public const string GadgetEnumPrefix = "EGadgetId::";

    /// <summary>The id the save uses for an empty cell.</summary>
    public const string NoGadget = "NonGadget";

    private const string Unlocked = "ELockType::None";
    private const int SuperSlotCells = 1;

    private readonly SaveCipher _cipher;
    private readonly Func<string, int?>? _slotCost;

    /// <param name="key">The game's 32-byte save key.</param>
    /// <param name="slotCost">
    /// Each gadget's slot cost (from the catalog), to place gadgets in cells when writing;
    /// null for a format that only reads.
    /// </param>
    public CrossWorldsSaveFormat(ReadOnlySpan<byte> key, Func<string, int?>? slotCost = null)
    {
        _cipher = new SaveCipher(key);
        _slotCost = slotCost;
    }

    /// <summary>
    /// The save key from a text that holds it: the first run of 64 hex digits, with or without
    /// "0x" (a key file may be "name=HEX" or the bare key).
    /// </summary>
    /// <exception cref="FormatException">No key in the text.</exception>
    public static byte[] KeyFromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var match = System.Text.RegularExpressions.Regex.Match(text, "(?<![0-9A-Fa-f])[0-9A-Fa-f]{64}(?![0-9A-Fa-f])");
        return match.Success
            ? Convert.FromHexString(match.Value)
            : throw new FormatException("No save key (64 hex digits) in the text.");
    }

    public string Name => "Sonic Racing CrossWorlds save";

    public bool CanRead(ReadOnlySpan<byte> content) => _cipher.LooksLikeSave(content);

    public SaveSetups Read(ReadOnlySpan<byte> content)
    {
        var document = ReadDocument(content);
        var root = document.Root;
        var rom = root.Get<GvasStruct>("_Header")?.Find("_RomVersion")?.Value as int?
            ?? throw Damaged("it has no _Header._RomVersion");
        var assembly = root.Get<GvasStruct>("_UserMachineAssemblyData") ?? throw Damaged("it has no _UserMachineAssemblyData");

        var layout = LayoutFor(Int(assembly, "GadgetSlotNum"));
        var unlocked = UnlockedGadgets(assembly);
        var presetLists = Items(assembly, "GadgetPresetDataLists");
        var superLists = assembly.Get<GvasArray>("SpGadgetPresetDataLists")?.Items ?? [];
        if (presetLists.Count is < PlayerSetups.MinPlayer or > PlayerSetups.MaxPlayer)
            throw Damaged($"it holds presets for {presetLists.Count} players");

        var players = new List<PlayerSetups>();
        for (var p = 0; p < presetLists.Count; p++)
        {
            var presets = Items(AsStruct(presetLists[p], $"player {p + 1}'s presets"), "list");
            var supers = p < superLists.Count && superLists[p] is GvasStruct s
                ? s.Get<GvasArray>("Presets")?.Items ?? []
                : [];

            var plates = new List<GadgetPlate>(presets.Count);
            for (var i = 0; i < presets.Count; i++)
            {
                var what = $"player {p + 1} preset {PlayerSetups.PresetLetter(i)}";
                var rows = Gadgets(AsStruct(presets[i], what), "GadgetDataList", what);
                var super = i < supers.Count && supers[i] is GvasStruct sp
                    ? Gadgets(sp, "EquippedSpGadgets", what + " Super Slot").Select(g => g.Id).FirstOrDefault()
                    : null;
                plates.Add(new GadgetPlate(rows.Select(g => g.Id), super));
            }

            players.Add(new PlayerSetups(p + 1, layout, unlocked, plates));
        }

        return new SaveSetups($"rom {rom}", players);
    }

    /// <summary>
    /// Returns the save with the changed presets rewritten and every other byte as it was.
    /// Only the two preset lists (<c>GadgetPresetDataLists</c>, <c>SpGadgetPresetDataLists</c>)
    /// are rebuilt, and inside them only the presets that changed; the size of
    /// <c>_UserMachineAssemblyData</c> is patched to match. Gadgets are placed in cells from
    /// their slot costs (<see cref="PlateLayout.Arrange"/>), the Super Slot at cell 6.
    /// </summary>
    /// <exception cref="SaveFormatException">
    /// A game version GadgetTool has not checked for writing, a plate that does not fit, a
    /// gadget of unknown cost, or a save whose preset lists GadgetTool cannot write back as
    /// they are (then it does not trust itself to write changed ones either).
    /// </exception>
    public byte[] Write(ReadOnlySpan<byte> original, SaveSetups setups)
    {
        ArgumentNullException.ThrowIfNull(setups);
        if (_slotCost is null)
            throw new SaveFormatException("Writing needs the gadgets' slot costs, to place them on the plate.");

        var plain = _cipher.Decrypt(original);
        var document = GvasReader.Read(plain);
        var current = Read(original);
        if (setups.SaveVersion != current.SaveVersion)
            throw new SaveFormatException($"The setups come from a '{setups.SaveVersion}' save; this one is '{current.SaveVersion}'.");
        var rom = int.Parse(current.SaveVersion["rom ".Length..], System.Globalization.CultureInfo.InvariantCulture);
        if (!WritableRomVersions.Contains(rom))
            throw new SaveFormatException(
                $"GadgetTool has not been checked with this game version (rom {rom}); it reads the save but will not write it.");
        if (setups.Players.Count != current.Players.Count
            || setups.Players.Any(p => p.Plates.Count != current.Player(p.PlayerNumber).Plates.Count))
            throw new SaveFormatException("The setups do not have the save's players and presets.");

        // The rows and the Super Slot live in two lists: each is rebuilt only where it changed,
        // so a new Super Slot gadget leaves the rows' cells as the game placed them.
        var plates = (from player in setups.Players
                      from index in Enumerable.Range(0, player.Plates.Count)
                      let at = new PlateRef(player.PlayerNumber, index)
                      select (At: at, New: player.Plates[index], Old: current.Plate(at))).ToList();
        var rowsChanged = plates.Where(p => !p.New.GadgetIds.SequenceEqual(p.Old.GadgetIds, GadgetIds.Comparer)).Select(p => p.At).ToHashSet();
        var superChanged = plates.Where(p => !GadgetIds.Same(p.New.SuperGadgetId, p.Old.SuperGadgetId)).Select(p => p.At).ToHashSet();
        if (rowsChanged.Count == 0 && superChanged.Count == 0)
            return original.ToArray();

        var assemblyProperty = document.Root.Find("_UserMachineAssemblyData") ?? throw Damaged("it has no _UserMachineAssemblyData");
        var assembly = (GvasStruct)assemblyProperty.Value;
        var rowsProperty = assembly.Find("GadgetPresetDataLists") ?? throw Damaged("it has no GadgetPresetDataLists");
        var superProperty = assembly.Find("SpGadgetPresetDataLists");

        // Trust the writer with a change only if it writes the unchanged lists back byte for byte.
        foreach (var property in new[] { rowsProperty, superProperty }.OfType<GvasProperty>())
        {
            byte[] again;
            try
            {
                again = GvasSerializer.Property(property);
            }
            catch (NotSupportedException e)
            {
                throw new SaveFormatException($"GadgetTool cannot write this save's {property.Name}: {e.Message}", e);
            }
            if (!again.AsSpan().SequenceEqual(plain.AsSpan(property.Offset, property.End - property.Offset)))
                throw new SaveFormatException($"GadgetTool cannot write this save's {property.Name} back as it is; it will not change it.");
        }

        var templates = new Templates(assembly);
        if (superProperty is null && superChanged.Count > 0)
            throw new SaveFormatException("This save has no Super Slot list.");
        var newRows = rowsChanged.Count == 0 ? null : Rebuild(rowsProperty, "list", "GadgetDataList", rowsChanged, (player, index) =>
            Cells(setups.Player(player).Plates[index], setups.Player(player).Layout, templates));
        var newSuper = superChanged.Count == 0 ? null : Rebuild(superProperty!, "Presets", "EquippedSpGadgets", superChanged, (player, index) =>
            setups.Player(player).Plates[index].SuperGadgetId is { } super ? [templates.Gadget(super, SuperCell)] : []);

        // Splice the new lists in, the later one first so offsets stay right, then fix the size
        // of the struct around them.
        var result = plain;
        var delta = 0;
        foreach (var (old, @new) in new[] { (rowsProperty, newRows), (superProperty, newSuper) }
                     .Where(r => r.Item1 is not null && r.Item2 is not null)
                     .OrderByDescending(r => r.Item1!.Offset))
        {
            var bytes = GvasSerializer.Property(@new!);
            result = [.. result.AsSpan(0, old!.Offset), .. bytes, .. result.AsSpan(old.End)];
            delta += bytes.Length - (old.End - old.Offset);
        }
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(assemblyProperty.SizeOffset), assemblyProperty.Size + delta);

        var encrypted = _cipher.Encrypt(result);
        CheckWritten(encrypted, setups, current, rowsChanged);
        return encrypted;
    }

    /// <summary>Game versions (<c>_Header._RomVersion</c>) whose save GadgetTool has checked for writing.</summary>
    public static IReadOnlySet<int> WritableRomVersions { get; } = new HashSet<int> { 20000 };

    /// <summary>The Super Slot's cell.</summary>
    public const int SuperCell = 6;

    /// <summary>
    /// Reads back what was written: presets whose rows changed must hold the new gadgets (by
    /// cell), the others the rows they held before; every Super Slot must be the one asked
    /// for. Otherwise nothing is returned.
    /// </summary>
    private void CheckWritten(byte[] written, SaveSetups setups, SaveSetups before, HashSet<PlateRef> rowsChanged)
    {
        SaveSetups reread;
        try
        {
            reread = Read(written);
        }
        catch (SaveFormatException e)
        {
            throw new SaveFormatException("The rewritten save does not read back; nothing was written.", e);
        }
        foreach (var player in setups.Players)
        {
            var back = reread.Player(player.PlayerNumber);
            var expected = Enumerable.Range(0, player.Plates.Count).Select(i =>
                rowsChanged.Contains(new PlateRef(player.PlayerNumber, i))
                    ? Ordered(player.Plates[i], player.Layout)
                    : before.Player(player.PlayerNumber).Plates[i].WithSuperGadget(player.Plates[i].SuperGadgetId));
            if (!back.Unlocked.SetEquals(player.Unlocked) || !back.Layout.SameAs(player.Layout)
                || !back.Plates.Zip(expected).All(pair => pair.First.SameGadgetsAs(pair.Second)))
                throw new SaveFormatException($"The rewritten save does not hold player {player.PlayerNumber}'s presets as asked; nothing was written.");
        }
    }

    /// <summary>The plate's gadgets in the order a save gives them back: by cell.</summary>
    private GadgetPlate Ordered(GadgetPlate plate, PlateLayout layout)
    {
        var cells = PlaceInCells(plate, layout);
        return new GadgetPlate(cells.OrderBy(c => c.Cell).Select(c => c.Id), plate.SuperGadgetId);
    }

    /// <summary>
    /// Each gadget's first cell: rows from <see cref="PlateLayout.Arrange"/>, then left to right
    /// in the plate's order within each row. Row r starts at cell 3r.
    /// </summary>
    internal List<(string Id, int Cell)> PlaceInCells(GadgetPlate plate, PlateLayout layout)
    {
        var costs = plate.GadgetIds
            .Select(id => _slotCost!(id) ?? throw new SaveFormatException($"Gadget '{id}' has no known slot cost; GadgetTool cannot place it."))
            .ToList();
        var rows = layout.Arrange(costs) ?? throw new SaveFormatException($"[{string.Join(", ", plate.GadgetIds)}] does not fit on the plate.");
        var next = Enumerable.Range(0, layout.RowSizes.Count).Select(r => r * PlateLayout.MaxRowSize).ToArray();
        var cells = new List<(string Id, int Cell)>();
        for (var k = 0; k < costs.Count; k++)
        {
            cells.Add((plate.GadgetIds[k], next[rows[k]]));
            next[rows[k]] += costs[k];
        }
        return cells;
    }

    /// <summary>A preset's row entries: its gadgets by cell, or every cell empty when it has none (as the game writes it).</summary>
    private List<GvasStruct> Cells(GadgetPlate plate, PlateLayout layout, Templates templates)
    {
        if (plate.GadgetIds.Count == 0)
        {
            return [.. layout.RowSizes.SelectMany((size, row) => Enumerable.Range(row * PlateLayout.MaxRowSize, size))
                .Select(cell => templates.Gadget(NoGadget, cell))];
        }
        return [.. PlaceInCells(plate, layout).OrderBy(c => c.Cell).Select(c => templates.Gadget(c.Id, c.Cell))];
    }

    /// <summary>
    /// A copy of a per-player list property (<c>GadgetPresetDataLists</c> or
    /// <c>SpGadgetPresetDataLists</c>) where the changed presets get new entries; every other
    /// preset keeps its parsed value, so it is written back as it was.
    /// </summary>
    private static GvasProperty Rebuild(GvasProperty listsProperty, string presetsName, string entriesName,
        HashSet<PlateRef> changed, Func<int, int, List<GvasStruct>> entries)
    {
        var players = (GvasArray)listsProperty.Value;
        var newPlayers = players.Items.Select((playerValue, p) =>
        {
            var player = (GvasStruct)playerValue;
            var presetsProperty = player.Find(presetsName) ?? throw Damaged($"player {p + 1} has no {presetsName}");
            var presets = (GvasArray)presetsProperty.Value;
            var newPresets = presets.Items.Select((presetValue, i) =>
            {
                if (!changed.Contains(new PlateRef(p + 1, i)))
                    return presetValue;
                var preset = (GvasStruct)presetValue;
                var entriesProperty = preset.Find(entriesName) ?? throw Damaged($"player {p + 1} preset {i + 1} has no {entriesName}");
                var list = (GvasArray)entriesProperty.Value;
                return (object)Replace(preset, entriesProperty with { Value = list with { Items = [.. entries(p + 1, i)] } });
            }).ToList();
            return (object)Replace(player, presetsProperty with { Value = presets with { Items = newPresets } });
        }).ToList();
        return listsProperty with { Value = players with { Items = newPlayers } };
    }

    private static GvasStruct Replace(GvasStruct value, GvasProperty property) =>
        new([.. value.Properties.Select(p => ReferenceEquals(p, value.Find(property.Name)) ? property : p)]);

    /// <summary>
    /// The shape of one entry ({ GadgetID, SlotIndex }), taken from an entry the save already
    /// has, so new entries are written exactly like the game's.
    /// </summary>
    private sealed class Templates
    {
        private readonly GvasProperty _id;
        private readonly GvasProperty _cell;

        public Templates(GvasStruct assembly)
        {
            var entry = new[] { ("GadgetPresetDataLists", "list", "GadgetDataList"), ("SpGadgetPresetDataLists", "Presets", "EquippedSpGadgets") }
                .SelectMany(path => assembly.Get<GvasArray>(path.Item1)?.Items.OfType<GvasStruct>()
                    .SelectMany(player => player.Get<GvasArray>(path.Item2)?.Items.OfType<GvasStruct>() ?? [])
                    .SelectMany(preset => preset.Get<GvasArray>(path.Item3)?.Items.OfType<GvasStruct>() ?? []) ?? [])
                .FirstOrDefault()
                ?? throw new SaveFormatException("The save has no gadget entry to copy the layout of; GadgetTool will not write it.");
            if (entry.Properties.Count != 2)
                throw new SaveFormatException($"A gadget entry has {entry.Properties.Count} fields, not 2; GadgetTool will not write this save.");
            _id = entry.Find("GadgetID") ?? throw new SaveFormatException("A gadget entry has no GadgetID.");
            _cell = entry.Find("SlotIndex") ?? throw new SaveFormatException("A gadget entry has no SlotIndex.");
        }

        public GvasStruct Gadget(string id, int cell) =>
            new([_id with { Value = GadgetEnumPrefix + id }, _cell with { Value = cell }]);
    }

    /// <summary>The decrypted save as a property tree, for tools and checks.</summary>
    /// <exception cref="SaveFormatException">Not a CrossWorlds save, another key, or damaged.</exception>
    public GvasDocument ReadDocument(ReadOnlySpan<byte> content)
    {
        var document = GvasReader.Read(_cipher.Decrypt(content));
        if (document.Header.SaveGameClass != SaveGameClass)
            throw new SaveFormatException($"This is a '{document.Header.SaveGameClass}' save, not the game's progress save ({SaveGameClass}).");
        return document;
    }

    /// <summary>
    /// The plate for a slot count that includes the Super Slot when there is one (7 = two rows of
    /// 3 plus the Super Slot). Lower plates are assumed to fill the top row first, as the cells
    /// are numbered; not checked on a save with a lower plate yet.
    /// </summary>
    internal static PlateLayout LayoutFor(int slotNum)
    {
        const int rowCells = PlateLayout.MaxRowSize * 2;
        if (slotNum is < 1 or > rowCells + SuperSlotCells)
            throw Damaged($"its plate has {slotNum} slots");
        if (slotNum > rowCells)
            return PlateLayout.GrandMaster;
        return slotNum <= PlateLayout.MaxRowSize
            ? new PlateLayout([slotNum], hasSuperSlot: false)
            : new PlateLayout([PlateLayout.MaxRowSize, slotNum - PlateLayout.MaxRowSize], hasSuperSlot: false);
    }

    private static HashSet<string> UnlockedGadgets(GvasStruct assembly)
    {
        var list = assembly.Get<GvasMap>("GadgetList") ?? throw Damaged("it has no GadgetList");
        var unlocked = new HashSet<string>(GadgetIds.Comparer);
        foreach (var (key, value) in list.Entries)
        {
            var id = GadgetId(key as string ?? throw Damaged("GadgetList has a key that is not a gadget"));
            var lockType = (value as GvasStruct)?.Find("LockType")?.Value as string ?? Unlocked;
            if (id is not null && lockType == Unlocked)
                unlocked.Add(id);
        }
        return unlocked;
    }

    /// <summary>The gadgets of one list, by cell, without empty cells.</summary>
    private static List<(string Id, int Cell)> Gadgets(GvasStruct preset, string listName, string what)
    {
        var gadgets = new List<(string Id, int Cell)>();
        foreach (var item in preset.Get<GvasArray>(listName)?.Items ?? throw Damaged($"{what} has no {listName}"))
        {
            var entry = AsStruct(item, what);
            var id = GadgetId(entry.Find("GadgetID")?.Value as string ?? throw Damaged($"{what} has a gadget without GadgetID"));
            var cell = entry.Find("SlotIndex")?.Value as int? ?? 0;
            if (id is not null)
                gadgets.Add((id, cell));
        }
        return [.. gadgets.OrderBy(g => g.Cell)];
    }

    /// <summary>"EGadgetId::Some_Gadget" → "Some_Gadget"; null for an empty cell.</summary>
    internal static string? GadgetId(string enumValue)
    {
        var id = enumValue.StartsWith(GadgetEnumPrefix, StringComparison.Ordinal) ? enumValue[GadgetEnumPrefix.Length..] : enumValue;
        return id.Length == 0 || id == NoGadget ? null : id;
    }

    private static IReadOnlyList<object> Items(GvasStruct parent, string name) =>
        parent.Get<GvasArray>(name)?.Items ?? throw Damaged($"it has no {name} list");

    private static int Int(GvasStruct parent, string name) =>
        parent.Find(name)?.Value as int? ?? throw Damaged($"it has no {name}");

    private static GvasStruct AsStruct(object value, string what) =>
        value as GvasStruct ?? throw Damaged($"{what} is not a struct");

    private static SaveFormatException Damaged(string why) => new($"The save is damaged or not as expected: {why}.");
}
