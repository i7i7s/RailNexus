namespace RailNexus.Core.Texture;

/// <summary>Format yang dikenali dari isi berkas, bukan dari ekstensi (PRD 13.3).</summary>
public enum ImageFormat
{
    Unknown,
    Png,
    Tga,
    /// <summary>Dikenali dari tanda tangan berkas tetapi belum dapat dibaca RailNexus.</summary>
    Jpeg,
    Bmp,
    Dds,
}

/// <summary>
/// Gambar 8-bit per kanal di memori: RGBA, baris pertama = baris paling atas.
/// Semua dekoder menormalkan ke bentuk ini sehingga orientasi dan urutan kanal
/// dapat diperiksa dengan cara yang sama untuk setiap format.
/// </summary>
public sealed class RasterImage
{
    public int Width { get; }
    public int Height { get; }
    /// <summary>Piksel RGBA, panjang Width × Height × 4.</summary>
    public byte[] Rgba { get; }
    /// <summary>Sumber memiliki kanal alpha (termasuk transparansi lewat tRNS).</summary>
    public bool HasAlpha { get; }

    public RasterImage(int width, int height, byte[] rgba, bool hasAlpha)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Ukuran gambar harus positif.");
        if (rgba.Length != (long)width * height * 4) throw new ArgumentException("Panjang data piksel tidak sesuai ukuran.", nameof(rgba));
        Width = width;
        Height = height;
        Rgba = rgba;
        HasAlpha = hasAlpha;
    }

    public (byte R, byte G, byte B, byte A) Pixel(int x, int y)
    {
        var i = (y * Width + x) * 4;
        return (Rgba[i], Rgba[i + 1], Rgba[i + 2], Rgba[i + 3]);
    }

    /// <summary>true bila semua alpha bernilai 255 (alpha tidak membawa informasi).</summary>
    public bool AlphaIsOpaque()
    {
        for (var i = 3; i < Rgba.Length; i += 4)
            if (Rgba[i] != 255) return false;
        return true;
    }
}

/// <summary>Hasil dekode: gambar beserta catatan yang perlu ditampilkan (mis. presisi diturunkan).</summary>
public sealed record DecodeResult(
    bool Ok,
    ImageFormat Format,
    RasterImage? Image,
    string Error,
    /// <summary>Keterangan teknis sumber, mis. "PNG RGBA 8-bit".</summary>
    string Description,
    IReadOnlyList<string> Notes)
{
    public static DecodeResult Fail(ImageFormat format, string error) => new(false, format, null, error, "", []);
}
