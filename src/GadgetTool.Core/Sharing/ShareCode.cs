using System.Buffers.Binary;
using System.Buffers.Text;
using GadgetTool.Core.Gadgets;
using GadgetTool.Core.Setups;

namespace GadgetTool.Core.Sharing;

/// <summary>
/// A short text code for one plate, to paste in a chat: <c>GT1-</c> followed by base64url
/// bytes. Six gadgets and a Super Slot gadget give a code of about 50 characters.
/// </summary>
/// <remarks>
/// Bytes: format version (1), number of row gadgets, flags (bit 0: a Super Slot gadget
/// follows), one 32-bit hash per gadget (little-endian, see <see cref="GadgetCatalog"/>), the
/// Super Slot gadget's hash if any, then a 16-bit checksum of everything before it, so a code
/// mangled by a typo or a cut is refused instead of decoded into another setup.
/// </remarks>
public static class ShareCode
{
    public const string Prefix = "GT1-";
    public const int MaxGadgets = 32;
    private const byte FormatVersion = 1;
    private const byte HasSuperFlag = 0x01;
    private const int HeaderSize = 3;
    private const int ChecksumSize = 2;

    public static string Encode(GadgetPlate plate)
    {
        ArgumentNullException.ThrowIfNull(plate);
        if (plate.GadgetIds.Count > MaxGadgets)
            throw new ArgumentException($"A share code holds at most {MaxGadgets} gadgets.", nameof(plate));

        var hashes = plate.GadgetIds.Select(GadgetCatalog.ShareHash).ToList();
        if (plate.SuperGadgetId is { } super)
            hashes.Add(GadgetCatalog.ShareHash(super));

        var bytes = new byte[HeaderSize + 4 * hashes.Count + ChecksumSize];
        bytes[0] = FormatVersion;
        bytes[1] = (byte)plate.GadgetIds.Count;
        bytes[2] = plate.SuperGadgetId is null ? (byte)0 : HasSuperFlag;
        for (var i = 0; i < hashes.Count; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(HeaderSize + 4 * i), hashes[i]);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - ChecksumSize), Checksum(bytes.AsSpan(0, bytes.Length - ChecksumSize)));

        return Prefix + Base64Url.EncodeToString(bytes);
    }

    /// <exception cref="FormatException">The code is not a valid share code for this catalog.</exception>
    public static GadgetPlate Decode(string code, GadgetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(catalog);

        var text = code.Trim();
        if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            throw new FormatException($"Not a GadgetTool code: it should start with '{Prefix}'.");

        byte[] bytes;
        try
        {
            bytes = Base64Url.DecodeFromChars(text.AsSpan(Prefix.Length));
        }
        catch (FormatException)
        {
            throw new FormatException("The code is damaged: it holds characters a code cannot contain.");
        }

        if (bytes.Length < HeaderSize + ChecksumSize)
            throw new FormatException("The code is too short; part of it may be missing.");
        if (bytes[0] != FormatVersion)
            throw new FormatException($"The code uses format {bytes[0]}, which this version of GadgetTool does not know.");

        var count = bytes[1];
        var hasSuper = (bytes[2] & HasSuperFlag) != 0;
        var hashCount = count + (hasSuper ? 1 : 0);
        if (count > MaxGadgets || (bytes[2] & ~HasSuperFlag) != 0 || bytes.Length != HeaderSize + 4 * hashCount + ChecksumSize)
            throw new FormatException("The code is damaged: its length does not match its content.");
        if (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(bytes.Length - ChecksumSize)) != Checksum(bytes.AsSpan(0, bytes.Length - ChecksumSize)))
            throw new FormatException("The code is damaged: its checksum does not match (a typo, or a character lost when copying).");

        var ids = new string[hashCount];
        for (var i = 0; i < hashCount; i++)
        {
            var hash = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(HeaderSize + 4 * i));
            if (!catalog.TryGetByShareHash(hash, out var gadget))
                throw new FormatException($"The code holds a gadget this game version does not know ({hash:x8}); it may come from a newer version.");
            ids[i] = gadget.Id;
        }

        return hasSuper ? new GadgetPlate(ids[..count], ids[count]) : new GadgetPlate(ids);
    }

    private static ushort Checksum(ReadOnlySpan<byte> data)
    {
        var hash = Fnv1a.Hash(data);
        return (ushort)(hash ^ (hash >> 16));
    }
}
