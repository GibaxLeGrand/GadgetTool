using System.Buffers.Binary;
using System.Buffers.Text;
using GadgetTool.Core.Gadgets;
using GadgetTool.Core.Setups;

namespace GadgetTool.Core.Sharing;

/// <summary>A share code, read back.</summary>
/// <param name="Plate">The gadgets this game version knows (the others are left out).</param>
/// <param name="GameVersion">The game version the code was made with.</param>
/// <param name="Changes">Gadgets whose slot cost changed since, or that this version does not have.</param>
/// <param name="Missing">How many of the code's gadgets this game version does not know (their ids cannot be recovered).</param>
/// <param name="Age">The code's game version against ours: <see cref="ShareAge.Newer"/> cannot be imported.</param>
public sealed record DecodedShareCode(GadgetPlate Plate, GameVersion GameVersion, IReadOnlyList<GadgetChange> Changes, int Missing, ShareAge Age)
{
    /// <summary>False for a code from a newer game version than ours.</summary>
    public bool CanImport => Age != ShareAge.Newer;

    /// <summary>Compatible as it is: importable, and none of its gadgets changed.</summary>
    public bool Unchanged => CanImport && Changes.Count == 0 && Missing == 0;
}

/// <summary>
/// A short text code for one plate, to paste in a chat: <c>GT1-</c> followed by base64url
/// bytes. Six gadgets and a Super Slot gadget give a code of about 55 characters.
/// </summary>
/// <remarks>
/// Bytes: format (1), number of row gadgets, flags (bit 0: a Super Slot gadget follows), the
/// game version (3 bytes), one 32-bit hash per gadget (little-endian, see
/// <see cref="GadgetCatalog"/>; rows, then the Super Slot), each gadget's slot cost when
/// shared (2 bits each, 0 = unknown), then a 16-bit checksum of everything before it, so a
/// code mangled by a typo or a cut is refused instead of decoded into another setup.
/// </remarks>
public static class ShareCode
{
    public const string Prefix = "GT1-";
    public const int MaxGadgets = 32;
    private const byte FormatVersion = 1;
    private const byte HasSuperFlag = 0x01;
    private const int HeaderSize = 6;
    private const int ChecksumSize = 2;

    private static int CostBytes(int gadgets) => (gadgets + 3) / 4;

    private static int Length(int gadgets) => HeaderSize + 4 * gadgets + CostBytes(gadgets) + ChecksumSize;

    public static string Encode(GadgetPlate plate, GadgetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(plate);
        ArgumentNullException.ThrowIfNull(catalog);
        if (plate.GadgetIds.Count > MaxGadgets)
            throw new ArgumentException($"A share code holds at most {MaxGadgets} gadgets.", nameof(plate));

        var ids = plate.GadgetIds.ToList();
        if (plate.SuperGadgetId is { } super)
            ids.Add(super);

        var bytes = new byte[Length(ids.Count)];
        bytes[0] = FormatVersion;
        bytes[1] = (byte)plate.GadgetIds.Count;
        bytes[2] = plate.SuperGadgetId is null ? (byte)0 : HasSuperFlag;
        bytes[3] = catalog.GameVersion.Major;
        bytes[4] = catalog.GameVersion.Minor;
        bytes[5] = catalog.GameVersion.Patch;

        var costs = bytes.AsSpan(HeaderSize + 4 * ids.Count, CostBytes(ids.Count));
        for (var i = 0; i < ids.Count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(HeaderSize + 4 * i), GadgetCatalog.ShareHash(ids[i]));
            var cost = catalog.TryGet(ids[i], out var g) && g.SlotCost is >= 1 and <= 3 ? g.SlotCost : 0;
            costs[i / 4] |= (byte)(cost << (2 * (i % 4)));
        }
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - ChecksumSize), Checksum(bytes.AsSpan(0, bytes.Length - ChecksumSize)));

        return Prefix + Base64Url.EncodeToString(bytes);
    }

    /// <exception cref="FormatException">The text is not a valid share code.</exception>
    public static DecodedShareCode Decode(string code, GadgetCatalog catalog)
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
        var total = count + (hasSuper ? 1 : 0);
        if (count > MaxGadgets || (bytes[2] & ~HasSuperFlag) != 0 || bytes.Length != Length(total))
            throw new FormatException("The code is damaged: its length does not match its content.");
        if (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(bytes.Length - ChecksumSize)) != Checksum(bytes.AsSpan(0, bytes.Length - ChecksumSize)))
            throw new FormatException("The code is damaged: its checksum does not match (a typo, or a character lost when copying).");

        var version = new GameVersion(bytes[3], bytes[4], bytes[5]);
        var costs = bytes.AsSpan(HeaderSize + 4 * total, CostBytes(total));
        var rows = new List<string>();
        string? super = null;
        var then = new List<(string, string?, int)>();
        var missing = 0;

        for (var i = 0; i < total; i++)
        {
            var hash = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(HeaderSize + 4 * i));
            var cost = (costs[i / 4] >> (2 * (i % 4))) & 0b11;
            if (!catalog.TryGetByShareHash(hash, out var gadget))
            {
                missing++;
                continue;
            }

            if (i < count)
                rows.Add(gadget.Id);
            else
                super = gadget.Id;
            if (cost != 0)
                then.Add((gadget.Id, gadget.Name, cost));
        }

        var age = GameVersion.AgeOf(version, catalog.GameVersion);
        IReadOnlyList<GadgetChange> changes = age == ShareAge.Same ? [] : GameChanges.Compare(then, catalog);
        return new DecodedShareCode(new GadgetPlate(rows, super), version, changes, missing, age);
    }

    private static ushort Checksum(ReadOnlySpan<byte> data)
    {
        var hash = Fnv1a.Hash(data);
        return (ushort)(hash ^ (hash >> 16));
    }
}
