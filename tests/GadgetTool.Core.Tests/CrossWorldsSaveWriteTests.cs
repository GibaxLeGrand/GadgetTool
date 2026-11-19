using System.Security.Cryptography;
using GadgetTool.Core.Saves;
using GadgetTool.Core.Saves.CrossWorlds;
using GadgetTool.Core.Saves.Gvas;
using GadgetTool.Core.Setups;
using static GadgetTool.Core.Tests.GvasWriter;
using static GadgetTool.Core.Tests.TestData;

namespace GadgetTool.Core.Tests;

/// <summary>Writing presets into a synthetic encrypted save shaped like the game's (made-up gadgets and key).</summary>
public sealed class CrossWorldsSaveWriteTests : IDisposable
{
    private static readonly byte[] Key = [.. Enumerable.Range(0, 32).Select(i => (byte)(255 - i))];
    private static readonly CrossWorldsSaveFormat Writer = new(Key, id => Catalog.TryGet(id, out var g) ? g.SlotCost : null);
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private const string No = CrossWorldsSaveFormat.NoGadget;

    private static byte[] Entry(string id, int cell) =>
        Struct(Enum("GadgetID", "EGadgetId", CrossWorldsSaveFormat.GadgetEnumPrefix + id), Int("SlotIndex", cell));

    private static byte[] Rows(params byte[][] entries) => Struct(ArrayProp("GadgetDataList", StructType("UserGadgetData"), entries));

    private static byte[] Super(params byte[][] entries) => Struct(ArrayProp("EquippedSpGadgets", StructType("UserGadgetData"), entries));

    /// <summary>
    /// Two players, three presets each. P1: A = [OneA@0, TwoA@3] + Super OneB, B = empty (six
    /// NonGadget cells), C = [ThreeA@0]. P2: A = [OneC@1], B and C empty lists. Things around the
    /// presets (_UserCommonData before, _UserAfter after) must survive byte for byte.
    /// </summary>
    private static byte[] Save(int rom = 20000)
    {
        var emptyCells = Enumerable.Range(0, 6).Select(c => Entry(No, c)).ToArray();
        var plain = File(CrossWorldsSaveFormat.SaveGameClass,
            StructProp("_Header", "SaveDataHeader", Int("_Version", 1), Int("_RomVersion", rom), Int("_SaveCount", 9)),
            StructProp("_UserCommonData", "UserCommonData", Str("Nickname", "kept"), Bool("Flag", true)),
            StructProp("_UserMachineAssemblyData", "UserMachineAssemblyData",
                Int("GadgetFragmentNum", 10),
                Property("GadgetList", MapOf(EnumType("EGadgetId"), StructType("UserGadgetItemData")),
                    Map([.. AllButLocked.Select(id => (FString(CrossWorldsSaveFormat.GadgetEnumPrefix + id), Struct(Bool("bIsNew", false), Enum("LockType", "ELockType", "ELockType::None"))))])),
                Int("GadgetSlotNum", 7),
                ArrayProp("GadgetPresetDataLists", StructType("UserGadgetPresetDataList"),
                    Struct(ArrayProp("list", StructType("UserGadgetPresetData"),
                        Rows(Entry(OneA, 0), Entry(TwoA, 3)), Rows(emptyCells), Rows(Entry(ThreeA, 0)))),
                    Struct(ArrayProp("list", StructType("UserGadgetPresetData"),
                        Rows(Entry(OneC, 1)), Rows(), Rows()))),
                ArrayProp("SpGadgetPresetDataLists", StructType("UserSpGadgetPresetDataList"),
                    Struct(ArrayProp("Presets", StructType("UserSpGadgetPresetData"),
                        Super(Entry(OneB, 6)), Super(Entry(No, 6)), Super(), Super())),
                    Struct(ArrayProp("Presets", StructType("UserSpGadgetPresetData"),
                        Super(), Super(), Super(), Super())))),
            StructProp("_UserAfter", "UserAfter", Int("Races", 120), Str("Note", "kept too")));
        using var aes = Aes.Create();
        aes.Key = Key;
        return aes.EncryptEcb(plain, PaddingMode.PKCS7);
    }

    private static byte[] Decrypt(byte[] save)
    {
        using var aes = Aes.Create();
        aes.Key = Key;
        return aes.DecryptEcb(save, PaddingMode.PKCS7);
    }

    /// <summary>The bytes of a top-level property, found by name.</summary>
    private static byte[] TopLevel(byte[] save, string name)
    {
        var plain = Decrypt(save);
        var property = GvasReader.Read(plain).Root.Find(name)!;
        return plain[property.Offset..property.End];
    }

    /// <summary>(GadgetID, SlotIndex) of a preset's entries in a list, as written.</summary>
    private static List<(string Id, int Cell)> Entries(byte[] save, string lists, string presets, string entries, int player, int preset)
    {
        var assembly = Writer.ReadDocument(save).Root.Get<GvasStruct>("_UserMachineAssemblyData")!;
        var p = (GvasStruct)assembly.Get<GvasArray>(lists)!.Items[player - 1];
        var s = (GvasStruct)p.Get<GvasArray>(presets)!.Items[preset];
        return [.. s.Get<GvasArray>(entries)!.Items.Cast<GvasStruct>()
            .Select(e => (((string)e.Find("GadgetID")!.Value)["EGadgetId::".Length..], (int)e.Find("SlotIndex")!.Value))];
    }

    [Fact]
    public void Writing_unchanged_setups_gives_back_the_same_file()
    {
        var save = Save();
        Assert.Equal(save, Writer.Write(save, Writer.Read(save)));
    }

    [Fact]
    public void A_changed_preset_is_placed_in_cells_and_everything_else_stays()
    {
        var save = Save();
        var setups = Writer.Read(save).WithPlate(new PlateRef(2, 1), new GadgetPlate([TwoB, OneD, ThreeA], OneA));

        var written = Writer.Write(save, setups);
        var back = Writer.Read(written);

        // ThreeA fills a row on its own; TwoB and OneD share the other, in the plate's order.
        Assert.Equal([(ThreeA, 0), (TwoB, 3), (OneD, 5)], Entries(written, "GadgetPresetDataLists", "list", "GadgetDataList", 2, 1));
        Assert.Equal([(OneA, 6)], Entries(written, "SpGadgetPresetDataLists", "Presets", "EquippedSpGadgets", 2, 1));
        Assert.True(back.Plate(new PlateRef(2, 1)).SameGadgetsAs(new GadgetPlate([ThreeA, TwoB, OneD], OneA)));

        // Every other preset reads as before, and the data around the presets is untouched.
        foreach (var at in new[] { new PlateRef(1, 0), new PlateRef(1, 1), new PlateRef(1, 2), new PlateRef(2, 0), new PlateRef(2, 2) })
            Assert.True(back.Plate(at).SameGadgetsAs(Writer.Read(save).Plate(at)), at.ToString());
        Assert.Equal(TopLevel(save, "_UserCommonData"), TopLevel(written, "_UserCommonData"));
        Assert.Equal(TopLevel(save, "_UserAfter"), TopLevel(written, "_UserAfter"));
        Assert.Equal(TopLevel(save, "_Header"), TopLevel(written, "_Header"));
    }

    [Fact]
    public void A_new_super_slot_gadget_leaves_the_rows_as_the_game_placed_them()
    {
        var save = Save();
        var p1 = Writer.Read(save).Player(1);
        var setups = Writer.Read(save).WithPlate(new PlateRef(1, 0), p1.Plates[0].WithSuperGadget(OneC));

        var written = Writer.Write(save, setups);

        // OneA@0 and TwoA@3 would be rearranged (TwoA first) if the rows were rebuilt.
        Assert.Equal([(OneA, 0), (TwoA, 3)], Entries(written, "GadgetPresetDataLists", "list", "GadgetDataList", 1, 0));
        Assert.Equal([(OneC, 6)], Entries(written, "SpGadgetPresetDataLists", "Presets", "EquippedSpGadgets", 1, 0));
    }

    [Fact]
    public void An_emptied_preset_is_written_as_empty_cells_and_no_super_slot()
    {
        var save = Save();
        var written = Writer.Write(save, Writer.Read(save).WithPlate(new PlateRef(1, 0), GadgetPlate.Empty));

        Assert.Equal([.. Enumerable.Range(0, 6).Select(c => (No, c))], Entries(written, "GadgetPresetDataLists", "list", "GadgetDataList", 1, 0));
        Assert.Empty(Entries(written, "SpGadgetPresetDataLists", "Presets", "EquippedSpGadgets", 1, 0));
        Assert.True(Writer.Read(written).Plate(new PlateRef(1, 0)).SameGadgetsAs(GadgetPlate.Empty));
    }

    [Fact]
    public void Refuses_what_it_cannot_place_or_has_not_checked()
    {
        var save = Save();
        var setups = Writer.Read(save);

        // Two 3-slot... only one exists: a 3-slot and two 2-slot gadgets do not fit two rows of 3.
        Assert.Throws<SaveFormatException>(() => Writer.Write(save, setups.WithPlate(new PlateRef(1, 1), new GadgetPlate([ThreeA, TwoA, TwoB]))));
        Assert.Throws<SaveFormatException>(() => Writer.Write(save, setups.WithPlate(new PlateRef(1, 1), new GadgetPlate(["t.no_cost"]))));

        var newer = Save(rom: 20100);
        var e = Assert.Throws<SaveFormatException>(() => Writer.Write(newer, Writer.Read(newer).WithPlate(new PlateRef(1, 1), new GadgetPlate([OneA]))));
        Assert.Contains("has not been checked with this game version", e.Message);

        var readOnly = new CrossWorldsSaveFormat(Key);
        Assert.Throws<SaveFormatException>(() => readOnly.Write(save, setups.WithPlate(new PlateRef(1, 1), new GadgetPlate([OneA]))));
    }

    [Fact]
    public void A_session_saves_the_game_save_with_a_backup()
    {
        var path = _temp.File("app.sav");
        System.IO.File.WriteAllBytes(path, Save());
        var session = SaveSession.Open(path, new AnySaveFormat(Writer, new DevJsonSaveFormat()), Catalog);

        session.Update(session.Current.WithPlate(new PlateRef(2, 2), new GadgetPlate([OneB, TwoC])));
        var backup = session.Save(new SafeSaveWriter(new NotRunning(), TimeProvider.System), _temp.File("backups"));

        Assert.Equal(Save(), System.IO.File.ReadAllBytes(backup));
        var reopened = SaveSession.Open(path, Writer, Catalog);
        Assert.True(reopened.Current.Plate(new PlateRef(2, 2)).SameGadgetsAs(new GadgetPlate([OneB, TwoC])));
    }

    private sealed class NotRunning : IGameProcessProbe
    {
        public bool IsGameRunning() => false;
    }
}
