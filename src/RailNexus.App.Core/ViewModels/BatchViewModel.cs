using System.Collections.ObjectModel;
using RailNexus.Core;
using RailNexus.Core.Batch;
using RailNexus.Core.Library;
using RailNexus.Core.Roles;
using RailNexus.Core.Tx;

namespace RailNexus.App.Core.ViewModels;

public sealed class SelectableAsset(LibraryEntry entry) : ObservableObject
{
    private bool _selected;
    public LibraryEntry Entry => entry;
    public string Name => entry.Name;
    public string KuidText => entry.Kuid.ToString();
    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }
}

public sealed record BatchRow(string Name, string Kuid, StatusLabel Status, string Detail);

/// <summary>
/// UI-22 Batch & preset (PRD 20, RN-012): pilih aset → pilih maksud (peran + aset lama → baru) →
/// tinjau siap/perlu pilihan/dilewati → jalankan satu per satu → hasil per aset (AC-15).
/// </summary>
public sealed class BatchViewModel : ObservableObject, IPage
{
    private readonly AppSession _session;
    private readonly string _presetFolder;
    private string _search = "";
    private string _roleId = "hornsound";
    private string _from = "";
    private string _to = "";
    private string _presetName = "Preset baru";
    private string? _message;
    private IReadOnlyList<BatchPlanItem> _plan = [];
    private CancellationTokenSource? _stop;

    public BatchViewModel(AppSession session, string presetFolder)
    {
        _session = session;
        _presetFolder = presetFolder;
        PreviewCommand = new AsyncCommand(PreviewAsync, () => PreviewDisabledReason is null);
        RunCommand = new AsyncCommand(RunAsync, () => RunDisabledReason is null);
        StopCommand = new RelayCommand(() => { _stop?.Cancel(); Message = "Antrean akan berhenti setelah aset yang sedang diproses selesai."; }, () => RunCommand.IsRunning);
        SavePresetCommand = new RelayCommand(SavePreset, () => Preset.Problem is null);
        LoadPresetCommand = new ParamCommand<string>(LoadPreset);
        Footer = new FooterAction("Jalankan pekerjaan", RunCommand, () => RunDisabledReason ?? PlanSummary);
        RefreshAssets();
        RefreshPresets();
    }

    public string Title => "Batch & preset";
    public FooterAction Footer { get; }
    public string? BackLabel => null;
    public void Back() { }

    public static IReadOnlyList<Option<string>> RoleOptions { get; } =
        RoleCatalog.All.Select(r => new Option<string>(r.Id, $"{r.Label} ({r.CommunityTerm})")).ToList();

    public ObservableCollection<SelectableAsset> Assets { get; } = new();
    public ObservableCollection<BatchRow> Rows { get; } = new();
    public ObservableCollection<string> Presets { get; } = new();
    public AsyncCommand PreviewCommand { get; }
    public AsyncCommand RunCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand SavePresetCommand { get; }
    public ParamCommand<string> LoadPresetCommand { get; }
    public string? Message { get => _message; private set => Set(ref _message, value); }

    public string SearchText { get => _search; set { if (Set(ref _search, value ?? "")) RefreshAssets(); } }
    public string RoleId { get => _roleId; set { if (Set(ref _roleId, value ?? "")) Invalidate(); } }
    public string From { get => _from; set { if (Set(ref _from, value ?? "")) Invalidate(); } }
    public string To { get => _to; set { if (Set(ref _to, value ?? "")) Invalidate(); } }
    public string PresetName { get => _presetName; set => Set(ref _presetName, value ?? ""); }

    public ReplacePreset Preset => new(PresetName, RoleId, From.Trim(), To.Trim());

    public string? PreviewDisabledReason =>
        !Assets.Any(a => a.IsSelected) ? "Pilih minimal satu aset."
        : Preset.Problem;

    public string? RunDisabledReason =>
        RunCommand?.IsRunning == true ? "Sedang berjalan…"
        : _session.WriteDisabledReason is { } w ? w
        : _plan.Count == 0 ? "Tinjau rencana terlebih dahulu."
        : !_plan.Any(p => p.State == BatchItemState.Ready) ? "Tidak ada aset yang siap diproses."
        : null;

    public string PlanSummary => _plan.Count == 0 ? "Belum ada rencana."
        : $"Siap {_plan.Count(p => p.State == BatchItemState.Ready)} · perlu pilihan {_plan.Count(p => p.State == BatchItemState.NeedsChoice)} · dilewati {_plan.Count(p => p.State == BatchItemState.Skipped)}";

    private void RefreshAssets()
    {
        var keep = Assets.Where(a => a.IsSelected).Select(a => a.Entry.Kuid).ToHashSet();
        Assets.Clear();
        foreach (var e in _session.Library.Search(new LibraryQuery(SearchText, Limit: 300)))
        {
            var a = new SelectableAsset(e) { IsSelected = keep.Contains(e.Kuid) };
            a.PropertyChanged += (_, _) => Invalidate();
            Assets.Add(a);
        }
        Invalidate();
    }

    /// <summary>Perubahan pilihan membuat rencana kedaluwarsa (PRD 22: rencana perlu ditinjau ulang).</summary>
    private void Invalidate()
    {
        _plan = [];
        Rows.Clear();
        OnPropertyChanged(nameof(PlanSummary));
        OnPropertyChanged(nameof(PreviewDisabledReason));
        OnPropertyChanged(nameof(RunDisabledReason));
        PreviewCommand?.Refresh();
        RunCommand?.Refresh();
        SavePresetCommand?.Refresh();
        Footer?.Refresh();
    }

    private async Task PreviewAsync()
    {
        var preset = Preset;
        var target = _session.Find(preset.ToKuid);
        var available = target?.State == AssetState.Installed;
        var items = new List<BatchPlanItem>();
        foreach (var a in Assets.Where(a => a.IsSelected).ToList())
        {
            var r = await _session.AssetReader.ReadSnapshotAsync(a.Entry.Kuid);
            items.Add(BatchPlanner.PlanReplace(a.Entry.Kuid, a.Name, r.IsSuccess ? r.Value!.Read("config.txt") : null,
                r.IsSuccess ? null : r.Message, preset, available));
        }
        _plan = items;
        Rows.Clear();
        foreach (var p in items)
            Rows.Add(new BatchRow(p.Name, p.Asset.ToString(), p.State switch
            {
                BatchItemState.Ready => new("Siap diproses", Tone.Info),
                BatchItemState.NeedsChoice => new("Perlu pilihan", Tone.Warn),
                _ => new("Dilewati", Tone.Mute),
            }, p.Detail));
        Message = PlanSummary;
        OnPropertyChanged(nameof(PlanSummary));
        OnPropertyChanged(nameof(RunDisabledReason));
        RunCommand.Refresh();
        Footer.Refresh();
    }

    private async Task RunAsync()
    {
        if (_session.Runner is not { } runner) return;
        _stop = new CancellationTokenSource();
        StopCommand.Refresh();
        Footer.Refresh();
        Rows.Clear();
        var results = await new BatchRunner(runner).RunAsync(_plan, null, _stop.Token);
        foreach (var r in results)
            Rows.Add(new BatchRow(r.Name, r.Asset.ToString(),
                r.Result is { } tx ? Labels.Transaction(tx.Status) : r.Planned == BatchItemState.Ready ? new("Tidak dijalankan", Tone.Mute)
                : r.Planned == BatchItemState.NeedsChoice ? new("Perlu pilihan", Tone.Warn) : new("Dilewati", Tone.Mute),
                r.Result is { } t ? $"{r.Message} (pekerjaan {t.TxId}; dapat dibatalkan dari Riwayat)" : r.Message));
        var ok = results.Count(r => r.Result?.Status is TxStatus.Succeeded or TxStatus.SucceededWithWarnings);
        Message = $"{ok} aset berhasil, {results.Count(r => r.Result is not null) - ok} tidak berhasil, {results.Count(r => r.Result is null)} tidak dijalankan. " +
                  "Batch bukan satu transaksi: keberhasilan tetap tercatat walau aset lain gagal.";
        foreach (var r in results) foreach (var (k, ws) in _session.Workspaces) if (References.SameAsset(k, r.Asset)) ws.InvalidateConfig();
        _plan = [];
        _stop = null;
        StopCommand.Refresh();
        OnPropertyChanged(nameof(RunDisabledReason));
        Footer.Refresh();
    }

    private void RefreshPresets()
    {
        Presets.Clear();
        if (!Directory.Exists(_presetFolder)) return;
        foreach (var f in Directory.EnumerateFiles(_presetFolder, "*.json").Order()) Presets.Add(Path.GetFileNameWithoutExtension(f));
    }

    private void SavePreset()
    {
        var safe = string.Concat(PresetName.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' ? '_' : c)).Trim();
        if (safe.Length == 0) { Message = "Beri nama preset."; return; }
        Preset.Save(Path.Combine(_presetFolder, safe + ".json"));
        Message = $"Preset '{safe}' tersimpan.";
        RefreshPresets();
    }

    /// <summary>Membuka preset pada profil ini; kandidat diselesaikan ulang saat rencana ditinjau (PRD 20).</summary>
    private void LoadPreset(string name)
    {
        var p = ReplacePreset.Load(Path.Combine(_presetFolder, name + ".json"));
        if (p is null) { Message = $"Preset '{name}' tidak dapat dibaca."; return; }
        PresetName = p.Name;
        RoleId = p.RoleId;
        From = p.From;
        To = p.To;
        Message = p.Problem ?? $"Preset '{p.Name}' dimuat. Tinjau rencana untuk memeriksa ketersediaan pengganti di profil ini.";
    }
}
