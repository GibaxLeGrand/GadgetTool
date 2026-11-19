using GadgetTool.Core.Setups;

namespace GadgetTool.Core.Saves;

/// <summary>
/// Reads the gadget part of a save and writes it back. The real CrossWorlds format is not
/// known yet; <see cref="DevJsonSaveFormat"/> stands in for it meanwhile.
/// </summary>
public interface ISaveFormat
{
    string Name { get; }

    /// <summary>True when the content looks like this format (a quick check, no full parse).</summary>
    bool CanRead(ReadOnlySpan<byte> content);

    /// <exception cref="SaveFormatException">The content is damaged or of a version this format does not know.</exception>
    SaveSetups Read(ReadOnlySpan<byte> content);

    /// <summary>
    /// Returns <paramref name="original"/> with only the plates replaced by those of
    /// <paramref name="setups"/>. Everything else in the save is kept byte for byte where the
    /// format allows it.
    /// </summary>
    /// <exception cref="SaveFormatException">The original is damaged or of an unknown version.</exception>
    byte[] Write(ReadOnlySpan<byte> original, SaveSetups setups);
}

/// <summary>A save this format cannot read or write.</summary>
public sealed class SaveFormatException(string message, Exception? inner = null) : Exception(message, inner);
