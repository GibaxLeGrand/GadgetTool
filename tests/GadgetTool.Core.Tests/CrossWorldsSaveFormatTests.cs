using System.Security.Cryptography;
using GadgetTool.Core.Saves;
using GadgetTool.Core.Saves.CrossWorlds;
using GadgetTool.Core.Setups;
using static GadgetTool.Core.Tests.GvasWriter;
using static GadgetTool.Core.Tests.TestData;

namespace GadgetTool.Core.Tests;

/// <summary>
/// Saves built here have the real save's shape (names of the structs and lists the format
/// reads) but made-up gadgets and a made-up key: no real save or key is needed.
/// </summary>
public class CrossWorldsSaveFormatTests
{
    private static readonly byte[] Key = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];
    private static readonly byte[] OtherKey = [.. Enumerable.Range(100, 32).Select(i => (byte)i)];
    private static readonly CrossWorldsSaveFormat Format = new(Key);

    private static byte[] Gadget(string id, int cell) =>
        Struct(Enum("GadgetID", "EGadgetId", CrossWorldsSaveFormat.GadgetEnumPrefix + id), Int("SlotIndex", cell));

    private static byte[] Preset(params byte[][] gadgets) =>
        Struct(ArrayProp("GadgetDataList", StructType("UserGadgetData"), gadgets));

    private static byte[] SuperPreset(params byte[][] gadgets) =>
        Struct(ArrayProp("EquippedSpGadgets", StructType("UserGadgetData"), gadgets));

    private static (byte[] Key, byte[] Value) Owned(string id, string lockType = "ELockType::None") =>
        (FString(CrossWorldsSaveFormat.GadgetEnumPrefix + id), Struct(Bool("bIsNew", false), Enum("LockType", "ELockType", lockType)));

    private static readonly (byte[] Key, byte[] Value)[] DefaultOwned =
        [Owned(CrossWorldsSaveFormat.NoGadget), Owned(OneA), Owned(OneB), Owned(OneC), Owned(TwoA), Owned(ThreeA), Owned(Locked, "ELockType::Price")];

    private static byte[] Plain(
        byte[][][] presets,
        byte[][][]? supers = null,
        int slotNum = 7,
        (byte[] Key, byte[] Value)[]? owned = null,
        string saveClass = CrossWorldsSaveFormat.SaveGameClass) =>
        File(saveClass,
            StructProp("_Header", "SaveDataHeader", Int("_Version", 1), Int("_RomVersion", 20000), Int("_SaveCount", 3)),
            StructProp("_UserCommonData", "UserCommonData", Str("Nickname", "kept")),
            StructProp("_UserMachineAssemblyData", "UserMachineAssemblyData",
                Int("GadgetFragmentNum", 10),
                Property("GadgetList", MapOf(EnumType("EGadgetId"), StructType("UserGadgetItemData")), Map(owned ?? DefaultOwned)),
                Int("GadgetSlotNum", slotNum),
                ArrayProp("GadgetPresetDataLists", StructType("UserGadgetPresetDataList"),
                    [.. presets.Select(p => Struct(ArrayProp("list", StructType("UserGadgetPresetData"), p)))]),
                ArrayProp("SpGadgetPresetDataLists", StructType("UserSpGadgetPresetDataList"),
                    [.. (supers ?? []).Select(p => Struct(ArrayProp("Presets", StructType("UserSpGadgetPresetData"), p)))])));

    private static byte[] Encrypt(byte[] plain, byte[]? key = null)
    {
        using var aes = Aes.Create();
        aes.Key = key ?? Key;
        return aes.EncryptEcb(plain, PaddingMode.PKCS7);
    }

    /// <summary>
    /// P1: preset A = gadgets listed out of cell order plus a Super Slot gadget; preset B = six
    /// empty cells. P2: preset A = one gadget, no Super Slot list at all.
    /// </summary>
    private static byte[] TwoPlayerSave() => Encrypt(Plain(
        presets:
        [
            [Preset(Gadget(TwoA, 3), Gadget(OneA, 0), Gadget(OneB, 1)), Preset([.. Enumerable.Range(0, 6).Select(c => Gadget(CrossWorldsSaveFormat.NoGadget, c))])],
            [Preset(Gadget(ThreeA, 0)), Preset()],
        ],
        supers:
        [
            [SuperPreset(Gadget(OneC, 6)), SuperPreset(Gadget(CrossWorldsSaveFormat.NoGadget, 6)), SuperPreset(Gadget(OneA, 6))],
        ]));

    [Fact]
    public void Recognises_its_saves_only()
    {
        var save = TwoPlayerSave();

        Assert.True(Format.CanRead(save));
        Assert.False(new CrossWorldsSaveFormat(OtherKey).CanRead(save));
        Assert.False(Format.CanRead(save[..^1]));
        Assert.False(Format.CanRead(System.Text.Encoding.UTF8.GetBytes(DevSaveJson)));
        Assert.False(new DevJsonSaveFormat().CanRead(save));
    }

    [Fact]
    public void Reads_each_players_presets_by_cell_with_the_super_slot()
    {
        var save = Format.Read(TwoPlayerSave());

        Assert.Equal("rom 20000", save.SaveVersion);
        Assert.Equal([1, 2], save.Players.Select(p => p.PlayerNumber));

        var p1 = save.Player(1);
        Assert.Equal(2, p1.Plates.Count);
        Assert.True(p1.Plates[0].SameGadgetsAs(new GadgetPlate([OneA, OneB, TwoA], superGadgetId: OneC)));
        Assert.True(p1.Plates[1].SameGadgetsAs(GadgetPlate.Empty)); // NonGadget cells are empty cells

        var p2 = save.Player(2);
        Assert.True(p2.Plates[0].SameGadgetsAs(new GadgetPlate([ThreeA])));
        Assert.True(p2.Plates[1].SameGadgetsAs(GadgetPlate.Empty));
    }

    [Fact]
    public void Unlocked_gadgets_are_the_ones_without_a_lock_and_shared_by_all_players()
    {
        var save = Format.Read(TwoPlayerSave());

        foreach (var player in save.Players)
        {
            Assert.Equal(new[] { OneA, OneB, OneC, TwoA, ThreeA }.Order(), player.Unlocked.Order());
            Assert.DoesNotContain(Locked, player.Unlocked);
        }
    }

    [Theory]
    [InlineData(7, new[] { 3, 3 }, true)]
    [InlineData(6, new[] { 3, 3 }, false)]
    [InlineData(4, new[] { 3, 1 }, false)]
    [InlineData(2, new[] { 2 }, false)]
    public void The_plate_comes_from_the_slot_count(int slotNum, int[] rows, bool superSlot)
    {
        var layout = Format.Read(Encrypt(Plain([[Preset()]], slotNum: slotNum))).Player(1).Layout;

        Assert.Equal(rows, layout.RowSizes);
        Assert.Equal(superSlot, layout.HasSuperSlot);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void A_slot_count_out_of_range_is_refused(int slotNum) =>
        Assert.Throws<SaveFormatException>(() => Format.Read(Encrypt(Plain([[Preset()]], slotNum: slotNum))));

    [Fact]
    public void Another_key_another_save_class_or_missing_data_is_refused()
    {
        Assert.Throws<SaveFormatException>(() => Format.Read(Encrypt(Plain([[Preset()]]), OtherKey)));
        Assert.Throws<SaveFormatException>(() => Format.Read(TwoPlayerSave()[..^1]));
        Assert.Throws<SaveFormatException>(() => Format.Read(Encrypt(Plain([[Preset()]], saveClass: "/Script/UnionSystem.NetSaveGame"))));
        Assert.Throws<SaveFormatException>(() => Format.Read(Encrypt(Plain([]))));
        Assert.Throws<SaveFormatException>(() => Format.Read(Encrypt(File(CrossWorldsSaveFormat.SaveGameClass,
            StructProp("_Header", "SaveDataHeader", Int("_RomVersion", 20000))))));
    }

    [Fact]
    public void Writing_needs_the_slot_costs()
    {
        var bytes = TwoPlayerSave();
        var e = Assert.Throws<SaveFormatException>(() => Format.Write(bytes, Format.Read(bytes)));
        Assert.Contains("slot costs", e.Message);
    }

    [Fact]
    public void The_key_must_be_32_bytes() =>
        Assert.Throws<ArgumentException>(() => new CrossWorldsSaveFormat(new byte[16]));

    [Theory]
    [InlineData("EGadgetId::Ring_Test", "Ring_Test")]
    [InlineData("Ring_Test", "Ring_Test")]
    [InlineData("EGadgetId::NonGadget", null)]
    [InlineData("EGadgetId::", null)]
    public void Gadget_ids_go_without_the_enum_prefix(string enumValue, string? id) =>
        Assert.Equal(id, CrossWorldsSaveFormat.GadgetId(enumValue));

    [Fact]
    public void A_session_opens_it_and_checks_its_plates()
    {
        using var temp = new TempFolder();
        var path = temp.File("app.sav");
        System.IO.File.WriteAllBytes(path, TwoPlayerSave());

        var session = SaveSession.Open(path, Format, Catalog);

        Assert.True(session.Current.Plate(new PlateRef(1, 0)).SameGadgetsAs(new GadgetPlate([OneA, OneB, TwoA], OneC)));
        Assert.Empty(session.Problems());
        Assert.False(session.HasChanges);
    }
}
