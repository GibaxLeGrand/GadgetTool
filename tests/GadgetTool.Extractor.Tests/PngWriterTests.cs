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
    public void Crops_a_rectangle_and_refuses_one_outside_the_picture()
    {
        // 3x2; each pixel's red is its index.
        var rgba = new byte[3 * 2 * 4];
        for (var i = 0; i < 6; i++)
            rgba[i * 4] = (byte)i;

        var cut = PngWriter.Crop(3, 2, rgba, 1, 0, 2, 2);

        Assert.Equal(new byte[] { 1, 2, 4, 5 }, Enumerable.Range(0, 4).Select(i => cut[i * 4]).ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => PngWriter.Crop(3, 2, rgba, 2, 0, 2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PngWriter.Crop(3, 2, rgba, -1, 0, 1, 1));
    }

    [Fact]
    public void Trims_the_transparent_margins()
    {
        // 4x3, one opaque pixel at (1,1) and one half-transparent at (2,2); alpha 0 elsewhere.
        var rgba = new byte[4 * 3 * 4];
        rgba[(1 * 4 + 1) * 4 + 3] = 255;
        rgba[(2 * 4 + 2) * 4 + 3] = 100;
        rgba[(2 * 4 + 2) * 4] = 7;

        var (w, h, cut) = PngWriter.TrimTransparent(4, 3, rgba);

        Assert.Equal((2, 2), (w, h));
        Assert.Equal(255, cut[3]);          // (1,1) is now (0,0)
        Assert.Equal(7, cut[(1 * 2 + 1) * 4]); // (2,2) is now (1,1)

        var empty = new byte[2 * 2 * 4];
        Assert.Equal((2, 2), (PngWriter.TrimTransparent(2, 2, empty).Width, PngWriter.TrimTransparent(2, 2, empty).Height));
    }

    [Fact]
    public void Centres_what_is_drawn_without_resizing_it()
    {
        // 8x6, a 2x2 opaque square at x 0-1, y 3-4: its centre should end up at the middle.
        var rgba = new byte[8 * 6 * 4];
        foreach (var (x, y) in new[] { (0, 3), (1, 3), (0, 4), (1, 4) })
        {
            rgba[(y * 8 + x) * 4] = 9;
            rgba[(y * 8 + x) * 4 + 3] = 255;
        }

        var moved = PngWriter.CenterDrawing(8, 6, rgba);

        var opaque = Enumerable.Range(0, 8 * 6).Where(i => moved[i * 4 + 3] == 255).Select(i => (i % 8, i / 8)).ToList();
        Assert.Equal([(3, 2), (4, 2), (3, 3), (4, 3)], opaque);
        Assert.Equal(9, moved[(2 * 8 + 3) * 4]);

        var empty = new byte[8 * 6 * 4];
        Assert.Same(empty, PngWriter.CenterDrawing(8, 6, empty)); // nothing drawn: nothing to move
    }

    [Fact]
    public void A_drawing_already_in_the_middle_stays()
    {
        var rgba = new byte[4 * 4 * 4];
        foreach (var (x, y) in new[] { (1, 1), (2, 1), (1, 2), (2, 2) })
            rgba[(y * 4 + x) * 4 + 3] = 255;

        Assert.Same(rgba, PngWriter.CenterDrawing(4, 4, rgba));
    }

    [Fact]
    public void Refuses_pixels_of_the_wrong_size()
    {
        Assert.Throws<ArgumentException>(() => PngWriter.Encode(2, 2, new byte[15]));
        Assert.Throws<ArgumentOutOfRangeException>(() => PngWriter.Encode(0, 2, []));
    }
}
