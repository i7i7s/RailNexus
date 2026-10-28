using System.Buffers.Binary;
using System.IO.Compression;

namespace RailNexus.Core.Texture;

/// <summary>
/// Dekoder PNG mandiri (spesifikasi PNG/ISO 15948) memakai <see cref="ZLibStream"/> bawaan .NET.
/// Tidak ada koreksi warna: chunk gAMA/iCCP/sRGB diabaikan sehingga nilai kanal disalin apa adanya
/// (PRD 13.3 — penting untuk normal map dan parameter map).
/// </summary>
public static class PngDecoder
{
    public static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Batas sisi dan jumlah piksel agar berkas rusak tidak menghabiskan memori.</summary>
    public const int MaxSide = 16384;
    public const long MaxPixels = 64L * 1024 * 1024;

    public static bool IsPng(ReadOnlySpan<byte> data) => data.Length >= 8 && data[..8].SequenceEqual(Signature);

    public static DecodeResult Decode(byte[] data)
    {
        try { return DecodeCore(data); }
        catch (InvalidDataException ex) { return DecodeResult.Fail(ImageFormat.Png, $"PNG rusak: {ex.Message}"); }
    }

    private static DecodeResult DecodeCore(byte[] data)
    {
        if (!IsPng(data)) return DecodeResult.Fail(ImageFormat.Png, "Tanda tangan PNG tidak ditemukan.");

        int width = 0, height = 0, bitDepth = 0, colorType = -1, interlace = 0;
        byte[]? palette = null;
        byte[]? paletteAlpha = null;
        ushort[]? colorKey = null;
        var idat = new MemoryStream();
        var seenIhdr = false;
        var seenEnd = false;

        var pos = 8;
        while (pos < data.Length)
        {
            if (pos + 12 > data.Length) throw new InvalidDataException("chunk terpotong.");
            var length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos));
            if (length > int.MaxValue || pos + 12 + (long)length > data.Length) throw new InvalidDataException("panjang chunk melebihi berkas.");
            var type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
            var body = data.AsSpan(pos + 8, (int)length);
            var crc = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 8 + (int)length));
            if (Crc32.Compute(data.AsSpan(pos + 4, 4 + (int)length)) != crc)
                throw new InvalidDataException($"CRC chunk {type} tidak cocok.");
            pos += 12 + (int)length;

            switch (type)
            {
                case "IHDR":
                    if (body.Length != 13) throw new InvalidDataException("IHDR tidak valid.");
                    width = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(body), int.MaxValue);
                    height = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(body[4..]), int.MaxValue);
                    bitDepth = body[8];
                    colorType = body[9];
                    if (body[10] != 0 || body[11] != 0) throw new InvalidDataException("metode kompresi/filter tidak dikenal.");
                    interlace = body[12];
                    seenIhdr = true;
                    break;
                case "PLTE":
                    if (body.Length % 3 != 0 || body.Length == 0) throw new InvalidDataException("PLTE tidak valid.");
                    palette = body.ToArray();
                    break;
                case "tRNS":
                    if (colorType == 3) paletteAlpha = body.ToArray();
                    else if (colorType == 0 && body.Length >= 2) colorKey = [BinaryPrimitives.ReadUInt16BigEndian(body)];
                    else if (colorType == 2 && body.Length >= 6)
                        colorKey = [BinaryPrimitives.ReadUInt16BigEndian(body), BinaryPrimitives.ReadUInt16BigEndian(body[2..]), BinaryPrimitives.ReadUInt16BigEndian(body[4..])];
                    break;
                case "IDAT":
                    idat.Write(body);
                    break;
                case "IEND":
                    seenEnd = true;
                    pos = data.Length;
                    break;
                default:
                    // Chunk kritis (huruf pertama kapital) yang tidak dikenal tidak boleh dilewati.
                    if (char.IsUpper(type[0])) throw new InvalidDataException($"chunk kritis '{type}' tidak didukung.");
                    break;
            }
        }

        if (!seenIhdr) throw new InvalidDataException("IHDR tidak ada.");
        if (!seenEnd) throw new InvalidDataException("IEND tidak ada; berkas mungkin terpotong.");
        if (width <= 0 || height <= 0 || width > MaxSide || height > MaxSide || (long)width * height > MaxPixels)
            return DecodeResult.Fail(ImageFormat.Png, $"Ukuran {width} × {height} di luar batas yang didukung ({MaxSide} per sisi).");

        var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
        var depthOk = colorType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            2 or 4 or 6 => bitDepth is 8 or 16,
            _ => false,
        };
        if (channels == 0 || !depthOk)
            return DecodeResult.Fail(ImageFormat.Png, $"Kombinasi tipe warna {colorType} dan kedalaman {bitDepth} bit tidak valid.");
        if (colorType == 3 && palette is null) throw new InvalidDataException("PLTE wajib untuk gambar berpalet.");
        if (interlace > 1) throw new InvalidDataException("metode interlace tidak dikenal.");

        var bitsPerPixel = channels * bitDepth;
        var raw = Inflate(idat.ToArray(), ExpectedLength(width, height, bitsPerPixel, interlace == 1));

        var rgba = new byte[(long)width * height * 4];
        var ctx = new PixelContext(colorType, bitDepth, palette, paletteAlpha, colorKey);
        if (interlace == 0)
        {
            Unfilter(raw, 0, width, height, bitsPerPixel, (x, y, line, bit) => ctx.Write(rgba, width, x, y, line, bit));
        }
        else
        {
            var offset = 0;
            foreach (var (sx, sy, dx, dy) in Adam7)
            {
                var pw = (width - sx + dx - 1) / dx;
                var ph = (height - sy + dy - 1) / dy;
                if (pw <= 0 || ph <= 0) continue;
                offset = Unfilter(raw, offset, pw, ph, bitsPerPixel,
                    (x, y, line, bit) => ctx.Write(rgba, width, sx + x * dx, sy + y * dy, line, bit));
            }
        }

        var hasAlpha = colorType is 4 or 6 || paletteAlpha is not null || colorKey is not null;
        var notes = new List<string>();
        if (bitDepth == 16) notes.Add("Sumber 16-bit per kanal; nilai diturunkan ke 8-bit (byte atas) karena TGA hanya 8-bit per kanal.");
        if (interlace == 1) notes.Add("Sumber memakai interlace Adam7.");
        var kind = colorType switch { 0 => "abu-abu", 2 => "RGB", 3 => "berpalet", 4 => "abu-abu + alpha", _ => "RGBA" };
        return new DecodeResult(true, ImageFormat.Png, new RasterImage(width, height, rgba, hasAlpha), "",
            $"PNG {kind} {bitDepth}-bit{(colorKey is not null || paletteAlpha is not null ? " (transparansi tRNS)" : "")}", notes);
    }

    private static readonly (int Sx, int Sy, int Dx, int Dy)[] Adam7 =
        [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];

    private static long ExpectedLength(int width, int height, int bpp, bool interlaced)
    {
        static long Size(int w, int h, int bpp) => w <= 0 || h <= 0 ? 0 : h * (1 + ((long)w * bpp + 7) / 8);
        if (!interlaced) return Size(width, height, bpp);
        long total = 0;
        foreach (var (sx, sy, dx, dy) in Adam7)
            total += Size((width - sx + dx - 1) / dx, (height - sy + dy - 1) / dy, bpp);
        return total;
    }

    private static byte[] Inflate(byte[] compressed, long expected)
    {
        using var z = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress);
        var output = new byte[expected];
        var read = 0;
        while (read < output.Length)
        {
            var n = z.Read(output, read, output.Length - read);
            if (n == 0) throw new InvalidDataException("data gambar terpotong.");
            read += n;
        }
        return output;
    }

    /// <summary>Membalik filter per baris lalu memanggil <paramref name="emit"/> untuk setiap piksel.</summary>
    private static int Unfilter(byte[] raw, int offset, int width, int height, int bpp, Action<int, int, byte[], int> emit)
    {
        var stride = (int)(((long)width * bpp + 7) / 8);
        var step = Math.Max(1, bpp / 8);
        var prev = new byte[stride];
        var line = new byte[stride];
        for (var y = 0; y < height; y++)
        {
            var filter = raw[offset++];
            Buffer.BlockCopy(raw, offset, line, 0, stride);
            offset += stride;
            for (var i = 0; i < stride; i++)
            {
                int a = i >= step ? line[i - step] : 0, b = prev[i], c = i >= step ? prev[i - step] : 0;
                line[i] = filter switch
                {
                    0 => line[i],
                    1 => (byte)(line[i] + a),
                    2 => (byte)(line[i] + b),
                    3 => (byte)(line[i] + ((a + b) >> 1)),
                    4 => (byte)(line[i] + Paeth(a, b, c)),
                    _ => throw new InvalidDataException($"filter baris {filter} tidak dikenal."),
                };
            }
            for (var x = 0; x < width; x++) emit(x, y, line, x * bpp);
            (prev, line) = (line, prev);
        }
        return offset;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private sealed class PixelContext(int colorType, int bitDepth, byte[]? palette, byte[]? paletteAlpha, ushort[]? colorKey)
    {
        private int Sample(byte[] line, int bit, int index)
        {
            var b = bit + index * bitDepth;
            return bitDepth switch
            {
                16 => (line[b / 8] << 8) | line[b / 8 + 1],
                8 => line[b / 8],
                _ => (line[b / 8] >> (8 - bitDepth - b % 8)) & ((1 << bitDepth) - 1),
            };
        }

        /// <summary>Nilai sampel diubah ke 8-bit: 16-bit memakai byte atas, kedalaman rendah diskalakan penuh.</summary>
        private byte To8(int v) => bitDepth switch
        {
            16 => (byte)(v >> 8),
            8 => (byte)v,
            _ => (byte)(v * 255 / ((1 << bitDepth) - 1)),
        };

        public void Write(byte[] rgba, int width, int x, int y, byte[] line, int bit)
        {
            var o = ((long)y * width + x) * 4;
            switch (colorType)
            {
                case 0:
                {
                    var g = Sample(line, bit, 0);
                    rgba[o] = rgba[o + 1] = rgba[o + 2] = To8(g);
                    rgba[o + 3] = colorKey is { } k && k[0] == g ? (byte)0 : (byte)255;
                    break;
                }
                case 2:
                {
                    int r = Sample(line, bit, 0), g = Sample(line, bit, 1), b = Sample(line, bit, 2);
                    rgba[o] = To8(r); rgba[o + 1] = To8(g); rgba[o + 2] = To8(b);
                    rgba[o + 3] = colorKey is { } k && k[0] == r && k[1] == g && k[2] == b ? (byte)0 : (byte)255;
                    break;
                }
                case 3:
                {
                    var i = Sample(line, bit, 0);
                    if (i * 3 + 2 >= palette!.Length) throw new InvalidDataException("indeks palet di luar PLTE.");
                    rgba[o] = palette[i * 3]; rgba[o + 1] = palette[i * 3 + 1]; rgba[o + 2] = palette[i * 3 + 2];
                    rgba[o + 3] = paletteAlpha is not null && i < paletteAlpha.Length ? paletteAlpha[i] : (byte)255;
                    break;
                }
                case 4:
                    rgba[o] = rgba[o + 1] = rgba[o + 2] = To8(Sample(line, bit, 0));
                    rgba[o + 3] = To8(Sample(line, bit, 1));
                    break;
                default:
                    rgba[o] = To8(Sample(line, bit, 0)); rgba[o + 1] = To8(Sample(line, bit, 1));
                    rgba[o + 2] = To8(Sample(line, bit, 2)); rgba[o + 3] = To8(Sample(line, bit, 3));
                    break;
            }
        }
    }
}

/// <summary>CRC-32 (polinomial 0xEDB88320) untuk chunk PNG.</summary>
internal static class Crc32
{
    private static readonly uint[] Table = Build();

    private static uint[] Build()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
