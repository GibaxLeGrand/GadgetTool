using System.Buffers.Binary;
using System.Text;
using GadgetTool.Core.Saves.Gvas;

namespace GadgetTool.Core.Tests;

/// <summary>
/// Builds GVAS files the way Unreal Engine 5.4 writes them (tags with complete type names), to
/// test the reader without any real save.
/// </summary>
internal static class GvasWriter
{
    public const string Package = "/Script/TestModule";

    public static GvasType IntType { get; } = new("IntProperty");
    public static GvasType StrType { get; } = new("StrProperty");
    public static GvasType BoolType { get; } = new("BoolProperty");

    public static GvasType StructType(string name) => new("StructProperty", new GvasType(name, new GvasType(Package)));

    public static GvasType EnumType(string name) => new("EnumProperty", new GvasType(name, new GvasType(Package)), new GvasType("ByteProperty"));

    public static GvasType ArrayOf(GvasType element) => new("ArrayProperty", element);

    public static GvasType MapOf(GvasType key, GvasType value) => new("MapProperty", key, value);

    /// <summary>A whole file: header, the properties, "None" and the 4 zero bytes the game writes after it.</summary>
    public static byte[] File(string saveClass, params byte[][] properties) => File(saveClass, 1012, properties);

    public static byte[] File(string saveClass, int ue5Version, params byte[][] properties) => Concat(
        "GVAS"u8.ToArray(), Int32(3), Int32(522), Int32(ue5Version),
        UInt16(5), UInt16(4), UInt16(3), Int32(0), FString("test-branch"),
        Int32(3), Int32(1), new Guid("00000000-0000-0000-0000-000000000001").ToByteArray(), Int32(7),
        FString(saveClass), [0],
        Concat(properties), FString("None"), Int32(0));

    public static byte[] Property(string name, GvasType type, byte[] value, byte flags = 0) =>
        Concat(FString(name), Type(type), Int32(value.Length), [flags], value);

    public static byte[] Int(string name, int value) => Property(name, IntType, Int32(value));

    public static byte[] Str(string name, string value) => Property(name, StrType, FString(value));

    public static byte[] Bool(string name, bool value) => Property(name, BoolType, [], value ? (byte)0x10 : (byte)0);

    public static byte[] Enum(string name, string enumName, string value) => Property(name, EnumType(enumName), FString(value));

    public static byte[] StructProp(string name, string structName, params byte[][] properties) =>
        Property(name, StructType(structName), Struct(properties));

    public static byte[] ArrayProp(string name, GvasType element, params byte[][] items) =>
        Property(name, ArrayOf(element), Array(items));

    /// <summary>A struct's value: its properties and "None".</summary>
    public static byte[] Struct(params byte[][] properties) => Concat(Concat(properties), FString("None"));

    public static byte[] Array(params byte[][] items) => Concat(Int32(items.Length), Concat(items));

    public static byte[] Map(params (byte[] Key, byte[] Value)[] entries) =>
        Concat(Int32(0), Int32(entries.Length), Concat(entries.Select(e => Concat(e.Key, e.Value)).ToArray()));

    public static byte[] Type(GvasType type) =>
        Concat(FString(type.Name), Int32(type.Inner.Count), Concat(type.Inner.Select(Type).ToArray()));

    public static byte[] FString(string text) =>
        text.Length == 0 ? Int32(0) : Concat(Int32(text.Length + 1), Encoding.Latin1.GetBytes(text), [0]);

    public static byte[] Int32(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }

    public static byte[] UInt16(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        return bytes;
    }

    public static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(p => p)];
}
