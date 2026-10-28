using System.Collections.ObjectModel;
using System.Windows.Input;
using RailNexus.Core;
using RailNexus.Core.Config;
using RailNexus.Core.Library;
using RailNexus.Core.Roles;

namespace RailNexus.App.Core.ViewModels;

/// <summary>Satu tombol tugas. Tugas yang belum dapat dipakai tetap tampil dengan alasan singkat (PRD 6.1).</summary>
public sealed class TaskCard(string title, string caption, StatusLabel status, string? unavailableReason, ICommand? open = null)
{
    public string Title => title;
    public string Caption => caption;
    public StatusLabel Status => status;
    public bool IsAvailable => unavailableReason is null && open is not null;
    public string? Reason => unavailableReason;
    public ICommand? Open => open;
}

public sealed record TaskGroup(string Title, IReadOnlyList<TaskCard> Cards);

/// <summary>UI-03 Pusat edit aset: "Mau edit apa?" berdasarkan isi config aset (PRD 6.1, RN-025).</summary>
public sealed class EditHubViewModel : ObservableObject, IPage
{
    private static readonly StatusLabel NotApplicable = new("Tidak berlaku", Tone.Mute);
    private static readonly StatusLabel NotYet = new("Belum tersedia", Tone.Mute);

    private readonly AppSession _session;
    private readonly AssetWorkspace _workspace;
    private readonly Action<IRole> _openRole;
    private readonly Action _back;
    private bool _loading;
    private string? _error;

    public EditHubViewModel(AppSession session, AssetWorkspace workspace, FooterAction footer, Action<IRole> openRole, Action back)
    {
        _session = session;
        _workspace = workspace;
        _openRole = openRole;
        _back = back;
        Footer = footer;
        RetryCommand = new AsyncCommand(() => LoadAsync(force: true));
        if (workspace.Document is not null) BuildGroups();
    }

    public string Title => "Mau edit apa?";
    public FooterAction Footer { get; }
    public string? BackLabel => "Kembali ke Pustaka";
    public void Back() => _back();

    public LibraryEntry Asset => _workspace.Entry;
    public ObservableCollection<TaskGroup> Groups { get; } = new();
    public bool IsLoading { get => _loading; private set => Set(ref _loading, value); }
    /// <summary>Pesan bila isi aset tidak dapat dibaca; tugas peran dinonaktifkan dengan alasan yang sama.</summary>
    public string? Error { get => _error; private set => Set(ref _error, value); }
    public AsyncCommand RetryCommand { get; }

    public string Intro => "Pilihan di bawah disesuaikan dengan isi aset ini. Pilih satu; perubahan disimpan ke draft dan baru ditulis saat Terapkan & Selesai.";

    /// <summary>Membaca config aset bila belum ada di ruang kerja, lalu menyusun tugas.</summary>
    public async Task LoadAsync(bool force = false)
    {
        if (_workspace.Document is not null && !force)
        {
            BuildGroups();
            return;
        }
        IsLoading = true;
        Error = null;
        try
        {
            var r = await _session.ConfigReader.ReadConfigAsync(_workspace.Entry.Kuid);
            if (r.IsSuccess) _workspace.SetConfig(r.Value!);
            else
            {
                _workspace.SetReadError(r.Message);
                Error = r.Message;
            }
        }
        finally { IsLoading = false; }
        BuildGroups();
    }

    private void BuildGroups()
    {
        Groups.Clear();
        var doc = _workspace.Document;
        var roleCards = RoleCatalog.P0.ToDictionary(r => r.Id, r => RoleCard(r, doc), StringComparer.OrdinalIgnoreCase);

        Groups.Add(new TaskGroup("Suara", [roleCards["hornsound"], roleCards["enginesound"], roleCards["tracksound"]]));
        Groups.Add(new TaskGroup("Komponen & dependency",
        [
            roleCards["bogie"],
            new TaskCard("Semua komponen & dependency", "Inventaris referensi per peran", NotYet,
                "Inventaris dependency belum tersedia di aplikasi."),
        ]));
        Groups.Add(new TaskGroup("Tampilan, tekstur & model",
        [
            new TaskCard("Tekstur & material", "Gambar → TGA + .texture.txt", NotYet,
                "Mesin konversi tekstur sudah ada, tetapi panelnya belum tersedia di aplikasi."),
            new TaskCard("Lihat model", "Preview geometri dan material", new("P2", Tone.Mute),
                "Renderer model belum tersedia (PRD 15, P2)."),
        ]));
        Groups.Add(new TaskGroup("Periksa & lanjutan",
        [
            new TaskCard("Periksa & perbaiki", "Diagnosis dan perbaikan yang dapat dibuktikan", NotYet,
                "Pusat Perbaikan belum tersedia di versi ini."),
            new TaskCard("Buka editor lanjutan", "Formulir, struktur, dan teks config", NotYet,
                "Editor aset lengkap belum tersedia (P1)."),
            new TaskCard("Riwayat aset ini", "Pekerjaan sebelumnya dan Undo", NotYet,
                "Riwayat belum tersedia di aplikasi; sementara gunakan 'railnexus list' dan 'railnexus undo' di CLI."),
        ]));
    }

    private static string RoleTitle(IRole role) => role.Id switch
    {
        "hornsound" => "Ganti klakson",
        "enginesound" => "Ganti suara mesin",
        "tracksound" => "Suara roda-rel",
        "bogie" => "Ganti bogie",
        _ => role.Label,
    };

    private TaskCard RoleCard(IRole role, ConfigDocument? doc)
    {
        var title = RoleTitle(role);
        if (doc is null)
            return new TaskCard(title, role.CommunityTerm, NotApplicable,
                IsLoading ? "Sedang membaca isi aset…" : $"Isi aset belum terbaca. {Error ?? _workspace.ReadError}".Trim());
        if (doc.HasErrors)
        {
            var first = doc.Diagnostics.First(d => d.Severity == Severity.Error);
            return new TaskCard(title, role.CommunityTerm, NotApplicable,
                $"Config memiliki error struktur (baris {first.Line}: {first.Message}); perubahan otomatis tidak dilakukan.");
        }

        var slots = role.Locate(doc).ToList();
        if (slots.Count == 0)
            return new TaskCard(title, role.CommunityTerm, NotApplicable,
                $"Tidak tersedia: aset ini tidak punya referensi {role.CommunityTerm} yang dikenali, atau referensinya ganda/ambigu.");

        var caption = role.Id switch
        {
            "tracksound" => $"{role.CommunityTerm} · {slots.Count} target di aturan",
            "bogie" => $"{role.CommunityTerm} · {slots.Count} slot",
            _ => $"{role.CommunityTerm} · sekarang: {NameOf(slots[0].Current)}",
        };
        // Tingkat dukungan terlemah di antara slot yang ditampilkan.
        var support = slots.Select(s => s.Support).Max();
        string? reason = support is SupportLevel.NotSupported ? "Peran ini belum didukung untuk struktur aset ini." : null;
        return new TaskCard(title, caption, Labels.Support(support), reason, new RelayCommand(() => _openRole(role)));
    }

    /// <summary>Nama aset dari indeks bila ada (revisi apa pun); bila tidak, KUID apa adanya.</summary>
    internal string NameOf(Kuid kuid) =>
        _session.Library.Search(new LibraryQuery(kuid.ToString(), Limit: 1)).FirstOrDefault()?.Name ?? kuid.ToString();
}
