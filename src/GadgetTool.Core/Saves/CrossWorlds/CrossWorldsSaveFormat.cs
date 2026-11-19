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

    /// <param name="key">The game's 32-byte save key.</param>
    public CrossWorldsSaveFormat(ReadOnlySpan<byte> key) => _cipher = new SaveCipher(key);

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

    public byte[] Write(ReadOnlySpan<byte> original, SaveSetups setups) =>
        throw new SaveFormatException("GadgetTool cannot write CrossWorlds saves yet: it only reads them.");

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
