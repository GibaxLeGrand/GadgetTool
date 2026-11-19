using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace GadgetTool.Extractor.Tests;

public class PngWriterTests
{
    /// <summary>The chunks of a PNG file, each checked against its CRC.</summary>
    private static List<(string Type, byte[] Data)> Chunks(byte[] png)
    {
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);
        var chunks = new List<(string, byte[])>();
        for (var p = 8; p < png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(p));
            var typeAndData = png.AsSpan(p + 4, 4 + length);
            var crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(p + 8 + length));
            Assert.Equal(crc, PngWriter.Crc(0xFFFFFFFFu, typeAndData) ^ 0xFFFFFFFFu);
            chunks.Add((Encoding.ASCII.GetString(typeAndData[..4]), typeAndData[4..].ToArray()));
            p += 12 + length;
        }
        return chunks;
    }

    [Fact]
    public void Writes_a_valid_rgba_png_with_the_same_pixels()
    {
        // 3x2 pixels, each a different colour with some transparency.
        byte[] rgba = [.. Enumerable.Range(0, 3 * 2 * 4).Select(i => (byte)(i * 10))];

        var chunks = Chunks(PngWriter.Encode(3, 2, rgba));

        Assert.Equal(["IHDR", "IDAT", "IEND"], chunks.Select(c => c.Type));
        var header = chunks[0].Data;
        Assert.Equal(3, BinaryPrimitives.ReadInt32BigEndian(header));
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4)));
        Assert.Equal([8, 6, 0, 0, 0], header[8..]); // 8 bits, RGBA, no interlace

        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(new MemoryStream(chunks[1].Data), CompressionMode.Decompress))
            zlib.CopyTo(inflated);
        var rows = inflated.ToArray();
        Assert.Equal(2 * (1 + 3 * 4), rows.Length);
        Assert.Equal(0, rows[0]);               // filter byte, row 1
        Assert.Equal(rgba[..12], rows[1..13]);
        Assert.Equal(0, rows[13]);              // filter byte, row 2
        Assert.Equal(rgba[12..], rows[14..]);
    }

    [Fact]
    public void Matches_the_known_crc_of_iend() =>
        // Every PNG ends with the same IEND chunk: AE 42 60 82.
        Assert.Equal(0xAE426082u, PngWriter.Crc(0xFFFFFFFFu, "IEND"u8) ^ 0xFFFFFFFFu);

    [Fact]
    public void Refuses_pixels_of_the_wrong_size()
    {
        Assert.Throws<ArgumentException>(() => PngWriter.Encode(2, 2, new byte[15]));
        Assert.Throws<ArgumentOutOfRangeException>(() => PngWriter.Encode(0, 2, []));
    }
}
