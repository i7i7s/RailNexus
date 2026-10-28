using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace RailNexus.Tests;

/// <summary>
/// Fixture piksel buatan sendiri (PRD 27: "fixture piksel yang diketahui"). Penulis PNG/TGA di sini
/// sengaja terpisah dari kode produksi agar uji tidak membuktikan dirinya sendiri.
/// </summary>
public static class ImageFixtures
{
    /// <summary>
    /// Pola 4×3 asimetris: setiap piksel unik sehingga kanal tertukar atau gambar terbalik
    /// (atas/bawah, kiri/kanan) langsung terdeteksi.
    /// </summary>
    public static (byte R, byte G, byte B, byte A) Pattern(int x, int y) =>
        ((byte)(10 + x * 60), (byte)(20 + y * 70), (byte)(200 - x * 10 - y * 30), (byte)(255 - (x + y * 4) * 17));

    public const int W = 4, H = 3;

    /// <summary>PNG 8-bit. colorType 6 = RGBA, 2 = RGB, 0 = abu-abu, 4 = abu-abu+alpha.</summary>
    public static byte[] Png(int w, int h, byte colorType, Func<int, int, int[]> samples, byte bitDepth = 8,
        int filter = 0, bool interlace = false, byte[]? plte = null, byte[]? trns = null, bool gama = false)
    {
        var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
        var bpp = channels * bitDepth;
        var raw = new MemoryStream();
        if (!interlace)
        {
            WritePass(raw, w, h, bpp, bitDepth, filter, (x, y) => samples(x, y));
        }
        else
        {
            (int sx, int sy, int dx, int dy)[] adam7 = [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];
            foreach (var (sx, sy, dx, dy) in adam7)
            {
                int pw = (w - sx + dx - 1) / dx, ph = (h - sy + dy - 1) / dy;
                if (pw <= 0 || ph <= 0) continue;
                WritePass(raw, pw, ph, bpp, bitDepth, filter, (x, y) => samples(sx + x * dx, sy + y * dy));
            }
        }

        var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw.ToArray());

        var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)w);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)h);
        ihdr[8] = bitDepth; ihdr[9] = colorType; ihdr[12] = (byte)(interlace ? 1 : 0);
        Chunk(png, "IHDR", ihdr);
        if (gama) Chunk(png, "gAMA", [0x00, 0x00, 0xB1, 0x8F]); // 1/2.2 — dekoder tidak boleh mengoreksi warna
        if (plte is not null) Chunk(png, "PLTE", plte);
        if (trns is not null) Chunk(png, "tRNS", trns);
        // IDAT dipecah dua untuk menguji penggabungan chunk.
        var data = compressed.ToArray();
        var half = data.Length / 2;
        Chunk(png, "IDAT", data[..half]);
        Chunk(png, "IDAT", data[half..]);
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    public static byte[] PatternPngRgba(bool gama = false, int filter = 0) =>
        Png(W, H, 6, (x, y) => { var p = Pattern(x, y); return [p.R, p.G, p.B, p.A]; }, filter: filter, gama: gama);

    public static byte[] PatternPngRgb(int filter = 0) =>
        Png(W, H, 2, (x, y) => { var p = Pattern(x, y); return [p.R, p.G, p.B]; }, filter: filter);

    private static void WritePass(Stream raw, int w, int h, int bpp, int bitDepth, int filter, Func<int, int, int[]> samples)
    {
        var stride = (w * bpp + 7) / 8;
        var step = Math.Max(1, bpp / 8);
        var prev = new byte[stride];
        for (var y = 0; y < h; y++)
        {
            var line = new byte[stride];
            var bit = 0;
            for (var x = 0; x < w; x++)
            {
                foreach (var v in samples(x, y))
                {
                    if (bitDepth == 16) { line[bit / 8] = (byte)(v >> 8); line[bit / 8 + 1] = (byte)v; }
                    else if (bitDepth == 8) line[bit / 8] = (byte)v;
                    else line[bit / 8] |= (byte)(v << (8 - bitDepth - bit % 8));
                    bit += bitDepth;
                }
            }
            raw.WriteByte((byte)filter);
            for (var i = 0; i < stride; i++)
            {
                int a = i >= step ? line[i - step] : 0, b = prev[i], c = i >= step ? prev[i - step] : 0;
                var pred = filter switch
                {
                    1 => a,
                    2 => b,
                    3 => (a + b) >> 1,
                    4 => Paeth(a, b, c),
                    _ => 0,
                };
                raw.WriteByte((byte)(line[i] - pred));
            }
            prev = line;
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void Chunk(Stream s, string type, byte[] body)
    {
        var len = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)body.Length);
        s.Write(len);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(body);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(typeBytes.Concat(body).ToArray()));
        s.Write(crc);
    }

    private static uint Crc(byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            c ^= b;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xFFFFFFFFu;
    }

    /// <summary>TGA pola uji dengan asal dan kompresi pilihan, ditulis byte demi byte.</summary>
    public static byte[] PatternTga(bool topLeft, bool rle = false, bool alpha = true)
    {
        var bpp = alpha ? 4 : 3;
        var ms = new MemoryStream();
        var header = new byte[18];
        header[2] = (byte)(rle ? 10 : 2);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), W);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), H);
        header[16] = (byte)(bpp * 8);
        header[17] = (byte)((alpha ? 8 : 0) | (topLeft ? 0x20 : 0));
        ms.Write(header);
        for (var row = 0; row < H; row++)
        {
            var y = topLeft ? row : H - 1 - row;
            if (rle)
            {
                // Paket raw untuk dua piksel pertama, lalu paket ulang (run) dua piksel dari piksel ketiga.
                ms.WriteByte(0x01);
                for (var x = 0; x < 2; x++) Pixel(ms, x, y, alpha);
                ms.WriteByte(0x81);
                Pixel(ms, 2, y, alpha);
                continue;
            }
            for (var x = 0; x < W; x++) Pixel(ms, x, y, alpha);
        }
        return ms.ToArray();
    }

    /// <summary>Piksel yang diharapkan dari <see cref="PatternTga"/> dengan rle=true (kolom 3 = kolom 2).</summary>
    public static (byte R, byte G, byte B, byte A) RlePattern(int x, int y) => Pattern(Math.Min(x, 2), y);

    private static void Pixel(Stream s, int x, int y, bool alpha)
    {
        var p = Pattern(x, y);
        s.WriteByte(p.B); s.WriteByte(p.G); s.WriteByte(p.R);
        if (alpha) s.WriteByte(p.A);
    }
}
