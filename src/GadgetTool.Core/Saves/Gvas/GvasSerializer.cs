using System.Buffers.Binary;
using System.Text;

namespace GadgetTool.Core.Saves.Gvas;

/// <summary>
/// Writes properties back in the UE 5.4 tag layout <see cref="GvasReader"/> reads. It covers what
/// the gadget presets use (structs, arrays, enums, ints, strings, bools) and raw values kept as
/// bytes; anything else is refused. Callers check that writing an unchanged property gives
/// back the original bytes before they trust it with a changed one.
/// </summary>
public static class GvasSerializer
{
    private const byte HasArrayIndex = 0x01;
    private const byte HasBinaryOrNativeSerialize = 0x08;
    private const byte BoolTrue = 0x10;
    private const byte Known = HasArrayIndex | HasBinaryOrNativeSerialize | BoolTrue;

    /// <summary>The whole property: its tag (name, type, size, flags) and its value.</summary>
    /// <exception cref="NotSupportedException">A type, value or tag flag this serializer does not write.</exception>
    public static byte[] Property(GvasProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        if ((property.Flags & ~Known) != 0)
            throw new NotSupportedException($"Property '{property.Name}' has tag flags 0x{property.Flags:X2}.");

        var value = new MemoryStream();
        if (property.Type.Name == "BoolProperty")
        {
            if (property.Value is not bool on || on != ((property.Flags & BoolTrue) != 0))
                throw new NotSupportedException($"Bool property '{property.Name}' does not match its flags.");
        }
        else if ((property.Flags & HasBinaryOrNativeSerialize) != 0)
        {
            value.Write((property.Value as GvasRaw ?? throw new NotSupportedException($"'{property.Name}' is native but not raw.")).Bytes);
        }
        else
        {
            WriteValue(value, property.Value, property.Type, inContainer: false);
        }

        var tag = new MemoryStream();
        WriteFString(tag, property.Name);
        WriteType(tag, property.Type);
        WriteInt32(tag, checked((int)value.Length));
        tag.WriteByte(property.Flags);
        if ((property.Flags & HasArrayIndex) != 0)
            WriteInt32(tag, property.ArrayIndex);
        value.Position = 0;
        value.CopyTo(tag);
        return tag.ToArray();
    }

    /// <summary>A struct's value: each property, then "None".</summary>
    public static byte[] Struct(GvasStruct value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var stream = new MemoryStream();
        WriteStruct(stream, value);
        return stream.ToArray();
    }

    private static void WriteStruct(Stream stream, GvasStruct value)
    {
        foreach (var property in value.Properties)
            stream.Write(Property(property));
        WriteFString(stream, "None");
    }

    private static void WriteValue(Stream stream, object value, GvasType type, bool inContainer)
    {
        if (value is GvasRaw raw && !inContainer)
        {
            stream.Write(raw.Bytes);
            return;
        }

        switch (type.Name, value)
        {
            case ("BoolProperty", bool b) when inContainer:
                stream.WriteByte(b ? (byte)1 : (byte)0);
                return;
            case ("IntProperty", int i):
                WriteInt32(stream, i);
                return;
            case ("UInt32Property", uint u):
                Span<byte> four = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(four, u);
                stream.Write(four);
                return;
            case ("Int64Property", long l):
                Span<byte> eight = stackalloc byte[8];
                BinaryPrimitives.WriteInt64LittleEndian(eight, l);
                stream.Write(eight);
                return;
            case ("StrProperty" or "NameProperty" or "EnumProperty", string s):
                WriteFString(stream, s);
                return;
            case ("ByteProperty", string s) when type.At(0) is { Name: not "None" }:
                WriteFString(stream, s);
                return;
            case ("ByteProperty", byte b):
                stream.WriteByte(b);
                return;
            case ("StructProperty", GvasStruct s):
                WriteStruct(stream, s);
                return;
            case ("ArrayProperty", GvasArray a):
                var element = type.At(0) ?? throw new NotSupportedException("An array without element type.");
                WriteInt32(stream, a.Items.Count);
                foreach (var item in a.Items)
                    WriteValue(stream, item, element, inContainer: true);
                return;
            case ("ArrayProperty", GvasRaw { Type: "bytes" } bytes):
                WriteInt32(stream, bytes.Bytes.Length);
                stream.Write(bytes.Bytes);
                return;
            default:
                throw new NotSupportedException($"Writing a {type} holding {value.GetType().Name} is not supported.");
        }
    }

    private static void WriteType(Stream stream, GvasType type)
    {
        WriteFString(stream, type.Name);
        WriteInt32(stream, type.Inner.Count);
        foreach (var inner in type.Inner)
            WriteType(stream, inner);
    }

    /// <summary>As Unreal writes an FString: ASCII as single bytes, anything else as UTF-16; both end with a null.</summary>
    internal static void WriteFString(Stream stream, string text)
    {
        if (text.Length == 0)
        {
            WriteInt32(stream, 0);
            return;
        }
        if (text.All(c => c < 0x80))
        {
            WriteInt32(stream, text.Length + 1);
            stream.Write(Encoding.ASCII.GetBytes(text));
            stream.WriteByte(0);
            return;
        }
        WriteInt32(stream, -(text.Length + 1));
        stream.Write(Encoding.Unicode.GetBytes(text));
        stream.Write([0, 0]);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> four = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(four, value);
        stream.Write(four);
    }
}
