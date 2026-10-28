using System.Buffers.Binary;

namespace RailNexus.Core.Texture;

/// <summary>
/// Pembaca dan penulis Truevision TGA. Ditulis sendiri karena TGA tidak memiliki tanda tangan:
/// pengenalan memakai pemeriksaan header yang ketat, bukan ekstensi berkas (PRD 13.3).
/// </summary>
public static class TgaCodec
{
    private const int HeaderSize = 18;

    /// <summary>Header TGA masuk akal dan ukuran berkas cukup untuk data yang dinyatakan.</summary>
    public static bool LooksLikeTga(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize) return false;
        int colorMapType = data[1], imageType = data[2], bpp = data[16];
        if (colorMapType > 1) return false;
        if (imageType is not (1 or 2 or 3 or 9 or 10 or 11)) return false;
        if ((imageType is 1 or 9) != (colorMapType == 1)) return false;
        int width = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]), height = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        if (width == 0 || height == 0) return false;
        if (bpp is not (8 or 15 or 16 or 24 or 32)) return false;
        if ((data[17] & 0xC0) != 0) return false; // bit 6–7 wajib nol (tanpa interleave)
        var cmBytes = colorMapType == 1 ? BinaryPrimitives.ReadUInt16LittleEndian(data[5..]) * ((data[7] + 7) / 8) : 0;
        long minimum = HeaderSize + data[0] + cmBytes;
        if (imageType is 1 or 2 or 3) minimum += (long)width * height * ((bpp + 7) / 8);
        else minimum += 1;
        return data.Length >= minimum;
    }

    public static DecodeResult Decode(byte[] data)
    {
        if (!LooksLikeTga(data)) return DecodeResult.Fail(ImageFormat.Tga, "Header TGA tidak valid atau berkas terpotong.");
        var s = data.AsSpan();
        int idLength = s[0], colorMapType = s[1], imageType = s[2], bpp = s[16], descriptor = s[17];
        int width = BinaryPrimitives.ReadUInt16LittleEndian(s[12..]), height = BinaryPrimitives.ReadUInt16LittleEndian(s[14..]);

        var gray = imageType is 3 or 11;
        if (imageType is 1 or 9)
            return DecodeResult.Fail(ImageFormat.Tga, "TGA berpalet belum didukung; simpan ulang sebagai TGA 24/32-bit atau PNG.");
        if (gray ? bpp != 8 : bpp is not (24 or 32))
            return DecodeResult.Fail(ImageFormat.Tga, $"TGA {bpp}-bit belum didukung; yang didukung 24/32-bit warna dan 8-bit abu-abu.");

        var bytesPerPixel = bpp / 8;
        var pos = HeaderSize + idLength + (colorMapType == 1 ? BinaryPrimitives.ReadUInt16LittleEndian(s[5..]) * ((s[7] + 7) / 8) : 0);
        var count = width * height;
        var src = new byte[count * bytesPerPixel];
        if (imageType is 9 or 10 or 11)
        {
            var o = 0;
            while (o < src.Length)
            {
                if (pos >= data.Length) return DecodeResult.Fail(ImageFormat.Tga, "Data RLE terpotong.");
                var packet = data[pos++];
                var n = (packet & 0x7F) + 1;
                if (o + n * bytesPerPixel > src.Length) return DecodeResult.Fail(ImageFormat.Tga, "Paket RLE melebihi ukuran gambar.");
                if ((packet & 0x80) != 0)
                {
                    if (pos + bytesPerPixel > data.Length) return DecodeResult.Fail(ImageFormat.Tga, "Data RLE terpotong.");
                    for (var i = 0; i < n; i++) { Buffer.BlockCopy(data, pos, src, o, bytesPerPixel); o += bytesPerPixel; }
                    pos += bytesPerPixel;
                }
                else
                {
                    var len = n * bytesPerPixel;
                    if (pos + len > data.Length) return DecodeResult.Fail(ImageFormat.Tga, "Data RLE terpotong.");
                    Buffer.BlockCopy(data, pos, src, o, len);
                    o += len; pos += len;
                }
            }
        }
        else
        {
            Buffer.BlockCopy(data, pos, src, 0, src.Length);
        }

        // Bit 5: baris pertama di atas; bit 4: piksel pertama di kanan.
        var topDown = (descriptor & 0x20) != 0;
        var rightToLeft = (descriptor & 0x10) != 0;
        var rgba = new byte[(long)count * 4];
        for (var row = 0; row < height; row++)
        {
            var y = topDown ? row : height - 1 - row;
            for (var col = 0; col < width; col++)
            {
                var x = rightToLeft ? width - 1 - col : col;
                var si = (row * width + col) * bytesPerPixel;
                var di = (y * width + x) * 4;
                if (gray)
                {
                    rgba[di] = rgba[di + 1] = rgba[di + 2] = src[si];
                    rgba[di + 3] = 255;
                }
                else
                {
                    rgba[di] = src[si + 2];
                    rgba[di + 1] = src[si + 1];
                    rgba[di + 2] = src[si];
                    rgba[di + 3] = bytesPerPixel == 4 ? src[si + 3] : (byte)255;
                }
            }
        }

        var notes = new List<string>();
        var alphaBits = descriptor & 0x0F;
        if (bpp == 32 && alphaBits == 0)
            notes.Add("Header TGA 32-bit menyatakan 0 bit alpha; kanal keempat tetap dibaca apa adanya.");
        var desc = $"TGA {(gray ? "abu-abu 8" : bpp.ToString(System.Globalization.CultureInfo.InvariantCulture))}-bit" +
                   $"{(imageType >= 9 ? " RLE" : "")}, asal {(topDown ? "kiri atas" : "kiri bawah")}{(rightToLeft ? " (kanan ke kiri)" : "")}";
        return new DecodeResult(true, ImageFormat.Tga, new RasterImage(width, height, rgba, bpp == 32), "", desc, notes);
    }

    /// <summary>
    /// Menulis TGA tak terkompresi dengan asal kiri bawah (bentuk paling umum dan paling kompatibel).
    /// 24-bit membuang kanal alpha; 32-bit menyimpannya dengan 8 bit alpha di descriptor.
    /// </summary>
    public static byte[] Encode(RasterImage image, bool withAlpha)
    {
        if (image.Width > ushort.MaxValue || image.Height > ushort.MaxValue)
            throw new ArgumentException("Ukuran melebihi batas TGA (65535).", nameof(image));
        var bytesPerPixel = withAlpha ? 4 : 3;
        var output = new byte[HeaderSize + (long)image.Width * image.Height * bytesPerPixel];
        output[2] = 2; // truecolor tak terkompresi
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(12), (ushort)image.Width);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(14), (ushort)image.Height);
        output[16] = (byte)(bytesPerPixel * 8);
        output[17] = withAlpha ? (byte)0x08 : (byte)0x00;

        var o = HeaderSize;
        for (var y = image.Height - 1; y >= 0; y--)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var i = (y * image.Width + x) * 4;
                output[o++] = image.Rgba[i + 2];
                output[o++] = image.Rgba[i + 1];
                output[o++] = image.Rgba[i];
                if (withAlpha) output[o++] = image.Rgba[i + 3];
            }
        }
        return output;
    }
}

/// <summary>Pengenalan format dari isi berkas lalu dekode bila didukung.</summary>
public static class ImageCodec
{
    public static ImageFormat Detect(ReadOnlySpan<byte> data)
    {
        if (PngDecoder.IsPng(data)) return ImageFormat.Png;
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return ImageFormat.Jpeg;
        if (data.Length >= 4 && data[0] == (byte)'D' && data[1] == (byte)'D' && data[2] == (byte)'S' && data[3] == (byte)' ') return ImageFormat.Dds;
        if (data.Length >= 14 && data[0] == (byte)'B' && data[1] == (byte)'M') return ImageFormat.Bmp;
        if (TgaCodec.LooksLikeTga(data)) return ImageFormat.Tga;
        return ImageFormat.Unknown;
    }

    public static DecodeResult Decode(byte[] data)
    {
        var format = Detect(data);
        return format switch
        {
            ImageFormat.Png => PngDecoder.Decode(data),
            ImageFormat.Tga => TgaCodec.Decode(data),
            ImageFormat.Unknown => DecodeResult.Fail(format, "Format gambar tidak dikenali dari isi berkas."),
            _ => DecodeResult.Fail(format, $"Format {format.ToString().ToUpperInvariant()} dikenali tetapi belum dapat dibaca. Simpan ulang sebagai PNG atau TGA."),
        };
    }
}
