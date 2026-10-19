using System.Buffers.Binary;
using System.IO.Compression;

namespace RTS.SoftRender;

/// <summary>Dependency-free PNG encoder (8-bit RGBA, filter 0, zlib via ZLibStream).</summary>
public static class PngWriter
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    /// <summary>
    /// Writes a PNG. Pixels are packed as R | G&lt;&lt;8 | B&lt;&lt;16 | A&lt;&lt;24 (i.e. bytes R,G,B,A in memory on little-endian).
    /// </summary>
    public static void Write(string path, int width, int height, uint[] rgba)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var fs = File.Create(path);
        Write(fs, width, height, rgba);
    }

    public static void Write(Stream output, int width, int height, uint[] rgba)
    {
        if (rgba.Length < width * height) throw new ArgumentException("Pixel buffer too small.", nameof(rgba));
        ReadOnlySpan<byte> sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        output.Write(sig);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // color type RGBA
        ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk(output, "IHDR"u8, ihdr);

        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            {
                var row = new byte[1 + width * 4];
                for (int y = 0; y < height; y++)
                {
                    row[0] = 0; // filter: none
                    for (int x = 0; x < width; x++)
                    {
                        uint p = rgba[y * width + x];
                        int o = 1 + x * 4;
                        row[o] = (byte)p;
                        row[o + 1] = (byte)(p >> 8);
                        row[o + 2] = (byte)(p >> 16);
                        row[o + 3] = (byte)(p >> 24);
                    }
                    z.Write(row);
                }
            }
            WriteChunk(output, "IDAT"u8, ms.GetBuffer().AsSpan(0, (int)ms.Length));
        }
        WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
    }

    private static void WriteChunk(Stream s, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buf, data.Length);
        s.Write(buf);
        s.Write(type);
        s.Write(data);
        uint crc = Crc(0xFFFFFFFFu, type);
        crc = Crc(crc, data) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(buf, crc);
        s.Write(buf);
    }
}
