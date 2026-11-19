using GadgetTool.Core.Saves.Gvas;
using static GadgetTool.Core.Tests.GvasWriter;

namespace GadgetTool.Core.Tests;

public class GvasSerializerTests
{
    private const string SaveClass = "/Script/TestModule.TestSave";

    [Fact]
    public void Writes_each_property_back_byte_for_byte()
    {
        var data = File(SaveClass,
            StructProp("_Header", "Header", Int("_Version", 42), Str("Name", "Kart"), Str("Wide", "Kärt ✓")),
            Bool("On", true),
            Bool("Off", false),
            ArrayProp("Entries", StructType("Entry"),
                Struct(Enum("Kind", "EKind", "EKind::First"), Int("Cell", 3)),
                Struct(Enum("Kind", "EKind", "EKind::Second"), Int("Cell", 0))),
            ArrayProp("Empty", StructType("Entry")),
            Property("Stamp", StructType("DateTime"), [1, 2, 3, 4, 5, 6, 7, 8], flags: 0x08),
            Property("Fancy", new GvasType("FancyProperty"), [9, 9]));

        var doc = GvasReader.Read(data);

        foreach (var property in doc.Root.Properties)
            Assert.Equal(data[property.Offset..property.End], GvasSerializer.Property(property));
    }

    [Fact]
    public void A_changed_value_gets_its_new_size_in_the_tag()
    {
        var doc = GvasReader.Read(File(SaveClass, Str("Name", "ab")));
        var property = doc.Root.Find("Name")! with { Value = "abcdef" };

        var bytes = GvasSerializer.Property(property);
        var reread = GvasReader.Read(File(SaveClass, bytes)).Root.Find("Name")!;

        Assert.Equal("abcdef", reread.Value);
        Assert.Equal(4 + 7, reread.Size); // FString: length, then the text and its null
    }

    [Fact]
    public void Refuses_what_it_does_not_write()
    {
        var doc = GvasReader.Read(File(SaveClass,
            Property("Owned", MapOf(EnumType("EKind"), IntType), Map((FString("EKind::First"), Int32(1)))),
            Concat(FString("Guarded"), Type(IntType), Int32(4), [0x02], new byte[16], Int32(1)))); // a property GUID

        Assert.Throws<NotSupportedException>(() => GvasSerializer.Property(doc.Root.Find("Owned")!));
        Assert.Throws<NotSupportedException>(() => GvasSerializer.Property(doc.Root.Find("Guarded")!));
    }
}
