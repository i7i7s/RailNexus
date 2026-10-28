using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using RailNexus.Core;

namespace RailNexus.Trainz;

/// <summary>
/// Membuat aset uji yang dapat dibuang untuk siklus Tahap 0.
///
/// Riwayat bentuk aset (bukti fixtures/trainzutil/trs19/validate-after-commit.json):
/// versi pertama ditandai faulty oleh TRS19 karena VE45 (tag tak dikenal dan baris komentar
/// tidak diizinkan di aset bertipe texture), VE60/VE26/VE28 (tag 'texture' wajib berisi berkas
/// gambar), dan VE62 (container 'thumbnails' wajib). Bentuk di bawah mengikuti pesan tersebut;
/// hasil validasinya tetap diperiksa, tidak diasumsikan lolos.
/// </summary>
public static class TestAssetFactory
{
    public const string Marker = "RailNexus-uji-Tahap0";
    public const string ThumbnailName = "thumbnail.png";

    public static string Create(string parent, Kuid kuid, string trainzBuild = "4.6")
    {
        var folder = Path.Combine(parent, $"railnexus-uji-{kuid.UserId}-{kuid.ContentId}");
        if (Directory.Exists(folder))
            throw new IOException($"Folder sudah ada: {folder}. Hapus atau pilih KUID lain.");
        Directory.CreateDirectory(folder);
        foreach (var (name, bytes) in Files(kuid, trainzBuild))
            File.WriteAllBytes(Path.Combine(folder, name), bytes);
        return folder;
    }

    /// <summary>Isi lengkap aset uji. Dipakai juga untuk memperbaiki aset uji versi lama lewat transaksi.</summary>
    public static IReadOnlyDictionary<string, byte[]> Files(Kuid kuid, string trainzBuild = "4.6")
    {
        var config = string.Join("\r\n",
            $"kuid                                    {kuid}",
            "username                                \"RailNexus aset uji\"",
            "kind                                    \"texture\"",
            $"trainz-build                            {trainzBuild}",
            "category-class                          \"YT\"",
            // VE176 (bukti cycle 2026-10-28): tag ini menunjuk berkas .texture, yang dibangun Trainz
            // dari pasangan rn_uji.texture.txt + rn_uji.tga.
            "texture                                 \"rn_uji.texture\"",
            $"description                             \"{Marker} - boleh dihapus.\"",
            "",
            "thumbnails",
            "{",
            "  0",
            "  {",
            $"    image                               \"{ThumbnailName}\"",
            "    width                               240",
            "    height                              180",
            "  }",
            "}",
            "");
        return new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["config.txt"] = new UTF8Encoding(false).GetBytes(config),
            ["rn_uji.tga"] = Tga24(32, 32),
            ["rn_uji.texture.txt"] = Encoding.ASCII.GetBytes("Primary=rn_uji.tga\r\nTile=st\r\n"),
            [ThumbnailName] = Png24(240, 180),
        };
    }

    /// <summary>TGA 24-bit tanpa kompresi, pola kotak-kotak dua warna.</summary>
    public static byte[] Tga24(int width, int height)
    {
        var data = new byte[18 + width * height * 3];
        data[2] = 2; // truecolor tanpa kompresi
        BitConverter.TryWriteBytes(data.AsSpan(12, 2), (ushort)width);
        BitConverter.TryWriteBytes(data.AsSpan(14, 2), (ushort)height);
        data[16] = 24;
        var i = 18;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var on = ((x / 8) + (y / 8)) % 2 == 0;
            data[i++] = on ? (byte)0xB8 : (byte)0xEE; // B
            data[i++] = on ? (byte)0x56 : (byte)0xF0; // G
            data[i++] = on ? (byte)0x1E : (byte)0xF3; // R
        }
        return data;
    }

    /// <summary>PNG RGB 8-bit sederhana untuk thumbnail.</summary>
    public static byte[] Png24(int width, int height)
    {
        var raw = new byte[(width * 3 + 1) * height];
        var i = 0;
        for (var y = 0; y < height; y++)
        {
            raw[i++] = 0; // filter: none
            for (var x = 0; x < width; x++)
            {
                var on = ((x / 20) + (y / 20)) % 2 == 0;
                raw[i++] = on ? (byte)0x1E : (byte)0xF3;
                raw[i++] = on ? (byte)0x56 : (byte)0xF0;
                raw[i++] = on ? (byte)0xB8 : (byte)0xEE;
            }
        }
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw);

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 2;  // RGB
        WriteChunk(png, "IHDR", ihdr);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = Crc32(typeBytes.Concat(data).ToArray());
        Span<byte> c = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(c, crc);
        s.Write(c);
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    internal static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in bytes) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    public static string DescriptionFor(DateTimeOffset at) =>
        string.Create(CultureInfo.InvariantCulture, $"{Marker} - diubah {at:yyyy-MM-dd HH:mm:ss}Z");
}
