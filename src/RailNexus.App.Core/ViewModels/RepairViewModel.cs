using System.Collections.ObjectModel;
using System.Windows.Input;
using RailNexus.Core;
using RailNexus.Core.Dependencies;
using RailNexus.Core.Library;
using RailNexus.Core.Roles;
using RailNexus.Core.Texture;

namespace RailNexus.App.Core.ViewModels;

public sealed record ProblemActionItem(string Label, ICommand? Command, string? DisabledReason);

public sealed class ProblemCard(Problem problem, IReadOnlyList<ProblemActionItem> actions)
{
    public Problem Problem => problem;
    public string Title => problem.Title;
    public string Cause => problem.Cause;
    public string Evidence => problem.Evidence;
    public string Impact => problem.Impact;
    public IReadOnlyList<ProblemActionItem> Actions => actions;
    public StatusLabel Class => problem.Class switch
    {
        ProblemClass.NotInstalled => new("Komponen belum tersedia", Tone.Warn),
        ProblemClass.InstalledFaulty => new("Terpasang tetapi bermasalah", Tone.Err),
        ProblemClass.Inconsistent => new("Config/berkas tidak konsisten", Tone.Info),
        _ => new("Belum dipahami mesin perbaikan", Tone.Unknown),
    };
}

/// <summary>
/// UI-13 Pusat Perbaikan (PRD 12): kumpulkan kondisi aset, dependency, config, berkas, dan laporan
/// validator → kartu masalah per penyebab → tindakan yang tersedia masuk draft. Tidak menjanjikan
/// semua faulty sembuh; hasil nyata datang dari validasi setelah Terapkan & Selesai.
/// </summary>
public sealed class RepairViewModel : ObservableObject, IPage
{
    private readonly AppSession _session;
    private readonly AssetWorkspace _workspace;
    private readonly AssetNavigation _nav;
    private ValidationReport? _validation;
    private string? _validationNote;
    private string? _message;
    private bool _checking;

    public RepairViewModel(AppSession session, AssetWorkspace workspace, FooterAction footer, AssetNavigation nav)
    {
        _session = session;
        _workspace = workspace;
        _nav = nav;
        Footer = footer;
        CheckCommand = new AsyncCommand(CheckAsync);
        Build();
    }

    public string Title => "Periksa & perbaiki";
    public FooterAction Footer { get; }
    public string? BackLabel => "Kembali ke Mau edit apa?";
    public void Back() => _nav.Back();

    public ObservableCollection<ProblemCard> Cards { get; } = new();
    public AsyncCommand CheckCommand { get; }
    public string? Message { get => _message; private set => Set(ref _message, value); }
    public bool IsChecking { get => _checking; private set => Set(ref _checking, value); }

    public string Summary => _workspace.Document is null ? (_workspace.ReadError ?? "Isi aset belum terbaca.")
        : Cards.Count == 0 ? "Tidak ditemukan masalah pada pemeriksaan ini." + (_validation is null ? " Validator Trainz belum dijalankan." : "")
        : $"{Cards.Count} masalah ditemukan." + (_validation is null ? " Validator Trainz belum dijalankan." : "");

    public string? ValidationNote => _validationNote;

    private void Build()
    {
        Cards.Clear();
        var doc = _workspace.Document;
        var refs = doc is null ? [] : DependencyInventory.Collect(doc, _workspace.Entry.Kuid);
        var files = _workspace.Snapshot is { } snap ? TextureInventory.Scan(snap, doc) : [];
        foreach (var p in Diagnosis.Diagnose(_workspace.Entry.Kuid, _workspace.Entry.MissingDependencies, doc, refs,
                     _session.TargetState, _validation, files))
            Cards.Add(new ProblemCard(p, p.Actions.Select(Action).ToList()));
        OnPropertyChanged(nameof(Summary));
    }

    private ProblemActionItem Action(RepairAction a)
    {
        switch (a.Kind)
        {
            case RepairActionKind.ChooseReplacement when RoleCatalog.ById(a.RoleId ?? "") is { } role:
                return new(a.Label, new RelayCommand(() => _nav.OpenRole(role)), null);
            case RepairActionKind.OpenTarget:
                return a.Target is { } t && _session.Find(t) is { } entry
                    ? new(a.Label, new RelayCommand(() => _nav.OpenAsset(entry)), null)
                    : new(a.Label, null, "Target belum ada di indeks pustaka.");
            case RepairActionKind.AddKuidTableEntry when a.Target is { } k:
                var inDraft = _workspace.Draft.Items.OfType<DraftItem.AddKuidTableEntry>().Any(i => References.SameAsset(i.Kuid, k));
                return inDraft
                    ? new("Sudah ada di draft", null, "Perubahan ini sudah masuk draft.")
                    : new(a.Label, new RelayCommand(() =>
                    {
                        _workspace.Draft.Put(new DraftItem.AddKuidTableEntry(k, "Perbaikan", $"{a.RuleId}: tambahkan {k} ke kuid-table"));
                        _workspace.NotifyDraftChanged();
                        Message = $"{k} masuk draft. Terapkan & Selesai akan menulis dan memvalidasi ulang.";
                        Build();
                    }), null);
            case RepairActionKind.OpenTextureWorkshop:
                return new(a.Label, new RelayCommand(_nav.OpenTexture), null);
            case RepairActionKind.OpenEditor:
                return new(a.Label, new RelayCommand(_nav.OpenEditor), null);
            default:
                return new(a.Label, null, "Tindakan ini belum tersedia.");
        }
    }

    /// <summary>Baca ulang isi aset dan jalankan validator Trainz (operasi baca yang sudah terverifikasi).</summary>
    private async Task CheckAsync()
    {
        IsChecking = true;
        try
        {
            var snap = await _session.AssetReader.ReadSnapshotAsync(_workspace.Entry.Kuid);
            if (snap.IsSuccess) _workspace.SetSnapshot(snap.Value!);
            else _workspace.SetReadError(snap.Message);

            if (_session.Adapter is { } adapter)
            {
                var v = await adapter.ValidateAsync(_workspace.Entry.Kuid);
                _validation = v.IsSuccess ? v.Value : null;
                _validationNote = v.IsSuccess
                    ? v.Value!.Errors.Count == 0 && v.Value.Warnings.Count == 0 ? "Validator Trainz: tidak ada masalah."
                      : $"Validator Trainz: {v.Value.Errors.Count} error, {v.Value.Warnings.Count} peringatan."
                    : $"Validator Trainz belum dapat dijalankan: {v.Message}";
            }
            else _validationNote = "Validator tidak tersedia pada sambungan ini.";
            OnPropertyChanged(nameof(ValidationNote));
            Message = snap.IsSuccess ? null : snap.Message;
        }
        finally { IsChecking = false; }
        Build();
    }
}

/// <summary>Perbaikan tanpa aset aktif: daftar aset bermasalah dari pustaka sebagai titik masuk (PRD 12.1 langkah 1).</summary>
public sealed class RepairListViewModel : ObservableObject, IPage
{
    public RepairListViewModel(AppSession session, Action<LibraryEntry> openRepair)
    {
        foreach (var e in session.Library.Search(new LibraryQuery(Filter: LibraryFilter.Faulty, Limit: 200))
                     .Concat(session.Library.Search(new LibraryQuery(Filter: LibraryFilter.MissingDependencies, Limit: 200)))
                     .DistinctBy(e => e.Kuid))
            Rows.Add(new AssetRow(e));
        OpenCommand = new RelayCommand(() => { if (Selected is { } s) openRepair(s.Entry); }, () => Selected is not null);
        Footer = new FooterAction("Periksa aset ini", OpenCommand, () => Selected is null ? "Pilih aset bermasalah dari daftar." : Selected.Name);
    }

    private AssetRow? _selected;
    public string Title => "Perbaikan";
    public FooterAction Footer { get; }
    public string? BackLabel => null;
    public void Back() { }
    public ObservableCollection<AssetRow> Rows { get; } = new();
    public RelayCommand OpenCommand { get; }
    public string? EmptyHint => Rows.Count > 0 ? null : "Indeks tidak mencatat aset bermasalah atau komponen yang belum tersedia.";

    public AssetRow? Selected
    {
        get => _selected;
        set { if (Set(ref _selected, value)) { OpenCommand.Refresh(); Footer.Refresh(); } }
    }
}
