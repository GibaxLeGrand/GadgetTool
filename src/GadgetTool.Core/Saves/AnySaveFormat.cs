using GadgetTool.Core.Setups;

namespace GadgetTool.Core.Saves;

/// <summary>
/// Several save formats as one: each file is read and written by the first format that
/// recognises it (the game's save, the made-up sample...).
/// </summary>
public sealed class AnySaveFormat : ISaveFormat
{
    private readonly IReadOnlyList<ISaveFormat> _formats;

    public AnySaveFormat(params ISaveFormat[] formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        if (formats.Length == 0 || formats.Any(f => f is null))
            throw new ArgumentException("At least one format, and no null ones.", nameof(formats));
        _formats = formats;
    }

    public string Name => string.Join(" or ", _formats.Select(f => f.Name));

    public bool CanRead(ReadOnlySpan<byte> content) => Find(content) is not null;

    public SaveSetups Read(ReadOnlySpan<byte> content) => Pick(content).Read(content);

    public byte[] Write(ReadOnlySpan<byte> original, SaveSetups setups) => Pick(original).Write(original, setups);

    private ISaveFormat? Find(ReadOnlySpan<byte> content)
    {
        foreach (var format in _formats)
        {
            if (format.CanRead(content))
                return format;
        }
        return null;
    }

    private ISaveFormat Pick(ReadOnlySpan<byte> content) =>
        Find(content) ?? throw new SaveFormatException($"This file is not a save GadgetTool can read ({Name}).");
}
