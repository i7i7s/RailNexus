using System.Text.Json;
using System.Text.Json.Serialization;

namespace RailNexus.Trainz;

/// <summary>Operasi konseptual RailNexus yang dipetakan ke perintah TrainzUtil.</summary>
public enum TrainzOp
{
    Help,
    Version,
    Status,
    BeginEdit,
    Commit,
    Revert,
    Validate,
    ImportFolder,
    /// <summary>'filterbystatus I &lt;KUID&gt; [..]' — status banyak aset terpasang dalam satu panggilan.</summary>
    StatusBatch,
    /// <summary>'searchbycategory &lt;kategori&gt;' — KUID aset lokal per kategori (untuk indeks pustaka).</summary>
    SearchByCategory,
    /// <summary>'printconfig &lt;KUID&gt;' — teks config yang sudah dinormalkan Trainz (baca saja).</summary>
    PrintConfig,
}

public sealed record CommandSpec(
    /// <summary>Argumen; placeholder {kuid}, {path}, dan {arg} diganti saat dijalankan.</summary>
    IReadOnlyList<string> Args,
    /// <summary>
    /// true hanya setelah sintaks dan bentuk keluarannya dibuktikan dengan output nyata
    /// pada build ini. Operasi tulis dengan spesifikasi belum terverifikasi ditolak.
    /// </summary>
    bool Verified,
    string? Evidence = null,
    int TimeoutSeconds = 60,
    /// <summary>Bila diisi, menggantikan TimeoutSeconds (dipakai uji timeout).</summary>
    int? TimeoutMilliseconds = null)
{
    public TimeSpan Timeout => TimeoutMilliseconds is { } ms ? TimeSpan.FromMilliseconds(ms) : TimeSpan.FromSeconds(TimeoutSeconds);
}

/// <summary>
/// Profil kompatibilitas per build Trainz (PRD 21.1: keluaran berbeda antarbuild).
/// Disimpan sebagai data agar hasil pengujian dapat ditinjau dan diperbarui tanpa mengubah kode.
/// </summary>
public sealed record CommandProfile(
    string Id,
    string Build,
    string Notes,
    IReadOnlyDictionary<TrainzOp, CommandSpec> Commands)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static CommandProfile Load(string path) =>
        JsonSerializer.Deserialize<CommandProfile>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException($"Profil kosong: {path}");

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Json));

    public static bool IsMutating(TrainzOp op) => op is TrainzOp.BeginEdit or TrainzOp.Commit or TrainzOp.Revert or TrainzOp.ImportFolder;
}
