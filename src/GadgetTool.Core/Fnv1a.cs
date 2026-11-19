namespace GadgetTool.Core;

/// <summary>32-bit FNV-1a. Not cryptographic: used for short, stable identifiers and checksums.</summary>
internal static class Fnv1a
{
    private const uint OffsetBasis = 2166136261;
    private const uint Prime = 16777619;

    public static uint Hash(ReadOnlySpan<byte> data)
    {
        var hash = OffsetBasis;
        foreach (var b in data)
        {
            hash ^= b;
            hash *= Prime;
        }
        return hash;
    }
}
