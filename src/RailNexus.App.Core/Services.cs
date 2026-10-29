using System.Text.Json;
using RailNexus.Core;
using RailNexus.Core.Config;
using RailNexus.Core.Library;
using RailNexus.Core.Roles;
using RailNexus.Core.Tx;

namespace RailNexus.App.Core;

/// <summary>
/// Pengaturan aplikasi yang disimpan per pengguna. Penulisan aset tetap dibatasi ke KUID aset uji
/// sampai profil peran lolos uji integrasi pada Trainz asli (docs/tahap0.md).
/// </summary>
public sealed class AppSettings
{
    public string? TrainzFolder { get; set; }
    /// <summary>KUID aset uji yang boleh dibuka untuk diedit dan ditulis.</summary>
    public List<string> WritableTestKuids { get; set; } = [];

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load(string path)
    {
        if (!File.Exists(path)) return new AppSettings();
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings(); }
        catch (JsonException) { return new AppSettings(); }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }

    public IReadOnlySet<(int UserId, int ContentId)> WritableSet() =>
        WritableTestKuids.Select(s => Kuid.TryParse(s, out var k) ? (k.UserId, k.ContentId) : ((int, int)?)null)
            .OfType<(int, int)>().ToHashSet();
}

/// <summary>Pengaturan beserta lokasi penyimpanannya.</summary>
public sealed class SettingsStore(string path)
{
    public string Path => path;
    public AppSettings Settings { get; } = AppSettings.Load(path);
    public void Save() => Settings.Save(path);
}

public sealed record InstallationCandidate(string Name, string Folder, bool ToolFound);

/// <summary>Satu operasi profil build beserta status verifikasinya (UI-25: kemampuan yang diuji).</summary>
public sealed record Capability(string Operation, StatusLabel Status, string Evidence);

public enum ConnectionState
{
    NotChecked,
    /// <summary>Build teruji: dapat membaca dan menulis aset yang diizinkan.</summary>
    Connected,
    /// <summary>Terhubung pada build yang belum diuji: hanya baca (PRD 7: mode terbatas).</summary>
    ReadOnly,
    TrainzNotRunning,
    ToolNotFound,
    Failed,
}

public sealed record CheckStep(bool Ok, string Title, string Detail);

public sealed record ConnectionCheck(ConnectionState State, string Build, string Message, IReadOnlyList<CheckStep> Steps)
{
    public bool CanOpen => State is ConnectionState.Connected or ConnectionState.ReadOnly;
}

/// <summary>Membaca isi aset terpasang (config dan berkas lain) untuk menyusun pilihan dan draft.</summary>
public interface IAssetReader
{
    Task<OpResult<AssetSnapshot>> ReadSnapshotAsync(Kuid kuid, CancellationToken ct = default);
}

/// <summary>
/// Membaca isi aset dengan pola yang sudah dibuktikan di Tahap 0: buka edit → salin isi → revert.
/// Byte yang dibaca sama dengan yang nanti diverifikasi transaksi (AC-10). Aset yang sedang terbuka
/// di luar RailNexus tidak disentuh (AC-09).
/// </summary>
public sealed class EditFolderAssetReader(ITrainzAdapter adapter) : IAssetReader
{
    public async Task<OpResult<AssetSnapshot>> ReadSnapshotAsync(Kuid kuid, CancellationToken ct = default)
    {
        var status = await adapter.GetStatusAsync(kuid, ct).ConfigureAwait(false);
        if (!status.IsSuccess) return new OpResult<AssetSnapshot>(status.Outcome, null, status.Message);
        if (status.Value!.State == AssetState.OpenForEdit)
            return OpResult<AssetSnapshot>.Fail("Aset masih terbuka untuk diedit di luar RailNexus. Selesaikan atau batalkan dulu di Content Manager.");
        if (status.Value.State == AssetState.Missing)
            return OpResult<AssetSnapshot>.Fail("Aset tidak terpasang di profil ini.");

        var edit = await adapter.BeginEditAsync(kuid, ct).ConfigureAwait(false);
        if (!edit.IsSuccess) return new OpResult<AssetSnapshot>(edit.Outcome, null, $"Isi aset tidak dapat dibuka: {edit.Message}");
        AssetSnapshot? snapshot = null;
        var error = "";
        try
        {
            snapshot = AssetSnapshot.Capture(edit.Value!);
            if (!snapshot.Exists("config.txt")) error = "config.txt tidak ditemukan di folder aset.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error = $"Isi aset tidak dapat dibaca: {ex.Message}"; }
        finally
        {
            var rv = await adapter.RevertAsync(kuid, CancellationToken.None).ConfigureAwait(false);
            if (!rv.IsSuccess)
                error = (error.Length > 0 ? error + " " : "") +
                        $"Aset dibuka untuk dibaca tetapi belum dapat ditutup kembali ({rv.Message}). Periksa di Content Manager.";
        }
        return snapshot is not null && error.Length == 0 ? OpResult<AssetSnapshot>.Ok(snapshot) : OpResult<AssetSnapshot>.Fail(error);
    }
}

/// <summary>Satu sambungan aktif ke profil Trainz. Semua tindakan memakai profil ini (CONNECT-01, AC-21).</summary>
public sealed class AppSession(
    string profileLabel,
    LibraryIndex library,
    IAssetReader assetReader,
    TxStore store,
    TransactionRunner? runner,
    string? writeDisabledReason,
    string? installationFolder = null,
    ITrainzAdapter? adapter = null) : IDisposable
{
    public string ProfileLabel => profileLabel;
    public string? InstallationFolder => installationFolder;
    /// <summary>Untuk operasi baca seperti validasi; penulisan selalu lewat <see cref="Runner"/>.</summary>
    public ITrainzAdapter? Adapter => adapter;
    /// <summary>Pengisian indeks dan pembacaan KIND; null bila tidak tersedia pada sambungan ini.</summary>
    public ILibraryIndexer? Indexer { get; init; }

    /// <summary>Keadaan target menurut indeks profil ini (revisi apa pun).</summary>
    public RailNexus.Core.Dependencies.TargetState TargetState(Kuid kuid) =>
        library.Search(new LibraryQuery(kuid.ToString(), Limit: 1)).FirstOrDefault()?.State switch
        {
            AssetState.Installed => RailNexus.Core.Dependencies.TargetState.Installed,
            AssetState.Faulty => RailNexus.Core.Dependencies.TargetState.Faulty,
            AssetState.OpenForEdit => RailNexus.Core.Dependencies.TargetState.OpenForEdit,
            AssetState.Missing => RailNexus.Core.Dependencies.TargetState.Missing,
            _ => RailNexus.Core.Dependencies.TargetState.NotIndexed,
        };

    public LibraryEntry? Find(Kuid kuid) => library.Search(new LibraryQuery(kuid.ToString(), Limit: 1)).FirstOrDefault();
    public LibraryIndex Library => library;
    public IAssetReader AssetReader => assetReader;
    /// <summary>Jurnal dan backup transaksi profil ini, untuk Riwayat.</summary>
    public TxStore Store => store;
    /// <summary>null bila penulisan tidak tersedia; alasannya di <see cref="WriteDisabledReason"/>.</summary>
    public TransactionRunner? Runner => runner;
    public string? WriteDisabledReason => runner is null ? writeDisabledReason ?? "Penulisan tidak tersedia." : null;

    /// <summary>Draft dan config per aset; tetap ada saat pengguna berpindah modul (AC-37).</summary>
    public Dictionary<Kuid, AssetWorkspace> Workspaces { get; } = new();

    public AssetWorkspace Workspace(LibraryEntry entry)
    {
        if (!Workspaces.TryGetValue(entry.Kuid, out var ws)) Workspaces[entry.Kuid] = ws = new AssetWorkspace(entry);
        else ws.Entry = entry;
        return ws;
    }

    public void Dispose() => library.Dispose();
}

/// <summary>Tindakan navigasi yang dapat dipicu halaman aset (dari Dependency/Perbaikan ke modul lain).</summary>
public sealed record AssetNavigation(
    Action<RailNexus.Core.Roles.IRole> OpenRole,
    Action<LibraryEntry> OpenAsset,
    Action OpenTexture,
    Action OpenEditor,
    Action Back);

/// <summary>Aset aktif: catatan pustaka, config yang terakhir dibaca, dan draft bersama.</summary>
public sealed class AssetWorkspace(LibraryEntry entry)
{
    public LibraryEntry Entry { get; set; } = entry;
    public Draft Draft { get; } = new(entry.Kuid);
    /// <summary>Isi aset terakhir yang dibaca; null bila belum dibaca atau sudah kedaluwarsa.</summary>
    public AssetSnapshot? Snapshot { get; private set; }
    public byte[]? ConfigBytes { get; private set; }
    public string? ConfigText { get; private set; }
    public ConfigDocument? Document { get; private set; }
    public string? ReadError { get; private set; }

    public event EventHandler? Changed;

    /// <summary>Isi gambar thumbnail aset bila ada dan tersimpan di snapshot; didekode oleh UI (JPG/PNG).</summary>
    public byte[]? Thumbnail { get; private set; }

    public void SetSnapshot(AssetSnapshot snapshot)
    {
        Snapshot = snapshot;
        SetConfig(snapshot.Read("config.txt")!);
        Thumbnail = FindThumbnail(snapshot, Document);
    }

    /// <summary>Thumbnail dari container thumbnails/image di config, atau berkas thumbnail.jpg/png.</summary>
    internal static byte[]? FindThumbnail(AssetSnapshot snapshot, ConfigDocument? doc)
    {
        var names = new List<string>();
        if (doc is not null)
        {
            names.AddRange(doc.Root.Named("thumbnails").Where(t => t.Container is not null)
                .SelectMany(t => t.Container!.Children).Where(c => c.Container is not null)
                .SelectMany(c => c.Container!.Named("image")).Select(i => i.Value?.Text).OfType<string>());
            if (doc.TopLevelString("thumbnail") is { } single) names.Add(single);
        }
        names.AddRange(["thumbnail.jpg", "thumbnail.png"]);
        foreach (var n in names)
        {
            if (!snapshot.Exists(n) || !snapshot.IsRetained(n)) continue;
            return snapshot.Read(n);
        }
        return null;
    }

    private void SetConfig(byte[] bytes)
    {
        ConfigBytes = bytes;
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        ConfigText = new System.Text.UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        Document = ConfigDocument.Parse(ConfigText);
        ReadError = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetReadError(string message)
    {
        ReadError = message;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Isi config tidak lagi dianggap terkini (mis. setelah transaksi); dibaca ulang saat dibutuhkan.</summary>
    public void InvalidateConfig()
    {
        Snapshot = null;
        ConfigBytes = null;
        ConfigText = null;
        Document = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyDraftChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>Deteksi dan pemeriksaan instalasi; menghasilkan sesi bila koneksi dapat dipakai.</summary>
public interface IConnectionService
{
    IReadOnlyList<InstallationCandidate> FindInstallations();
    InstallationCandidate Inspect(string folder);
    Task<ConnectionCheck> CheckAsync(InstallationCandidate installation, CancellationToken ct = default);
    AppSession Open(InstallationCandidate installation, ConnectionCheck check);
    /// <summary>Operasi di profil build beserta bukti verifikasinya.</summary>
    IReadOnlyList<Capability> Capabilities();
    /// <summary>Folder data (indeks, jurnal, backup).</summary>
    string DataRoot { get; }
}
