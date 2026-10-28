using RailNexus.Core.Tx;

namespace RailNexus.Core;

/// <summary>Pandangan baca-saja atas berkas di folder tujuan (folder edit aset, snapshot, atau folder ekspor).</summary>
public interface IAssetFileView
{
    /// <summary>Isi berkas, atau null bila berkas tidak ada.</summary>
    byte[]? Read(string relativePath);

    /// <summary>Hash SHA-256 berkas, atau null bila tidak ada. Dipakai untuk benturan dan hash dasar transaksi.</summary>
    string? Hash(string relativePath) => Read(relativePath) is { } b ? Hashing.Sha256(b) : null;
}

public sealed class FolderFileView(string root) : IAssetFileView
{
    public byte[]? Read(string relativePath)
    {
        var p = SafePath.Combine(root, relativePath);
        return File.Exists(p) ? File.ReadAllBytes(p) : null;
    }
}

public sealed class DictionaryFileView(IReadOnlyDictionary<string, byte[]> files) : IAssetFileView
{
    public static DictionaryFileView Empty { get; } = new(new Dictionary<string, byte[]>());

    public byte[]? Read(string relativePath)
    {
        var key = AssetSnapshot.Normalize(relativePath);
        return files.FirstOrDefault(kv => AssetSnapshot.Normalize(kv.Key).Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
    }
}

public sealed record SnapshotFile(string RelativePath, long Size, string Hash);

/// <summary>
/// Isi aset terpasang yang dibaca saat aset dibuka untuk diedit, lalu edit ditutup kembali.
/// Hash setiap berkas selalu tersedia; isi berkas disimpan sampai batas ukuran total sehingga
/// aset bertekstur besar tidak menghabiskan memori. Dasar bagi draft, workshop, dan editor.
/// </summary>
public sealed class AssetSnapshot : IAssetFileView
{
    public const long DefaultRetainLimit = 64L * 1024 * 1024;

    private readonly Dictionary<string, SnapshotFile> _files;
    private readonly Dictionary<string, byte[]> _content;

    public IReadOnlyList<SnapshotFile> Files { get; }
    public DateTimeOffset CapturedAt { get; }

    private AssetSnapshot(List<SnapshotFile> files, Dictionary<string, byte[]> content, DateTimeOffset at)
    {
        Files = files.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        _files = files.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);
        _content = new Dictionary<string, byte[]>(content, StringComparer.OrdinalIgnoreCase);
        CapturedAt = at;
    }

    internal static string Normalize(string p) => p.Replace('\\', '/').TrimStart('/');

    /// <summary>Membaca seluruh folder. config.txt dan berkas teks selalu disimpan isinya.</summary>
    public static AssetSnapshot Capture(string folder, long retainLimit = DefaultRetainLimit)
    {
        var files = new List<SnapshotFile>();
        var content = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long retained = 0;
        // Berkas kecil dan teks didahulukan agar metadata selalu tersedia walau batas tercapai.
        var paths = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(f => (Full: f, Rel: Normalize(Path.GetRelativePath(folder, f)), Info: new FileInfo(f)))
            .OrderBy(x => IsText(x.Rel) ? 0 : 1).ThenBy(x => x.Info.Length);
        foreach (var (full, rel, info) in paths)
        {
            var bytes = File.ReadAllBytes(full);
            files.Add(new SnapshotFile(rel, bytes.LongLength, Hashing.Sha256(bytes)));
            if (IsText(rel) || retained + bytes.LongLength <= retainLimit)
            {
                content[rel] = bytes;
                retained += bytes.LongLength;
            }
        }
        return new AssetSnapshot(files, content, DateTimeOffset.UtcNow);
    }

    public static AssetSnapshot FromFiles(IReadOnlyDictionary<string, byte[]> files) =>
        new(files.Select(kv => new SnapshotFile(Normalize(kv.Key), kv.Value.LongLength, Hashing.Sha256(kv.Value))).ToList(),
            files.ToDictionary(kv => Normalize(kv.Key), kv => kv.Value), DateTimeOffset.UtcNow);

    private static bool IsText(string rel) => rel.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

    public bool Exists(string relativePath) => _files.ContainsKey(Normalize(relativePath));

    public bool IsRetained(string relativePath) => _content.ContainsKey(Normalize(relativePath));

    public byte[]? Read(string relativePath)
    {
        var key = Normalize(relativePath);
        if (!_files.ContainsKey(key)) return null;
        return _content.TryGetValue(key, out var b) ? b
            : throw new NotSupportedException($"Isi '{key}' tidak disimpan di snapshot (melebihi batas ukuran); hanya hash-nya yang tersedia.");
    }

    public string? Hash(string relativePath) => _files.TryGetValue(Normalize(relativePath), out var f) ? f.Hash : null;
}
