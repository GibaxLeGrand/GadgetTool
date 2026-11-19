using System.Buffers.Binary;
using System.Text;

namespace GadgetTool.Core.Saves.Gvas;

/// <summary>
/// Reads a GVAS file (Unreal Engine SaveGame) written with property tags that carry complete
/// type names (UE 5.4 and later, package UE5 version 1012 and up). Every tagged property is
/// read; values the reader does not decode are kept as <see cref="GvasRaw"/>, so an unknown
/// type elsewhere in the save never stops it.
/// </summary>
public static class GvasReader
{
    /// <summary>The first package UE5 version whose tags carry complete type names.</summary>
    public const int CompleteTypeNameVersion = 1012;

    private const byte HasArrayIndex = 0x01;
    private const byte HasPropertyGuid = 0x02;
    private const byte HasPropertyExtensions = 0x04;
    private const byte HasBinaryOrNativeSerialize = 0x08;
    private const byte BoolTrue = 0x10;
    private const int MaxDepth = 64;

    /// <exception cref="SaveFormatException">Not a GVAS file, an older tag layout, or damaged.</exception>
    public static GvasDocument Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var cursor = new Cursor(data);
        var header = ReadHeader(cursor);
        var root = ReadProperties(cursor, 0);
        return new GvasDocument(header, root, cursor.Position);
    }

    private static GvasHeader ReadHeader(Cursor c)
    {
        if (c.Remaining < 4 || !c.Data.AsSpan(0, 4).SequenceEqual("GVAS"u8))
            throw Damaged("it does not start with GVAS");
        c.Skip(4);

        var saveVersion = c.Int32();
        var ue4 = c.Int32();
        var ue5 = saveVersion >= 3 ? c.Int32() : 0;
        if (ue5 < CompleteTypeNameVersion)
            throw new SaveFormatException(
                $"This save uses an older property layout (package version {ue4}/{ue5}); GadgetTool reads version {CompleteTypeNameVersion} and later only.");

        var major = c.UInt16();
        var minor = c.UInt16();
        var patch = c.UInt16();
        var changelist = c.UInt32();
        var branch = c.FString();
        var customFormat = c.Int32();
        var count = c.Count(20);
        var custom = new List<KeyValuePair<Guid, int>>(count);
        for (var i = 0; i < count; i++)
            custom.Add(new(new Guid(c.Bytes(16)), c.Int32()));
        var saveClass = c.FString();
        // One byte follows the class name in the saves seen so far (always 0); its meaning is unknown.
        var afterClass = c.Byte();

        return new GvasHeader(saveVersion, ue4, ue5, $"{major}.{minor}.{patch}.{changelist}", branch, customFormat, custom, saveClass, afterClass);
    }

    private static GvasStruct ReadProperties(Cursor c, int depth)
    {
        if (depth > MaxDepth)
            throw Damaged("properties are nested too deeply");

        var properties = new List<GvasProperty>();
        while (true)
        {
            var offset = c.Position;
            var name = c.FString();
            if (name == "None")
                return new GvasStruct(properties);

            var type = ReadType(c, 0);
            var size = c.Int32();
            if (size < 0 || size > c.Remaining)
                throw Damaged($"property '{name}' at 0x{offset:X} claims {size} bytes");
            var flags = c.Byte();
            var arrayIndex = (flags & HasArrayIndex) != 0 ? c.Int32() : 0;
            if ((flags & HasPropertyGuid) != 0)
                c.Skip(16);
            if ((flags & HasPropertyExtensions) != 0)
            {
                var extensions = c.Byte();
                if ((extensions & 0x02) != 0) // overridable information
                    c.Byte();
            }

            var valueOffset = c.Position;
            object value = (flags & HasBinaryOrNativeSerialize) != 0
                ? new GvasRaw(type.ToString(), c.Bytes(size))
                : ReadTagged(c, type, size, flags, depth);
            if (c.Position != valueOffset + size)
                throw Damaged($"property '{name}' ({type}) at 0x{offset:X} should take {size} bytes, not {c.Position - valueOffset}");

            properties.Add(new GvasProperty(name, type, flags, arrayIndex, value, offset, valueOffset, size));
        }
    }

    private static GvasType ReadType(Cursor c, int depth)
    {
        if (depth > MaxDepth)
            throw Damaged("a property type is nested too deeply");
        var name = c.FString();
        var inner = c.Count(8);
        var types = new GvasType[inner];
        for (var i = 0; i < inner; i++)
            types[i] = ReadType(c, depth + 1);
        return new GvasType(name, types);
    }

    private static object ReadTagged(Cursor c, GvasType type, int size, byte flags, int depth)
    {
        if (type.Name == "BoolProperty")
            return (flags & BoolTrue) != 0;

        // A value the reader cannot decode is kept as raw bytes: the tag gives its size.
        var start = c.Position;
        try
        {
            return ReadValue(c, type, depth, inContainer: false);
        }
        catch (Exception e) when (e is NotSupportedException or SaveFormatException)
        {
            c.Position = start;
            return new GvasRaw(type.ToString(), c.Bytes(size));
        }
    }

    private static object ReadValue(Cursor c, GvasType type, int depth, bool inContainer)
    {
        switch (type.Name)
        {
            case "BoolProperty": return c.Byte() != 0;
            case "Int8Property": return (sbyte)c.Byte();
            case "Int16Property": return BinaryPrimitives.ReadInt16LittleEndian(c.Bytes(2));
            case "UInt16Property": return c.UInt16();
            case "IntProperty": return c.Int32();
            case "UInt32Property": return c.UInt32();
            case "Int64Property": return BinaryPrimitives.ReadInt64LittleEndian(c.Bytes(8));
            case "UInt64Property": return BinaryPrimitives.ReadUInt64LittleEndian(c.Bytes(8));
            case "FloatProperty": return BinaryPrimitives.ReadSingleLittleEndian(c.Bytes(4));
            case "DoubleProperty": return BinaryPrimitives.ReadDoubleLittleEndian(c.Bytes(8));
            case "StrProperty":
            case "NameProperty":
            case "ObjectProperty":
            case "EnumProperty":
                return c.FString();
            case "SoftObjectProperty":
                return $"{c.FString()}.{c.FString()}:{c.FString()}";
            case "ByteProperty":
                // With an enum, the value is the enumerator's name; without, one byte.
                return type.At(0) is { Name: not "None" } ? c.FString() : c.Byte();
            case "StructProperty":
                return ReadStruct(c, type.At(0)?.Name ?? "", depth);
            case "ArrayProperty":
                return ReadArray(c, type.At(0) ?? throw Damaged("an array has no element type"), depth);
            case "SetProperty":
            {
                var element = type.At(0) ?? throw Damaged("a set has no element type");
                SkipRemoved(c, element, depth);
                return ReadItems(c, element, depth);
            }
            case "MapProperty":
                return ReadMap(c, type, depth);
            default:
                throw new NotSupportedException($"{type} {(inContainer ? "in a container" : "")}");
        }
    }

    private static object ReadStruct(Cursor c, string structName, int depth)
    {
        // Structs Unreal writes in binary inside containers; tagged ones carry the native flag instead.
        var binarySize = structName switch
        {
            "Color" => 4,
            "DateTime" or "Timespan" or "IntPoint" => 8,
            "IntVector" => 12,
            "Guid" or "Vector2D" or "LinearColor" => 16,
            "Vector" or "Rotator" => 24,
            "Quat" or "Vector4" => 32,
            _ => 0,
        };
        return binarySize > 0 ? new GvasRaw(structName, c.Bytes(binarySize)) : ReadProperties(c, depth + 1);
    }

    private static object ReadArray(Cursor c, GvasType element, int depth)
    {
        if (element.Name == "ByteProperty" && element.At(0) is null or { Name: "None" })
        {
            var length = c.Count(1);
            return new GvasRaw("bytes", c.Bytes(length));
        }
        return ReadItems(c, element, depth);
    }

    private static GvasArray ReadItems(Cursor c, GvasType element, int depth)
    {
        var count = c.Count(1);
        var items = new List<object>(count);
        for (var i = 0; i < count; i++)
            items.Add(ReadValue(c, element, depth + 1, inContainer: true));
        return new GvasArray(element, items);
    }

    private static GvasMap ReadMap(Cursor c, GvasType type, int depth)
    {
        var keyType = type.At(0) ?? throw Damaged("a map has no key type");
        var valueType = type.At(1) ?? throw Damaged("a map has no value type");
        SkipRemoved(c, keyType, depth);
        var count = c.Count(2);
        var entries = new List<KeyValuePair<object, object>>(count);
        for (var i = 0; i < count; i++)
        {
            var key = ReadValue(c, keyType, depth + 1, inContainer: true);
            var value = ReadValue(c, valueType, depth + 1, inContainer: true);
            entries.Add(new(key, value));
        }
        return new GvasMap(keyType, valueType, entries);
    }

    private static void SkipRemoved(Cursor c, GvasType keyType, int depth)
    {
        var removed = c.Count(1);
        for (var i = 0; i < removed; i++)
            ReadValue(c, keyType, depth + 1, inContainer: true);
    }

    private static SaveFormatException Damaged(string why) => new($"The save is damaged: {why}.");

    private sealed class Cursor(byte[] data)
    {
        public byte[] Data { get; } = data;

        public int Position { get; set; }

        public int Remaining => Data.Length - Position;

        public void Skip(int count) => Bytes(count);

        public byte[] Bytes(int count)
        {
            if (count < 0 || count > Remaining)
                throw Damaged($"it ends early at 0x{Position:X}");
            var bytes = Data.AsSpan(Position, count).ToArray();
            Position += count;
            return bytes;
        }

        public byte Byte() => Bytes(1)[0];

        public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Bytes(2));

        public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Bytes(4));

        public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4));

        /// <summary>An element count, checked against what is left (each element takes at least <paramref name="minSize"/> bytes).</summary>
        public int Count(int minSize)
        {
            var at = Position;
            var count = Int32();
            if (count < 0 || (long)count * minSize > Remaining)
                throw Damaged($"a count of {count} at 0x{at:X} does not fit");
            return count;
        }

        /// <summary>An FString: positive length = Latin-1 bytes, negative = UTF-16; both end with a null.</summary>
        public string FString()
        {
            var at = Position;
            var length = Int32();
            if (length == 0)
                return "";
            if (length > 0)
            {
                if (length > Remaining)
                    throw Damaged($"a string at 0x{at:X} is {length} bytes long");
                var bytes = Bytes(length);
                return Encoding.Latin1.GetString(bytes, 0, length - 1);
            }
            if (length == int.MinValue || -(long)length * 2 > Remaining)
                throw Damaged($"a string at 0x{at:X} is {-(long)length} characters long");
            var utf16 = Bytes(-length * 2);
            return Encoding.Unicode.GetString(utf16, 0, utf16.Length - 2);
        }
    }
}
