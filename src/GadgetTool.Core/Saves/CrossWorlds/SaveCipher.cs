using System.Security.Cryptography;

namespace GadgetTool.Core.Saves.CrossWorlds;

/// <summary>
/// The game's save encryption: AES-256 in ECB mode with PKCS#7 padding, over the whole GVAS file.
/// The key is not part of this repository; whoever builds the format supplies it.
/// </summary>
internal sealed class SaveCipher
{
    public const int KeySize = 32;
    private const int BlockSize = 16;

    private readonly byte[] _key;

    public SaveCipher(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"The save key is {KeySize} bytes long, not {key.Length}.", nameof(key));
        _key = key.ToArray();
    }

    /// <summary>True when the first block decrypts to the GVAS magic: the right key and a save.</summary>
    public bool LooksLikeSave(ReadOnlySpan<byte> content)
    {
        if (content.Length < BlockSize || content.Length % BlockSize != 0)
            return false;

        using var aes = Create();
        var first = aes.DecryptEcb(content[..BlockSize], PaddingMode.None);
        return first.AsSpan(0, 4).SequenceEqual("GVAS"u8);
    }

    /// <exception cref="SaveFormatException">Not a whole number of blocks, or the padding is wrong (another key, or damage).</exception>
    public byte[] Decrypt(ReadOnlySpan<byte> content)
    {
        if (content.Length == 0 || content.Length % BlockSize != 0)
            throw new SaveFormatException($"The save is damaged: its size ({content.Length} bytes) is not a whole number of {BlockSize}-byte blocks.");

        using var aes = Create();
        try
        {
            return aes.DecryptEcb(content, PaddingMode.PKCS7);
        }
        catch (CryptographicException e)
        {
            throw new SaveFormatException("The save does not decrypt: it is damaged, or it was not made with this key.", e);
        }
    }

    public byte[] Encrypt(ReadOnlySpan<byte> plain)
    {
        using var aes = Create();
        return aes.EncryptEcb(plain, PaddingMode.PKCS7);
    }

    private Aes Create()
    {
        var aes = Aes.Create();
        aes.Key = _key;
        return aes;
    }
}
