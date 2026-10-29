using System.Collections.ObjectModel;
using RailNexus.Core;
using RailNexus.Core.Library;
using RailNexus.Core.Roles;

namespace RailNexus.App.Core.ViewModels;

public sealed class SlotItem(RoleSlot slot, string currentName)
{
    public RoleSlot Slot => slot;
    public string Label => slot.Label;
    public string Current => currentName;
    public string CurrentKuid => slot.Current.ToString();
    public string Location => slot.Path;
    /// <summary>Nilai di sekitar slot yang dipertahankan, mis. orientasi atau prioritas.</summary>
    public string? Context => slot.Context.Count == 0 ? null
        : "Dipertahankan: " + string.Join(", ", slot.Context.Select(kv => $"{kv.Key} {kv.Value}"));
}

public sealed class CandidateItem(LibraryEntry entry, CandidateCheck check)
{
    public LibraryEntry Entry => entry;
    public string Name => entry.Name;
    public string KuidText => entry.Kuid.ToString();
    public StatusLabel AssetStatus => Labels.Asset(entry.State);
    public StatusLabel Verdict => Labels.Candidate(check.Verdict);
    public string Reason => check.Reason;
    public bool IsSelectable => check.Verdict != CandidateVerdict.Rejected;
}

/// <summary>
/// Ganti komponen untuk satu peran (bentuk minimal UI-04/05/07/08): pilih slot → cari kandidat
/// lokal → tambahkan ke draft. Kandidat yang tidak sesuai tetap tampil dengan alasannya (PRD 9.3).
/// </summary>
public sealed class ReplaceViewModel : ObservableObject, IPage, IDisposable
{
    private readonly AppSession _session;
    private readonly AssetWorkspace _workspace;
    private readonly Action _back;
    private SlotItem? _slot;
    private CandidateItem? _candidate;
    private string _searchText = "";
    private string? _error;

    private readonly IAudioPlayer _player;
    private string? _previewStatus;

    public ReplaceViewModel(AppSession session, AssetWorkspace workspace, IRole role, FooterAction footer, Action back, IAudioPlayer? player = null)
    {
        _player = player ?? new NullAudioPlayer();
        PreviewCurrentCommand = new AsyncCommand(() => PreviewAsync(SelectedSlot?.Slot.Current, "sekarang"), () => CanPreview && SelectedSlot is not null);
        PreviewCandidateCommand = new AsyncCommand(() => PreviewAsync(SelectedCandidate?.Entry.Kuid, "pengganti"), () => CanPreview && SelectedCandidate is not null);
        StopPreviewCommand = new RelayCommand(() => { _player.Stop(); PreviewStatus = "Dihentikan."; });
        _session = session;
        _workspace = workspace;
        _back = back;
        Role = role;
        Footer = footer;
        AddCommand = new RelayCommand(Add, () => AddDisabledReason is null);
        ReadKindsCommand = new AsyncCommand(ReadKindsAsync, () => _session.Indexer is not null && UnknownKind.Any());
        if (workspace.Document is { } doc)
            foreach (var s in role.Locate(doc)) Slots.Add(new SlotItem(s, NameOf(s.Current)));
        SelectedSlot = Slots.FirstOrDefault();
    }

    public IRole Role { get; }

    /// <summary>Preview A/B untuk peran suara (PRD 10.2/10.3); peran lain tidak menampilkan pemutar.</summary>
    public bool CanPreview => Role.Id is "hornsound" or "enginesound";
    public AsyncCommand PreviewCurrentCommand { get; }
    public AsyncCommand PreviewCandidateCommand { get; }
    public RelayCommand StopPreviewCommand { get; }
    public string? PreviewStatus { get => _previewStatus; private set => Set(ref _previewStatus, value); }

    public void Dispose() => _player.Stop();

    /// <summary>
    /// Membaca berkas suara aset target lalu memutar sampel pertama. Bila tidak dapat dibaca, keterbatasan
    /// itu ditampilkan; penggantian tetap dapat dilakukan tanpa preview (PRD 10.2).
    /// </summary>
    private async Task PreviewAsync(Kuid? kuid, string label)
    {
        _player.Stop();
        if (kuid is not { } k) return;
        if (_session.Find(k) is not { } entry) { PreviewStatus = "Aset ini belum ada di indeks; suaranya tidak dapat dibaca."; return; }
        var ws = _session.Workspace(entry);
        if (ws.Snapshot is null)
        {
            var r = await _session.AssetReader.ReadSnapshotAsync(entry.Kuid);
            if (!r.IsSuccess) { PreviewStatus = $"Suara {entry.Name} belum dapat dibaca untuk preview: {r.Message} Penggantian tetap dapat dilakukan."; return; }
            ws.SetSnapshot(r.Value!);
        }
        var wav = ws.Snapshot!.Files.FirstOrDefault(f => f.RelativePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) && ws.Snapshot.IsRetained(f.RelativePath));
        if (wav is null) { PreviewStatus = $"{entry.Name} tidak memiliki berkas WAV yang dapat diputar."; return; }
        var bytes = ws.Snapshot.Read(wav.RelativePath)!;
        var info = RailNexus.Core.Audio.WavInfo.Read(bytes);
        if (!info.Ok) { PreviewStatus = $"{wav.RelativePath}: {info.Error}"; return; }
        var error = _player.Play($"{entry.Name} ({label})", bytes, 0.8);
        PreviewStatus = error ?? $"Memutar sampel {wav.RelativePath} dari {entry.Name} ({label}). " +
            (Role.Id == "enginesound" ? "Satu sampel, bukan simulasi seluruh rentang RPM." : "Bukan simulasi suara di Trainz.");
    }
    public string Title => Role.Label;
    public string CommunityTerm => Role.CommunityTerm;
    public FooterAction Footer { get; }
    public string? BackLabel => "Kembali ke Mau edit apa?";
    public void Back() => _back();

    public ObservableCollection<SlotItem> Slots { get; } = new();
    public ObservableCollection<CandidateItem> Candidates { get; } = new();
    public RelayCommand AddCommand { get; }
    public AsyncCommand ReadKindsCommand { get; }
    private string? _kindStatus;
    public string? KindStatus { get => _kindStatus; private set => Set(ref _kindStatus, value); }

    /// <summary>Kandidat yang ditolak hanya karena KIND belum terbaca.</summary>
    private IEnumerable<CandidateItem> UnknownKind =>
        Role.CandidateKinds.Count == 0 ? [] : Candidates.Where(c => c.Entry.Kind is null && c.Entry.State == AssetState.Installed);
    public string? Error { get => _error; private set => Set(ref _error, value); }

    public SlotItem? SelectedSlot
    {
        get => _slot;
        set
        {
            if (!Set(ref _slot, value)) return;
            RefreshCandidates();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set { if (Set(ref _searchText, value ?? "")) RefreshCandidates(); }
    }

    public CandidateItem? SelectedCandidate
    {
        get => _candidate;
        set
        {
            if (!Set(ref _candidate, value)) return;
            OnPropertyChanged(nameof(AddDisabledReason));
            AddCommand.Refresh();
            PreviewCandidateCommand.Refresh();
        }
    }

    public string? AddDisabledReason =>
        SelectedSlot is null ? "Pilih lokasi yang ingin diganti."
        : SelectedCandidate is null ? "Pilih pengganti dari daftar."
        : !SelectedCandidate.IsSelectable ? SelectedCandidate.Reason
        : null;

    public string? EmptyHint => Candidates.Count > 0 ? null
        : SearchText.Trim().Length == 0 ? "Ketik nama atau tempel KUID pengganti untuk mencari di pustaka lokal."
        : "Tidak ada aset yang cocok di pustaka lokal.";

    private void RefreshCandidates()
    {
        Candidates.Clear();
        SelectedCandidate = null;
        if (SelectedSlot is { } slot && SearchText.Trim().Length > 0)
        {
            foreach (var e in _session.Library.Search(new LibraryQuery(SearchText, Limit: 50)))
            {
                if (References.SameAsset(e.Kuid, _workspace.Entry.Kuid)) continue;
                Candidates.Add(new CandidateItem(e, Evaluate(slot.Slot, e)));
            }
        }
        OnPropertyChanged(nameof(EmptyHint));
        ReadKindsCommand?.Refresh();
    }

    /// <summary>Membaca KIND kandidat dari config-nya (printconfig) lalu menilai ulang (LIB-01).</summary>
    private async Task ReadKindsAsync()
    {
        if (_session.Indexer is not { } indexer) return;
        var targets = UnknownKind.Take(20).Select(c => c.Entry.Kuid).ToList();
        var failed = new List<string>();
        foreach (var k in targets)
        {
            var r = await indexer.ReadKindAsync(k);
            if (!r.IsSuccess) failed.Add(r.Message);
        }
        KindStatus = failed.Count == 0 ? $"Jenis {targets.Count} kandidat terbaca."
            : $"{targets.Count - failed.Count} terbaca, {failed.Count} belum: {failed.Distinct().First()}";
        RefreshCandidates();
    }

    /// <summary>
    /// Indeks pustaka belum menyimpan KIND aset (kategori Trainz bukan KIND). Untuk peran yang
    /// mensyaratkan KIND tertentu, kandidat tidak ditawarkan sampai jenisnya terbaca (AC-04).
    /// </summary>
    private CandidateCheck Evaluate(RoleSlot slot, LibraryEntry e)
    {
        var record = new AssetRecord(e.Kuid, e.Name, e.Kind ?? "", e.State);
        var check = ModificationEngine.Evaluate(Role, slot, record);
        if (e.Kind is null && Role.CandidateKinds.Count > 0 && check.Verdict == CandidateVerdict.Rejected && check.Reason.StartsWith("Jenis berbeda", StringComparison.Ordinal))
            return check with { Reason = $"Jenis aset belum terbaca di indeks; kecocokan dengan {string.Join(" / ", Role.CandidateKinds)} belum dapat dipastikan." };
        return check;
    }

    private void Add()
    {
        if (SelectedSlot is not { } slot || SelectedCandidate is not { IsSelectable: true } c || _workspace.ConfigText is null) return;
        var item = new DraftItem.ReplaceKuid(Role.Id, slot.Slot.Path, slot.Slot.Current, c.Entry.Kuid, "Ganti komponen",
            $"{Role.Label} · {slot.Label}: {slot.Current} → {c.Name}");
        var previous = _workspace.Draft.Items.ToList();
        _workspace.Draft.Put(item);

        // Draft diperiksa langsung agar kegagalan terlihat sekarang, bukan saat menerapkan.
        var build = _workspace.Draft.Build(_workspace.ConfigText);
        if (!build.Ok)
        {
            _workspace.Draft.Clear();
            foreach (var p in previous) _workspace.Draft.Put(p);
            Error = build.Error;
            return;
        }
        Error = null;
        _workspace.NotifyDraftChanged();
        _back();
    }

    private string NameOf(Kuid kuid) =>
        _session.Library.Search(new LibraryQuery(kuid.ToString(), Limit: 1)).FirstOrDefault()?.Name ?? kuid.ToString();
}
