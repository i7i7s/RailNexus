using RailNexus.Core.Config;

namespace RailNexus.Core.Texture;

public enum InventoryKind
{
    /// <summary>Gambar tanpa pasangan .texture.txt dan tidak dipakai langsung oleh config.</summary>
    ImageWithoutMetadata,
    /// <summary>Metadata menunjuk berkas yang tidak ada.</summary>
    MissingTarget,
    /// <summary>Metadata tidak dapat dibaca dengan aman (encoding, key ganda).</summary>
    UnreadableMetadata,
}

public sealed record InventoryFinding(InventoryKind Kind, string Path, string Message);

/// <summary>
/// Inventaris gambar dan metadata di satu aset (PRD 13.4 langkah 2, UI-15). Hanya mendeteksi;
/// perbaikan memakai <see cref="TextureWorkshop.PrepareMetadata"/> atas pilihan pengguna.
/// </summary>
public static class TextureInventory
{
    public static readonly string[] ImageExtensions = [".tga", ".png", ".bmp", ".jpg", ".jpeg"];

    public static bool IsImage(string path) => ImageExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<InventoryFinding> Scan(AssetSnapshot snapshot, ConfigDocument? config)
    {
        var findings = new List<InventoryFinding>();
        var files = snapshot.Files.Select(f => f.RelativePath).ToList();
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var meta in files.Where(f => f.EndsWith(TextureWorkshop.MetadataExtension, StringComparison.OrdinalIgnoreCase)))
        {
            var dir = System.IO.Path.GetDirectoryName(meta)?.Replace('\\', '/') is { Length: > 0 } d ? d + "/" : "";
            byte[]? bytes;
            try { bytes = snapshot.Read(meta); }
            catch (NotSupportedException) { bytes = null; }
            var (parsed, err) = bytes is null ? (null, "isi tidak tersedia") : TextureMetadata.Parse(bytes);
            if (parsed is null)
            {
                findings.Add(new InventoryFinding(InventoryKind.UnreadableMetadata, meta, err));
                continue;
            }
            foreach (var key in new[] { "Primary", "Alpha" })
            {
                if (parsed.Get(key) is not { Length: > 0 } value) continue;
                var target = dir + value;
                referenced.Add(target);
                if (!snapshot.Exists(target))
                    findings.Add(new InventoryFinding(InventoryKind.MissingTarget, meta, $"{key}={value} menunjuk berkas yang tidak ada."));
            }
            // Pasangan nama yang sama juga dianggap terhubung (badan.texture.txt ↔ badan.tga).
            var stem = meta[..^TextureWorkshop.MetadataExtension.Length];
            foreach (var ext in ImageExtensions) referenced.Add(stem + ext);
        }

        // Gambar yang disebut langsung di config (mis. thumbnail) bukan tekstur yang butuh metadata.
        if (config is not null)
            foreach (var t in config.AllTags())
                if (t.Value is { Kind: ValueKind.String } v)
                    referenced.Add(v.Text.Replace('\\', '/'));

        foreach (var img in files.Where(IsImage).Where(f => !referenced.Contains(f) && !referenced.Contains(System.IO.Path.GetFileName(f))))
            findings.Add(new InventoryFinding(InventoryKind.ImageWithoutMetadata, img, "Gambar belum memiliki .texture.txt."));
        return findings;
    }
}
