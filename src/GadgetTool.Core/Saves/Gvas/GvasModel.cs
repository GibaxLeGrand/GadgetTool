namespace GadgetTool.Core.Saves.Gvas;

/// <summary>
/// A property's type as Unreal Engine 5.4 writes it in a tag: a name and its inner types, for
/// example <c>ArrayProperty(StructProperty(UserGadgetData(/Script/UnionSystem)))</c>.
/// </summary>
public sealed class GvasType
{
    public GvasType(string name, params GvasType[] inner)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(inner);
        Name = name;
        Inner = inner;
    }

    public string Name { get; }

    public IReadOnlyList<GvasType> Inner { get; }

    /// <summary>The inner type at <paramref name="index"/>, or null.</summary>
    public GvasType? At(int index) => index < Inner.Count ? Inner[index] : null;

    public override string ToString() => Inner.Count == 0 ? Name : $"{Name}({string.Join(", ", Inner)})";
}

/// <summary>One tagged property: its name, type and value, and where it sits in the file.</summary>
/// <param name="Offset">Where the tag starts (its name).</param>
/// <param name="ValueOffset">Where the value starts.</param>
/// <param name="Size">The value's size in bytes, as the tag gives it.</param>
public sealed record GvasProperty(string Name, GvasType Type, byte Flags, int ArrayIndex, object Value, int Offset, int ValueOffset, int Size);

/// <summary>A struct written as tagged properties, ending with "None".</summary>
public sealed class GvasStruct(IReadOnlyList<GvasProperty> properties)
{
    public IReadOnlyList<GvasProperty> Properties { get; } = properties;

    /// <summary>The first property with this name, or null.</summary>
    public GvasProperty? Find(string name) => Properties.FirstOrDefault(p => p.Name == name);

    /// <summary>The value of the first property with this name, if it has the expected type.</summary>
    public T? Get<T>(string name) where T : class => Find(name)?.Value as T;
}

/// <summary>An array or set: its element type and the elements.</summary>
public sealed record GvasArray(GvasType ElementType, IReadOnlyList<object> Items);

/// <summary>A map: key and value types and the entries, in file order.</summary>
public sealed record GvasMap(GvasType KeyType, GvasType ValueType, IReadOnlyList<KeyValuePair<object, object>> Entries);

/// <summary>Bytes the reader keeps as they are: a natively serialized struct, or a type it does not decode.</summary>
public sealed record GvasRaw(string Type, byte[] Bytes);

/// <summary>The header of a GVAS file (an Unreal Engine SaveGame).</summary>
public sealed record GvasHeader(
    int SaveGameVersion,
    int PackageUe4Version,
    int PackageUe5Version,
    string EngineVersion,
    string EngineBranch,
    int CustomVersionFormat,
    IReadOnlyList<KeyValuePair<Guid, int>> CustomVersions,
    string SaveGameClass,
    byte AfterClass);

/// <summary>A whole GVAS file: header and top-level properties.</summary>
/// <param name="End">Where the properties end (after the closing "None").</param>
public sealed record GvasDocument(GvasHeader Header, GvasStruct Root, int End);
