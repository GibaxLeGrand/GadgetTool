using GadgetTool.Core.Saves;
using GadgetTool.Core.Saves.Gvas;
using static GadgetTool.Core.Tests.GvasWriter;

namespace GadgetTool.Core.Tests;

public class GvasReaderTests
{
    private const string SaveClass = "/Script/TestModule.TestSave";

    [Fact]
    public void Reads_the_header()
    {
        var doc = GvasReader.Read(File(SaveClass, Int("Count", 1)));

        Assert.Equal(3, doc.Header.SaveGameVersion);
        Assert.Equal(522, doc.Header.PackageUe4Version);
        Assert.Equal(1012, doc.Header.PackageUe5Version);
        Assert.Equal("5.4.3.0", doc.Header.EngineVersion);
        Assert.Equal("test-branch", doc.Header.EngineBranch);
        Assert.Equal(7, Assert.Single(doc.Header.CustomVersions).Value);
        Assert.Equal(SaveClass, doc.Header.SaveGameClass);
    }

    [Fact]
    public void Reads_nested_structs_arrays_maps_and_simple_values()
    {
        var data = File(SaveClass,
            StructProp("_Header", "Header", Int("_Version", 42)),
            Str("Name", "Kart"),
            Bool("On", true),
            Bool("Off", false),
            ArrayProp("Entries", StructType("Entry"),
                Struct(Enum("Kind", "EKind", "EKind::First"), Int("Cell", 3)),
                Struct(Enum("Kind", "EKind", "EKind::Second"), Int("Cell", 0))),
            Property("Owned", MapOf(EnumType("EKind"), StructType("Lock")),
                Map((FString("EKind::First"), Struct(Bool("bIsNew", true), Enum("LockType", "ELock", "ELock::None"))))),
            Int("After", 7));

        var doc = GvasReader.Read(data);
        var root = doc.Root;

        Assert.Equal(42, root.Get<GvasStruct>("_Header")!.Find("_Version")!.Value);
        Assert.Equal("Kart", root.Find("Name")!.Value);
        Assert.Equal(true, root.Find("On")!.Value);
        Assert.Equal(false, root.Find("Off")!.Value);

        var entries = root.Get<GvasArray>("Entries")!.Items.Cast<GvasStruct>().ToList();
        Assert.Equal(["EKind::First", "EKind::Second"], entries.Select(e => (string)e.Find("Kind")!.Value));
        Assert.Equal([3, 0], entries.Select(e => (int)e.Find("Cell")!.Value));

        var owned = Assert.Single(root.Get<GvasMap>("Owned")!.Entries);
        Assert.Equal("EKind::First", owned.Key);
        var value = (GvasStruct)owned.Value;
        Assert.Equal(true, value.Find("bIsNew")!.Value); // a bool inside a container is a byte, not a tag flag
        Assert.Equal("ELock::None", value.Find("LockType")!.Value);

        Assert.Equal(7, root.Find("After")!.Value);
        Assert.Equal(data.Length - 4, doc.End);
    }

    [Fact]
    public void Keeps_unknown_types_and_native_structs_as_bytes_and_reads_on()
    {
        var data = File(SaveClass,
            Property("Fancy", new GvasType("FancyProperty"), [1, 2, 3]),
            Property("Stamp", StructType("DateTime"), [9, 9, 9, 9, 9, 9, 9, 9], flags: 0x08),
            Int("After", 5));

        var root = GvasReader.Read(data).Root;

        Assert.Equal([1, 2, 3], Assert.IsType<GvasRaw>(root.Find("Fancy")!.Value).Bytes);
        Assert.Equal(8, Assert.IsType<GvasRaw>(root.Find("Stamp")!.Value).Bytes.Length);
        Assert.Equal(5, root.Find("After")!.Value);
    }

    [Fact]
    public void Keeps_an_unreadable_container_as_bytes()
    {
        // An array of a type the reader does not know: the tag's size still lets it skip.
        var data = File(SaveClass,
            ArrayProp("Odd", new GvasType("FancyProperty"), [1], [2]),
            Int("After", 5));

        var root = GvasReader.Read(data).Root;

        Assert.IsType<GvasRaw>(root.Find("Odd")!.Value);
        Assert.Equal(5, root.Find("After")!.Value);
    }

    [Fact]
    public void Refuses_older_tag_layouts()
    {
        var e = Assert.Throws<SaveFormatException>(() => GvasReader.Read(File(SaveClass, 1009, Int("A", 1))));
        Assert.Contains("older property layout", e.Message);
    }

    [Fact]
    public void Refuses_damaged_files()
    {
        var good = File(SaveClass, Int("A", 1));

        Assert.Throws<SaveFormatException>(() => GvasReader.Read("NOPE"u8.ToArray()));
        Assert.Throws<SaveFormatException>(() => GvasReader.Read(good[..^12]));

        // An int whose tag claims 8 bytes.
        var wrongSize = File(SaveClass, Concat(FString("A"), Type(IntType), Int32(8), [0], Int32(1), Int32(2)));
        var e = Assert.Throws<SaveFormatException>(() => GvasReader.Read(wrongSize));
        Assert.Contains("should take 8 bytes", e.Message);
    }
}
