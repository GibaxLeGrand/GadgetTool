using System.Buffers.Binary;
using System.IO.Compression;

namespace GadgetTool.Extractor;

/// <summary>
/// Writes 8-bit RGBA pixels as a PNG file (no filter, zlib at its best level). Enough for the
/// gadget icons, without an imaging library.
/// </summary>
internal static class PngWriter
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <param name="rgba">Rows from the top, 4 bytes per pixel (red, green, blue, alpha).</param>
    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (rgba.Length != (long)width * height * 4)
            throw new ArgumentException($"{width}x{height} RGBA pixels take {(long)width * height * 4} bytes, not {rgba.Length}.", nameof(rgba));

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;  // bits per channel
        header[9] = 6;  // colour type: RGBA
        // compression, filter and interlace methods stay 0

        using var pixels = new MemoryStream();
        using (var zlib = new ZLibStream(pixels, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var stride = width * 4;
            for (var y = 0; y < height; y++)
            {
                zlib.WriteByte(0); // filter: none
                zlib.Write(rgba.Slice(y * stride, stride));
            }
        }

        using var png = new MemoryStream();
        png.Write(Signature);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", pixels.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> four = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(four, data.Length);
        stream.Write(four);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        var crc = Crc(Crc(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(four, crc);
        stream.Write(four);
    }

    /// <summary>
    /// The pixels cut down to the box that holds every pixel more opaque than
    /// <paramref name="alphaThreshold"/>; an image with none comes back as it is.
    /// </summary>
    public static (int Width, int Height, byte[] Rgba) TrimTransparent(int width, int height, byte[] rgba, byte alphaThreshold = 8)
    {
        int left = width, top = height, right = -1, bottom = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (rgba[(y * width + x) * 4 + 3] <= alphaThreshold)
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }
        if (right < 0)
            return (width, height, rgba);

        int w = right - left + 1, h = bottom - top + 1;
        var cut = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            Array.Copy(rgba, ((top + y) * width + left) * 4, cut, y * w * 4, w * 4);
        return (w, h, cut);
    }

    /// <summary>
    /// The pixels moved so that the box around what is drawn (alpha above
    /// <paramref name="alphaThreshold"/>) sits in the middle of the image; the size stays. The
    /// game's icons are not always drawn in the middle of their canvas (up to 40 pixels off out
    /// of 512), and Gibax wants every icon centred on its base. Pixels moved past an edge are
    /// dropped (the game's icons have room enough around them).
    /// </summary>
    public static byte[] CenterDrawing(int width, int height, byte[] rgba, byte alphaThreshold = 24)
    {
        int left = width, top = height, right = -1, bottom = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (rgba[(y * width + x) * 4 + 3] <= alphaThreshold)
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }
        if (right < 0)
            return rgba;

        // Twice the shift, to stay in whole pixels: (width - 1) - (left + right) is 2 * (centre - middle).
        var dx = (int)Math.Round((width - 1 - left - right) / 2.0, MidpointRounding.AwayFromZero);
        var dy = (int)Math.Round((height - 1 - top - bottom) / 2.0, MidpointRounding.AwayFromZero);
        if (dx == 0 && dy == 0)
            return rgba;

        var moved = new byte[rgba.Length];
        for (var y = 0; y < height; y++)
        {
            var ny = y + dy;
            if (ny < 0 || ny >= height)
                continue;
            for (var x = 0; x < width; x++)
            {
                var nx = x + dx;
                if (nx < 0 || nx >= width)
                    continue;
                Array.Copy(rgba, (y * width + x) * 4, moved, (ny * width + nx) * 4, 4);
            }
        }
        return moved;
    }

    /// <summary>A rectangle of the pixels (it must lie inside the image).</summary>
    public static byte[] Crop(int width, int height, byte[] rgba, int x, int y, int w, int h)
    {
        if (x < 0 || y < 0 || w < 1 || h < 1 || x + w > width || y + h > height)
            throw new ArgumentOutOfRangeException(nameof(x), $"{w}x{h} at {x},{y} is not inside {width}x{height}.");
        var cut = new byte[w * h * 4];
        for (var row = 0; row < h; row++)
            Array.Copy(rgba, ((y + row) * width + x) * 4, cut, row * w * 4, w * 4);
        return cut;
    }

    /// <summary>The PNG (zlib) CRC-32 of <paramref name="data"/>, continuing from <paramref name="crc"/>.</summary>
    internal static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
