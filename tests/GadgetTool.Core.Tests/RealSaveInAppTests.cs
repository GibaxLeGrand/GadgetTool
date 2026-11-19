using System.Security.Cryptography;
using System.Text.Json.Nodes;
using GadgetTool.Core.Bridge;
using GadgetTool.Core.Gadgets;
using GadgetTool.Core.Saves;
using GadgetTool.Core.Saves.CrossWorlds;
using static GadgetTool.Core.Tests.GvasWriter;
using static GadgetTool.Core.Tests.TestData;

namespace GadgetTool.Core.Tests;

/// <summary>What the app needs to open the game's save next to the made-up sample.</summary>
public sealed class RealSaveInAppTests : IDisposable
{
    private static readonly byte[] Key = [.. Enumerable.Range(0, 32).Select(i => (byte)(i * 3))];
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>A small encrypted CrossWorlds save: one player, one preset, made-up gadgets.</summary>
    private static byte[] GameSave()
    {
        static byte[] Gadget(string id, int cell) =>
            Struct(Enum("GadgetID", "EGadgetId", "EGadgetId::" + id), Int("SlotIndex", cell));
        var plain = File(CrossWorldsSaveFormat.SaveGameClass,
            StructProp("_Header", "SaveDataHeader", Int("_RomVersion", 20000)),
            StructProp("_UserMachineAssemblyData", "UserMachineAssemblyData",
                Property("GadgetList", MapOf(EnumType("EGadgetId"), StructType("UserGadgetItemData")),
                    Map((FString("EGadgetId::" + OneA), Struct(Enum("LockType", "ELockType", "ELockType::None"))))),
                Int("GadgetSlotNum", 7),
                ArrayProp("GadgetPresetDataLists", StructType("UserGadgetPresetDataList"),
                    Struct(ArrayProp("list", StructType("UserGadgetPresetData"),
                        Struct(ArrayProp("GadgetDataList", StructType("UserGadgetData"), Gadget(OneA.ToUpperInvariant(), 0))))))));
        using var aes = Aes.Create();
        aes.Key = Key;
        return aes.EncryptEcb(plain, PaddingMode.PKCS7);
    }

    [Fact]
    public void Any_save_format_reads_each_file_with_the_format_that_knows_it()
    {
        var any = new AnySaveFormat(new CrossWorldsSaveFormat(Key), new DevJsonSaveFormat());
        var json = System.Text.Encoding.UTF8.GetBytes(DevSaveJson);

        Assert.True(any.CanRead(GameSave()));
        Assert.True(any.CanRead(json));
        Assert.Equal("rom 20000", any.Read(GameSave()).SaveVersion);
        Assert.Equal("dev-1", any.Read(json).SaveVersion);
        Assert.False(any.CanRead("hello"u8));
        Assert.Throws<SaveFormatException>(() => any.Read("hello"u8));
        Assert.Throws<ArgumentException>(() => new AnySaveFormat());
    }

    [Theory]
    [InlineData("save_aes256_ecb=000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F\nfound=...")]
    [InlineData("0x000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f")]
    [InlineData("  000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F  ")]
    public void The_save_key_is_read_from_a_text_that_holds_it(string text) =>
        Assert.Equal([.. Enumerable.Range(0, 32).Select(i => (byte)i)], CrossWorldsSaveFormat.KeyFromText(text));

    [Theory]
    [InlineData("no key here")]
    [InlineData("0102")]
    [InlineData("000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F00")] // 66 digits
    public void A_text_without_a_key_is_refused(string text) =>
        Assert.Throws<FormatException>(() => CrossWorldsSaveFormat.KeyFromText(text));

    [Fact]
    public void The_app_opens_the_game_save_with_catalog_spelling_and_sends_icon_urls()
    {
        var path = _temp.File("app.sav");
        System.IO.File.WriteAllBytes(path, GameSave());
        var bridge = new AppBridge(new AppBridgeOptions(
            LocalizedCatalog.FromSingle(Catalog),
            new AnySaveFormat(new CrossWorldsSaveFormat(Key), new DevJsonSaveFormat()),
            new FakeUi { SaveToOpen = path },
            new NeverRunning(),
            TimeProvider.System,
            _temp.File("backups"), null, _temp.File("sample.json"),
            new PresetNameStore(_temp.File("names.json")),
            new SeenGameStore(_temp.File("seen.json")))
        {
            IconUrl = id => id == OneA ? $"https://icons.test/{id}.png" : null,
        });

        var catalog = Call(bridge, "catalog");
        var gadgets = catalog["gadgets"]!.AsArray().Cast<JsonObject>().ToDictionary(g => (string)g["id"]!);
        Assert.Equal($"https://icons.test/{OneA}.png", (string?)gadgets[OneA]["icon"]);
        Assert.Null(gadgets[OneB]["icon"]);

        var save = Call(bridge, "openSave");
        var plate = save["players"]![0]!["plates"]![0]!;
        Assert.Equal(OneA, (string?)plate["gadgets"]![0]); // the save spells it in upper case
    }

    private static JsonObject Call(AppBridge bridge, string method)
    {
        var answer = JsonNode.Parse(bridge.Handle(new JsonObject { ["id"] = 1, ["method"] = method, ["params"] = new JsonObject() }.ToJsonString()))!.AsObject();
        Assert.Null(answer["error"]);
        return answer["result"]!.AsObject();
    }

    private sealed class NeverRunning : IGameProcessProbe
    {
        public bool IsGameRunning() => false;
    }
}
