namespace RailNexus.Core.Texture;

public enum SourceChannel { R, G, B, A, Luminance }

/// <summary>Sumber satu kanal output: kanal dari gambar, atau nilai konstan pilihan pengguna (PRD 13.6).</summary>
public sealed record ChannelInput(string? ImagePath, SourceChannel Channel = SourceChannel.R, byte Constant = 0)
{
    public bool IsConstant => ImagePath is null;
    public static ChannelInput Const(byte value) => new(null, SourceChannel.R, value);
}

public sealed record PackResult(bool Ok, string Error, RasterImage? Image, IReadOnlyList<string> ChannelReport, IReadOnlyList<string> Notes);

/// <summary>
/// Perakit kanal (RN-014): setiap kanal output R/G/B/A diisi secara eksplisit. Tidak ada koreksi warna;
/// kanal disalin apa adanya. Luminance hanya dihitung bila dipilih pengguna dan dicatat. Arti kanal
/// untuk material tertentu (mis. m.pbrmetal) ditentukan pengguna, bukan ditebak aplikasi.
/// </summary>
public static class ChannelPacker
{
    private static readonly string[] Names = ["R", "G", "B", "A"];

    public static PackResult Pack(ChannelInput r, ChannelInput g, ChannelInput b, ChannelInput a)
    {
        var inputs = new[] { r, g, b, a };
        var images = new Dictionary<string, RasterImage>(StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>();
        foreach (var path in inputs.Where(i => !i.IsConstant).Select(i => i.ImagePath!).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Fail($"{Path.GetFileName(path)} tidak dapat dibaca: {ex.Message}");
            }
            var d = ImageCodec.Decode(bytes);
            if (!d.Ok) return Fail($"{Path.GetFileName(path)}: {d.Error}");
            images[path] = d.Image!;
            notes.AddRange(d.Notes.Select(n => $"{Path.GetFileName(path)}: {n}"));
        }
        if (images.Count == 0) return Fail("Minimal satu kanal harus berasal dari gambar agar ukuran output diketahui.");
        var sizes = images.Values.Select(i => (i.Width, i.Height)).Distinct().ToList();
        if (sizes.Count > 1)
            return Fail("Ukuran gambar sumber berbeda: " + string.Join(", ", images.Select(kv => $"{Path.GetFileName(kv.Key)} {kv.Value.Width} × {kv.Value.Height}")) +
                        ". Samakan ukurannya dulu; RailNexus tidak mengubah ukuran secara diam-diam.");
        var (w, h) = sizes[0];

        var rgba = new byte[w * h * 4];
        var report = new List<string>();
        for (var c = 0; c < 4; c++)
        {
            var input = inputs[c];
            if (input.IsConstant)
            {
                for (var i = c; i < rgba.Length; i += 4) rgba[i] = input.Constant;
                report.Add($"{Names[c]}: nilai konstan pilihan pengguna ({input.Constant}), bukan hasil pengukuran");
                continue;
            }
            var src = images[input.ImagePath!];
            if (input.Channel == SourceChannel.A && !src.HasAlpha)
                notes.Add($"{Names[c]} diambil dari alpha {Path.GetFileName(input.ImagePath)}, tetapi gambar itu tidak memiliki alpha (nilai 255).");
            for (var p = 0; p < w * h; p++)
            {
                var s = p * 4;
                rgba[p * 4 + c] = input.Channel switch
                {
                    SourceChannel.R => src.Rgba[s],
                    SourceChannel.G => src.Rgba[s + 1],
                    SourceChannel.B => src.Rgba[s + 2],
                    SourceChannel.A => src.Rgba[s + 3],
                    _ => (byte)((src.Rgba[s] * 299 + src.Rgba[s + 1] * 587 + src.Rgba[s + 2] * 114 + 500) / 1000),
                };
            }
            report.Add($"{Names[c]}: kanal {input.Channel} dari {Path.GetFileName(input.ImagePath)}");
            if (input.Channel == SourceChannel.Luminance)
                notes.Add($"{Names[c]} dihitung dari kecerahan {Path.GetFileName(input.ImagePath)} atas pilihan pengguna (bukan salinan kanal).");
        }
        return new PackResult(true, "", new RasterImage(w, h, rgba, hasAlpha: true), report, notes);

        PackResult Fail(string e) => new(false, e, null, [], notes);
    }
}
