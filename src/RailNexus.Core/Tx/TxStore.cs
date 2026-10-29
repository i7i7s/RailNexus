using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RailNexus.Core.Tx;

public sealed record JournalEntry(DateTimeOffset At, TxPhase Phase, string Note);

/// <summary>
/// Penyimpanan transaksi milik RailNexus: manifest, preimage (byte asli), dan jurnal
/// fase yang ditulis langsung ke disk agar keadaan setelah crash dapat dikenali (PRD 21.3).
/// </summary>
public sealed class TxStore
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(), new KuidJsonConverter() },
    };

    private static readonly JsonSerializerOptions JsonLine = new(Json) { WriteIndented = false };

    public string Root { get; }

    public TxStore(string root)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(TxRoot);
        Directory.CreateDirectory(LockRoot);
    }

    private string TxRoot => Path.Combine(Root, "tx");
    private string LockRoot => Path.Combine(Root, "locks");
    private string TxDir(string id) => Path.Combine(TxRoot, id);
    private string JournalPath(string id) => Path.Combine(TxDir(id), "journal.jsonl");
    private string ManifestPath(string id) => Path.Combine(TxDir(id), "manifest.json");
    private string PreRoot(string id) => Path.Combine(TxDir(id), "pre");

    public string NewTxId() =>
        DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>Dipanggil setelah fase tercatat di disk. Dipakai uji crash nyata (Tahap 0).</summary>
    public Action<string, TxPhase>? AfterAppend { get; set; }

    public void Append(string id, TxPhase phase, string note = "")
    {
        AppendCore(id, phase, note);
        AfterAppend?.Invoke(id, phase);
    }

    private void AppendCore(string id, TxPhase phase, string note)
    {
        Directory.CreateDirectory(TxDir(id));
        var line = JsonSerializer.Serialize(new JournalEntry(DateTimeOffset.UtcNow, phase, note), JsonLine);
        using var fs = new FileStream(JournalPath(id), FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        fs.Write(bytes);
        fs.Flush(flushToDisk: true);
    }

    public IReadOnlyList<JournalEntry> ReadJournal(string id)
    {
        var path = JournalPath(id);
        if (!File.Exists(path)) return [];
        return File.ReadAllLines(path)
            .Where(l => l.Length > 0)
            .Select(l => JsonSerializer.Deserialize<JournalEntry>(l, Json)!)
            .ToList();
    }

    public TxPhase? LastPhase(string id) => ReadJournal(id) is { Count: > 0 } j ? j[^1].Phase : null;

    public void SaveManifest(TxManifest manifest)
    {
        Directory.CreateDirectory(TxDir(manifest.TxId));
        File.WriteAllText(ManifestPath(manifest.TxId), JsonSerializer.Serialize(manifest, Json));
    }

    public TxManifest? LoadManifest(string id) =>
        File.Exists(ManifestPath(id)) ? JsonSerializer.Deserialize<TxManifest>(File.ReadAllText(ManifestPath(id)), Json) : null;

    public void SavePreimage(string id, string relativePath, byte[] bytes)
    {
        var path = SafePath.Combine(PreRoot(id), relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>Ukuran backup (preimage) sebuah transaksi dalam byte (PRD 21.5).</summary>
    public long BackupBytes(string id) =>
        Directory.Exists(PreRoot(id)) ? Directory.EnumerateFiles(PreRoot(id), "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0;

    public byte[] ReadPreimage(string id, string relativePath) =>
        File.ReadAllBytes(SafePath.Combine(PreRoot(id), relativePath));

    public IEnumerable<string> AllTxIds() =>
        Directory.EnumerateDirectories(TxRoot).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal);

    /// <summary>Transaksi yang belum ditutup — kandidat rekonsiliasi setelah crash (AC-12).</summary>
    public IReadOnlyList<string> FindIncomplete() =>
        AllTxIds().Where(id => LastPhase(id) is { } p && p != TxPhase.Closed).ToList();

    /// <summary>Kunci aplikasi per aset. Tidak menjamin Content Manager ikut terkunci (PRD 21.3).</summary>
    public IDisposable AcquireLock(Kuid kuid)
    {
        var name = $"{kuid.UserId}_{kuid.ContentId}.lock".Replace('-', 'm');
        var path = Path.Combine(LockRoot, name);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
    }
}

internal sealed class KuidJsonConverter : JsonConverter<Kuid>
{
    public override Kuid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Kuid.Parse(reader.GetString() ?? throw new JsonException("KUID kosong"));

    public override void Write(Utf8JsonWriter writer, Kuid value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
