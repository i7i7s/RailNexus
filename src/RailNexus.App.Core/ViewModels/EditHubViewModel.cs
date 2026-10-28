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

/// <summary>Tujuan setiap tombol tugas; null = modul belum tersedia dari tempat ini.</summary>
public sealed record HubActions(
    Action<IRole> OpenRole,
    Action Back,
    Action? OpenHistory = null,
    Action? OpenEditor = null,
    Action? OpenTexture = null,
    Action? OpenDependency = null,
    Action? OpenRepair = null,
    Action? OpenSound = null);

/// <summary>UI-03 Pusat edit aset: "Mau edit apa?" berdasarkan isi config aset (PRD 6.1, RN-025).</summary>
public sealed class EditHubViewModel : ObservableObject, IPage
{
    private static readonly StatusLabel NotApplicable = new("Tidak berlaku", Tone.Mute);
    private static readonly StatusLabel NotYet = new("Belum tersedia", Tone.Mute);

    private readonly AppSession _session;
    private readonly AssetWorkspace _workspace;
    private readonly HubActions _actions;
    private bool _loading;
    private string? _error;

    public EditHubViewModel(AppSession session, AssetWorkspace workspace, FooterAction footer, HubActions actions)
    {
        _session = session;
        _workspace = workspace;
        _actions = actions;
        Footer = footer;
        RetryCommand = new AsyncCommand(() => LoadAsync(force: true));
        if (workspace.Document is not null) BuildGroups();
    }

    public string Title => "Mau edit apa?";
    public FooterAction Footer { get; }
    public string? BackLabel => "Kembali ke Pustaka";
    public void Back() => _actions.Back();

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
            var r = await _session.AssetReader.ReadSnapshotAsync(_workspace.Entry.Kuid);
            if (r.IsSuccess) _workspace.SetSnapshot(r.Value!);
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
        var roleCards = RoleCatalog.All.ToDictionary(r => r.Id, r => RoleCard(r, doc), StringComparer.OrdinalIgnoreCase);

        Groups.Add(new TaskGroup("Suara", [roleCards["hornsound"], roleCards["enginesound"], roleCards["tracksound"], SoundCard()]));
        Groups.Add(new TaskGroup("Komponen & dependency",
        [
            roleCards["bogie"],
            roleCards["interior"],
            roleCards["pantograph"],
            roleCards["enginespec"],
            DocCard("Semua komponen & dependency", doc is null ? "Inventaris referensi per peran"
                : $"{RailNexus.Core.Dependencies.DependencyInventory.Collect(doc, _workspace.Entry.Kuid).Count} referensi · status dan turunan",
                doc, _actions.OpenDependency),
        ]));
        Groups.Add(new TaskGroup("Tampilan, tekstur & model",
        [
            TextureCard(),
            new TaskCard("Lihat model", "Preview geometri dan material", new("P2", Tone.Mute),
                "Renderer model belum tersedia (PRD 15, P2)."),
        ]));
        Groups.Add(new TaskGroup("Periksa & lanjutan",
        [
            DocCard("Periksa & perbaiki", "Penyebab, bukti, dan tindakan yang tersedia", doc, _actions.OpenRepair),
            EditorCard(doc),
            HistoryCard(),
        ]));
    }

    private TaskCard DocCard(string title, string caption, ConfigDocument? doc, Action? open) =>
        new(title, caption, new("Terpandu", Tone.Info),
            doc is null ? $"Isi aset belum terbaca. {Error ?? _workspace.ReadError}".Trim() : null,
            open is null ? null : new RelayCommand(open));

    private TaskCard SoundCard()
    {
        const string title = "Berkas suara lokal";
        if (_workspace.Snapshot is not { } snap)
            return new TaskCard(title, "WAV di dalam aset", NotApplicable, $"Isi aset belum terbaca. {Error ?? _workspace.ReadError}".Trim());
        var n = SoundViewModel.CountAudio(snap);
        return n == 0
            ? new TaskCard(title, "WAV di dalam aset", NotApplicable, "Aset ini tidak memiliki berkas suara lokal.")
            : new TaskCard(title, $"{n} berkas · dengarkan dan ganti", new("Terpandu", Tone.Info), null,
                _actions.OpenSound is null ? null : new RelayCommand(_actions.OpenSound));
    }

    private TaskCard TextureCard()
    {
        if (_workspace.Snapshot is not { } snap)
            return new TaskCard("Tekstur & material", "Gambar → TGA + .texture.txt", NotApplicable,
                $"Isi aset belum terbaca. {Error ?? _workspace.ReadError}".Trim());
        var images = snap.Files.Count(f => RailNexus.Core.Texture.TextureInventory.IsImage(f.RelativePath));
        var missing = RailNexus.Core.Texture.TextureInventory.Scan(snap, _workspace.Document)
            .Count(f => f.Kind == RailNexus.Core.Texture.InventoryKind.ImageWithoutMetadata);
        return new TaskCard("Tekstur & material", missing > 0 ? $"{images} gambar · {missing} tanpa metadata" : $"{images} gambar",
            new("Terpandu", Tone.Info), null, _actions.OpenTexture is null ? null : new RelayCommand(_actions.OpenTexture));
    }

    private TaskCard EditorCard(ConfigDocument? doc) =>
        new("Buka editor lanjutan", "Formulir, struktur, teks, dan perubahan", new("Terbatas", Tone.Info),
            doc is null ? $"Isi aset belum terbaca. {Error ?? _workspace.ReadError}".Trim() : null,
            _actions.OpenEditor is null ? null : new RelayCommand(_actions.OpenEditor));

    private TaskCard HistoryCard()
    {
        var count = _session.Store.AllTxIds().Select(_session.Store.LoadManifest)
            .Count(m => m is not null && References.SameAsset(m.Kuid, _workspace.Entry.Kuid));
        return new TaskCard("Riwayat aset ini", count == 0 ? "Belum ada pekerjaan" : $"{count} pekerjaan · dapat ditinjau dan dibatalkan",
            new("Tersedia", Tone.Ok), null, _actions.OpenHistory is null ? null : new RelayCommand(_actions.OpenHistory));
    }

    private static string RoleTitle(IRole role) => role.Id switch
    {
        "hornsound" => "Ganti klakson",
        "enginesound" => "Ganti suara mesin",
        "tracksound" => "Suara roda-rel",
        "bogie" => "Ganti bogie",
        "interior" => "Ganti interior",
        "pantograph" => "Ganti pantograf",
        "enginespec" => "Ganti spesifikasi mesin",
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
        return new TaskCard(title, caption, Labels.Support(support), reason, new RelayCommand(() => _actions.OpenRole(role)));
    }

    /// <summary>Nama aset dari indeks bila ada (revisi apa pun); bila tidak, KUID apa adanya.</summary>
    internal string NameOf(Kuid kuid) =>
        _session.Library.Search(new LibraryQuery(kuid.ToString(), Limit: 1)).FirstOrDefault()?.Name ?? kuid.ToString();
}
