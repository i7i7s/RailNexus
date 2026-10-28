using System.Text.RegularExpressions;
using RailNexus.Core;
using RailNexus.Core.Library;
using RailNexus.Core.Process;
using RailNexus.Core.Tx;
using RailNexus.Trainz;

namespace RailNexus.App.Core;

/// <summary>
/// Koneksi ke TrainzUtil memakai profil build terverifikasi. Indeks pustaka dan jurnal transaksi
/// berbagi lokasi dengan CLI (%LOCALAPPDATA%\RailNexus\tahap0) sehingga indeks dari
/// <c>railnexus index</c> langsung terlihat di aplikasi.
/// </summary>
public sealed partial class TrainzConnectionService(AppSettings settings, string profilePath, string dataRoot, IProcessRunner runner)
    : IConnectionService
{
    [GeneratedRegex(@"\d{5,}")]
    private static partial Regex BuildNumber();

    public string DataRoot => dataRoot;

    public IReadOnlyList<Capability> Capabilities()
    {
        var profile = CommandProfile.Load(profilePath);
        return profile.Commands.Select(kv => new Capability(
            OperationLabel(kv.Key),
            kv.Value.Verified ? new StatusLabel("Terverifikasi", Tone.Ok) : new StatusLabel("Belum diverifikasi", Tone.Warn),
            kv.Value.Evidence ?? "")).ToList();
    }

    internal static string OperationLabel(TrainzOp op) => op switch
    {
        TrainzOp.Help => "Daftar perintah",
        TrainzOp.Version => "Versi build",
        TrainzOp.Status => "Status aset",
        TrainzOp.BeginEdit => "Buka untuk diedit",
        TrainzOp.Commit => "Submit",
        TrainzOp.Revert => "Batalkan edit",
        TrainzOp.Validate => "Validasi",
        TrainzOp.ImportFolder => "Impor folder",
        TrainzOp.StatusBatch => "Status banyak aset",
        _ => op.ToString(),
    };

    public IReadOnlyList<InstallationCandidate> FindInstallations()
    {
        var list = new List<InstallationCandidate>();
        if (settings.TrainzFolder is { Length: > 0 } saved) list.Add(Inspect(saved));
        foreach (var (name, folder) in TrainzInstallations.KnownFolders)
            if (TrainzInstallations.HasTool(folder) && !list.Any(c => SameFolder(c.Folder, folder)))
                list.Add(new InstallationCandidate(name, folder, true));
        return list;
    }

    public InstallationCandidate Inspect(string folder) =>
        new(Path.GetFileName(folder.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : folder, folder, TrainzInstallations.HasTool(folder));

    private static bool SameFolder(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private TrainzUtilAdapter CreateAdapter(InstallationCandidate c, CommandProfile profile) =>
        new(new TrainzUtilOptions(TrainzInstallations.ToolPath(c.Folder), profile, settings.WritableSet()), runner);

    public async Task<ConnectionCheck> CheckAsync(InstallationCandidate installation, CancellationToken ct = default)
    {
        if (!installation.ToolFound)
            return new ConnectionCheck(ConnectionState.ToolNotFound, "", $"{TrainzInstallations.ToolName} tidak ditemukan di folder ini.",
                [new CheckStep(false, "Alat Trainz tidak ditemukan", $"Pilih folder instalasi yang berisi {TrainzInstallations.ToolName}.")]);

        var profile = CommandProfile.Load(profilePath);
        var version = await CreateAdapter(installation, profile).GetVersionAsync(ct).ConfigureAwait(false);
        switch (version.Outcome)
        {
            case Outcome.TrainzNotRunning:
                // Temuan Tahap 0: Trainz harus berjalan dengan Advanced Debug Tools aktif dan dibuka ulang.
                return new ConnectionCheck(ConnectionState.TrainzNotRunning, "", "Trainz belum tersambung.",
                [
                    new CheckStep(false, "Trainz belum menjawab", "Buka Trainz, lalu periksa ulang."),
                    new CheckStep(false, "Aktifkan Advanced Debug Tools", "Launcher → Trainz Settings → Advanced Debug Tools."),
                    new CheckStep(false, "Tutup Trainz sepenuhnya lalu buka ulang", "Pengaturan baru berlaku setelah Trainz dijalankan ulang."),
                ]);
            case Outcome.Success:
                break;
            default:
                return new ConnectionCheck(ConnectionState.Failed, "", $"Pemeriksaan belum berhasil: {version.Message}",
                    [new CheckStep(false, "Identitas build tidak terbaca", version.Message)]);
        }

        var build = version.Value!;
        var number = BuildNumber().Match(build).Value;
        var tested = number.Length > 0 && profile.Build.Contains(number, StringComparison.Ordinal);
        var writable = settings.WritableSet().Count;
        var steps = new List<CheckStep>
        {
            new(true, "Identitas build terbaca", build),
            tested
                ? new(true, "Profil kompatibilitas tersedia", profile.Build)
                : new(false, "Build belum diuji", $"Profil yang tersedia: {profile.Build}. Aplikasi dibuka dalam mode hanya baca."),
            writable > 0
                ? new(true, "Penulisan dibatasi ke aset uji", $"{writable} aset uji terdaftar di pengaturan.")
                : new(false, "Belum ada aset uji yang boleh ditulis", "Daftarkan KUID aset uji di pengaturan untuk mencoba perubahan."),
        };
        return tested
            ? new ConnectionCheck(ConnectionState.Connected, build, "Terhubung · dapat mengedit aset uji", steps)
            : new ConnectionCheck(ConnectionState.ReadOnly, build, "Terhubung · build belum diuji, hanya baca", steps);
    }

    public AppSession Open(InstallationCandidate installation, ConnectionCheck check)
    {
        if (!check.CanOpen) throw new InvalidOperationException("Koneksi belum dapat dipakai.");
        var profile = CommandProfile.Load(profilePath);
        var adapter = CreateAdapter(installation, profile);
        var store = new TxStore(dataRoot);
        var library = new LibraryIndex(Path.Combine(dataRoot, "library.db"), profile.Id);
        var runner = check.State == ConnectionState.Connected ? new TransactionRunner(adapter, store) : null;
        return new AppSession($"{installation.Name} — {profile.Id}", library, new EditFolderAssetReader(adapter), store, runner,
            runner is null ? "Build ini belum diuji; perubahan dinonaktifkan sampai profilnya lolos uji." : null, installation.Folder, adapter);
    }
}
