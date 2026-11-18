using System.IO.Compression;

namespace TerrainTool.Core.Models;

/// <summary>Minimal PNG writer (8-bit RGBA or RGB, no interlace), for textures handed to Blender.</summary>
public static class PngWriter
{
    private static readonly uint[] Crc = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    /// <summary>Writes a PNG from BGRA pixels (as DdsImage gives them); alpha is dropped when not needed.</summary>
    public static void WriteBgra(string path, int w, int h, byte[] bgra, bool alpha)
    {
        int ch = alpha ? 4 : 3;
        var raw = new byte[(w * ch + 1) * h];
        int o = 0;
        for (int y = 0; y < h; y++)
        {
            raw[o++] = 0; // filter none
            int s = y * w * 4;
            for (int x = 0; x < w; x++, s += 4)
            {
                raw[o++] = bgra[s + 2]; raw[o++] = bgra[s + 1]; raw[o++] = bgra[s];
                if (alpha) raw[o++] = bgra[s + 3];
            }
        }
        using var fs = File.Create(path);
        fs.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        Be(ihdr, 0, (uint)w); Be(ihdr, 4, (uint)h);
        ihdr[8] = 8; ihdr[9] = (byte)(alpha ? 6 : 2);
        Chunk(fs, "IHDR", ihdr);
        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.Fastest, true)) z.Write(raw);
            Chunk(fs, "IDAT", ms.ToArray());
        }
        Chunk(fs, "IEND", Array.Empty<byte>());
    }

    private static void Be(byte[] b, int at, uint v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; Be(len, 0, (uint)data.Length); s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t); s.Write(data);
        uint c = 0xFFFFFFFF;
        foreach (var x in t) c = Crc[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (var x in data) c = Crc[(c ^ x) & 0xFF] ^ (c >> 8);
        var cb = new byte[4]; Be(cb, 0, c ^ 0xFFFFFFFF); s.Write(cb);
    }
}
