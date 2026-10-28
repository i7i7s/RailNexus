using System.Collections.ObjectModel;
using RailNexus.Core.Roles;
using RailNexus.Core.Tx;

namespace RailNexus.App.Core.ViewModels;

public sealed class DraftEntry(DraftItem item, RelayCommand remove)
{
    public DraftItem Item => item;
    public string Module => item.Module;
    public string Summary => item.Summary;
    public RelayCommand RemoveCommand => remove;
}

/// <summary>Hasil penerapan terakhir (UI-23 ringkas): status berlabel teks, pesan, dan id transaksi.</summary>
public sealed record ApplyOutcome(StatusLabel Status, string Message, string? TxId, IReadOnlyList<string> Details);

/// <summary>
/// Ringkasan perubahan di panel kanan (PRD 6.1) untuk aset aktif. Draft bersama lintas modul
/// (PRD 9.5) diterapkan sebagai satu transaksi lewat <see cref="TransactionRunner"/>.
/// </summary>
public sealed class DraftPanelViewModel : ObservableObject
{
    private readonly AppSession _session;
    private AssetWorkspace? _workspace;
    private DraftBuild? _preview;
    private ApplyOutcome? _outcome;

    public DraftPanelViewModel(AppSession session)
    {
        _session = session;
        ApplyCommand = new AsyncCommand(ApplyAsync, () => ApplyDisabledReason is null);
        Footer = new FooterAction("Terapkan & Selesai", ApplyCommand, () => ApplyDisabledReason ?? CountLabel);
    }

    public FooterAction Footer { get; }
    public AsyncCommand ApplyCommand { get; }

    /// <summary>Transaksi selesai dijalankan (apa pun hasilnya); isi aset mungkin perlu dibaca ulang.</summary>
    public event EventHandler? Applied;
    public ObservableCollection<DraftEntry> Items { get; } = new();
    public ObservableCollection<string> Handled { get; } = new();
    public ObservableCollection<string> Uncertain { get; } = new();

    public AssetWorkspace? Workspace
    {
        get => _workspace;
        set
        {
            if (_workspace == value) return;
            if (_workspace is not null) _workspace.Changed -= OnWorkspaceChanged;
            _workspace = value;
            if (_workspace is not null) _workspace.Changed += OnWorkspaceChanged;
            Outcome = null;
            OnPropertyChanged();
            Reload();
        }
    }

    public ApplyOutcome? Outcome { get => _outcome; private set => Set(ref _outcome, value); }
    public string? PreviewError => _preview is { Ok: false } p ? p.Error : null;
    public bool IsEmpty => Items.Count == 0;

    public string CountLabel => Items.Count switch
    {
        0 => "Belum ada perubahan",
        1 => "1 perubahan siap",
        var n => $"{n} perubahan siap",
    };

    public string Scope => "Aset terdampak: hanya aset ini";
    public string Backup => "Backup: dibuat otomatis sebelum menulis";
    public string AfterApply => "Setelah selesai: submit lokal dan pemeriksaan ulang";

    /// <summary>Alasan tombol utama nonaktif (PRD 6.3); null bila dapat diterapkan.</summary>
    public string? ApplyDisabledReason =>
        ApplyCommand.IsRunning ? "Sedang menerapkan…"
        : _workspace is null ? "Pilih aset terlebih dahulu."
        : Items.Count == 0 ? "Belum ada perubahan di draft."
        : _session.WriteDisabledReason is { } w ? w
        : _workspace.ConfigBytes is null ? "Isi aset belum terbaca; buka ulang aset untuk membacanya."
        : PreviewError is { } e ? e
        : null;

    private void OnWorkspaceChanged(object? sender, EventArgs e) => Reload();

    private void Reload()
    {
        Items.Clear();
        Handled.Clear();
        Uncertain.Clear();
        _preview = null;
        if (_workspace is { } ws)
        {
            foreach (var item in ws.Draft.Items)
                Items.Add(new DraftEntry(item, new RelayCommand(() => { ws.Draft.Remove(item); ws.NotifyDraftChanged(); })));
            if (ws.ConfigText is not null && Items.Count > 0)
            {
                _preview = ws.Draft.Build(ws.ConfigText);
                foreach (var h in _preview.Handled) Handled.Add(h);
                foreach (var u in _preview.Uncertain) Uncertain.Add(u);
            }
        }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CountLabel));
        OnPropertyChanged(nameof(PreviewError));
        OnPropertyChanged(nameof(ApplyDisabledReason));
        ApplyCommand.Refresh();
        Footer.Refresh();
    }

    private async Task ApplyAsync()
    {
        if (_workspace is not { ConfigBytes: { } original } ws || _session.Runner is not { } runner) return;
        Footer.Refresh();
        var (plan, build) = ws.Draft.ToEditPlan(original, string.Join("; ", ws.Draft.Items.Select(i => i.Summary)));
        if (plan is null)
        {
            Outcome = new ApplyOutcome(new("Belum diterapkan", Tone.Warn), build.Error, null, []);
            Footer.Refresh();
            return;
        }

        TxResult r;
        try { r = await runner.ApplyAsync(plan); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Outcome = new ApplyOutcome(new("Perlu tindakan", Tone.Err), $"Penerapan terhenti: {ex.Message}", null, []);
            Footer.Refresh();
            return;
        }

        var details = new List<string>();
        if (r.ConflictingFiles is { Count: > 0 } c) details.Add("Berkas berubah: " + string.Join(", ", c));
        if (r.Validation is { } v)
        {
            details.AddRange(v.Errors.Select(e => "Masalah: " + e));
            details.AddRange(v.Warnings.Select(w => "Peringatan: " + w));
        }
        Outcome = new ApplyOutcome(Labels.Transaction(r.Status), r.Message, r.TxId, details);

        // Isi aset berubah (atau mungkin berubah); config dibaca ulang sebelum perubahan berikutnya.
        if (r.Status is TxStatus.Succeeded or TxStatus.SucceededWithWarnings)
        {
            ws.Draft.Clear();
            ws.InvalidateConfig();
        }
        else if (r.Status is TxStatus.Conflict or TxStatus.Uncertain or TxStatus.NeedsAttention)
        {
            ws.InvalidateConfig();
        }
        Footer.Refresh();
        Applied?.Invoke(this, EventArgs.Empty);
    }
}
