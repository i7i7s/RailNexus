using System.Collections.ObjectModel;
using System.Globalization;
using RailNexus.Core;
using RailNexus.Core.Library;
using RailNexus.Core.Roles;
using RailNexus.Core.Tx;

namespace RailNexus.App.Core.ViewModels;

public sealed record HistoryFile(string Path, string Change);

public sealed record HistoryStep(string Time, string Phase, string Note);

/// <summary>Satu pekerjaan di riwayat (PRD 21.5): waktu, aset, perubahan, hasil, ukuran backup.</summary>
public sealed class HistoryRow
{
    private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");

    public HistoryRow(TxManifest manifest, IReadOnlyList<JournalEntry> journal, string assetName, long backupBytes, string? undoneBy)
    {
        Manifest = manifest;
        Journal = journal;
        AssetName = assetName;
        BackupBytes = backupBytes;
        UndoneBy = undoneBy;
        var last = journal.Count > 0 ? journal[^1] : null;
        IsClosed = last?.Phase == TxPhase.Closed;
        FinalStatus = IsClosed && Enum.TryParse<TxStatus>(last!.Note, out var s) ? s : null;
    }

    public TxManifest Manifest { get; }
    public IReadOnlyList<JournalEntry> Journal { get; }
    public string TxId => Manifest.TxId;
    public Kuid Kuid => Manifest.Kuid;
    public string AssetName { get; }
    public string Purpose => Manifest.Purpose;
    public string KindLabel => Manifest.Kind == "undo" ? "Undo" : "Perubahan";
    public string StartedAt => Manifest.StartedAt.ToLocalTime().ToString("d MMM yyyy HH.mm.ss", Id);
    public bool IsClosed { get; }
    public TxStatus? FinalStatus { get; }
    public long BackupBytes { get; }
    public string BackupLabel => BackupBytes == 0 ? "Tanpa backup berkas" : $"Backup {FormatSize(BackupBytes)}";
    /// <summary>Id transaksi undo yang sudah membatalkan pekerjaan ini.</summary>
    public string? UndoneBy { get; }

    public StatusLabel Status => !IsClosed ? new("Belum selesai · perlu rekonsiliasi", Tone.Warn)
        : FinalStatus is { } s ? Labels.Transaction(s)
        : new("Status tidak terbaca", Tone.Unknown);

    public IReadOnlyList<HistoryFile> Files => Manifest.Files
        .Select(f => new HistoryFile(f.RelativePath, (f.ExistedBefore, f.AfterHash) switch
        {
            (false, not null) => "Ditambahkan",
            (true, null) => "Dihapus",
            _ => "Diubah",
        })).ToList();

    public IReadOnlyList<HistoryStep> Steps => Journal
        .Select(j => new HistoryStep(j.At.ToLocalTime().ToString("HH.mm.ss", Id), j.Phase.ToString(), j.Note)).ToList();

    internal static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => string.Create(Id, $"{bytes / 1024.0:0.#} KB"),
        _ => string.Create(Id, $"{bytes / 1024.0 / 1024.0:0.#} MB"),
    };
}

/// <summary>
/// UI-24 Riwayat dan pemulihan: daftar pekerjaan, berkas yang berubah, backup, Undo sebagai
/// transaksi baru (AC-13/14), dan rekonsiliasi pekerjaan yang terputus (AC-11/12).
/// </summary>
public sealed class HistoryViewModel : ObservableObject, IPage
{
    private readonly AppSession _session;
    private readonly Kuid? _filter;
    private readonly Action? _back;
    private HistoryRow? _selected;
    private ApplyOutcome? _outcome;
    private bool _busy;

    public HistoryViewModel(AppSession session, Kuid? filter = null, Action? back = null)
    {
        _session = session;
        _filter = filter;
        _back = back;
        UndoCommand = new AsyncCommand(UndoAsync, () => UndoDisabledReason is null);
        ReconcileCommand = new AsyncCommand(() => ReconcileAsync(false), () => Selected is { IsClosed: false } && !_busy);
        DiscardPendingCommand = new AsyncCommand(() => ReconcileAsync(true), () => Selected is { IsClosed: false } && !_busy);
        Footer = new FooterAction("Batalkan perubahan terpilih", UndoCommand, () => UndoDisabledReason ?? $"Undo: {Selected!.Purpose}");
        Refresh();
    }

    public string Title => _filter is null ? "Riwayat" : "Riwayat aset ini";
    public FooterAction Footer { get; }
    public string? BackLabel => _back is null ? null : "Kembali ke Mau edit apa?";
    public void Back() => _back?.Invoke();

    public ObservableCollection<HistoryRow> Rows { get; } = new();
    public AsyncCommand UndoCommand { get; }
    public AsyncCommand ReconcileCommand { get; }
    public AsyncCommand DiscardPendingCommand { get; }
    public ApplyOutcome? Outcome { get => _outcome; private set => Set(ref _outcome, value); }

    public string? EmptyHint => Rows.Count > 0 ? null
        : _filter is null ? "Belum ada pekerjaan. Setiap Terapkan & Selesai dan Undo tercatat di sini."
        : "Belum ada pekerjaan untuk aset ini.";

    public HistoryRow? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            OnPropertyChanged(nameof(UndoDisabledReason));
            RefreshCommands();
        }
    }

    public string? UndoDisabledReason =>
        _busy ? "Sedang memproses…"
        : Selected is not { } r ? "Pilih pekerjaan dari daftar."
        : _session.WriteDisabledReason is { } w ? w
        : !r.IsClosed ? "Pekerjaan ini belum selesai. Jalankan rekonsiliasi dulu."
        : r.UndoneBy is { } by ? $"Perubahan ini sudah dibatalkan oleh pekerjaan {by}."
        : r.FinalStatus is not (TxStatus.Succeeded or TxStatus.SucceededWithWarnings) ? "Pekerjaan ini tidak menulis perubahan, jadi tidak ada yang dibatalkan."
        : r.Manifest.Files.Count == 0 ? "Pekerjaan ini tidak memiliki berkas untuk dipulihkan."
        : null;

    public void Refresh()
    {
        var keep = Selected?.TxId;
        var store = _session.Store;
        var manifests = store.AllTxIds().Select(store.LoadManifest).OfType<TxManifest>().ToList();
        var finals = manifests.ToDictionary(m => m.TxId, m => store.ReadJournal(m.TxId));
        // Undo yang berhasil menandai pekerjaan asalnya sebagai sudah dibatalkan.
        var undone = manifests
            .Where(m => m.UndoOf is not null && finals[m.TxId] is { Count: > 0 } j && j[^1].Phase == TxPhase.Closed
                        && j[^1].Note is nameof(TxStatus.Succeeded) or nameof(TxStatus.SucceededWithWarnings))
            .GroupBy(m => m.UndoOf!)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => finals[m.TxId][0].At).Last().TxId);

        Rows.Clear();
        foreach (var m in manifests.Where(m => _filter is null || References.SameAsset(m.Kuid, _filter.Value))
                     // Id hanya presisi detik; waktu entri jurnal pertama memisahkan pekerjaan dalam detik yang sama.
                     .OrderByDescending(m => finals[m.TxId] is { Count: > 0 } j ? j[0].At : m.StartedAt)
                     .ThenByDescending(m => m.TxId, StringComparer.Ordinal))
            Rows.Add(new HistoryRow(m, finals[m.TxId], NameOf(m.Kuid), store.BackupBytes(m.TxId), undone.GetValueOrDefault(m.TxId)));

        Selected = Rows.FirstOrDefault(r => r.TxId == keep) ?? Rows.FirstOrDefault();
        OnPropertyChanged(nameof(EmptyHint));
        OnPropertyChanged(nameof(UndoDisabledReason));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        UndoCommand.Refresh();
        ReconcileCommand.Refresh();
        DiscardPendingCommand.Refresh();
        Footer.Refresh();
    }

    private async Task UndoAsync()
    {
        if (Selected is not { } row || _session.Runner is not { } runner) return;
        await RunAsync(row.Kuid, () => runner.UndoAsync(row.TxId));
    }

    /// <param name="discardPendingEdit">Pilihan eksplisit: buang edit tertunda milik pekerjaan ini.</param>
    private async Task ReconcileAsync(bool discardPendingEdit)
    {
        if (Selected is not { IsClosed: false } row) return;
        // Rekonsiliasi hanya membaca keadaan dan membatalkan edit milik pekerjaan ini; butuh adapter yang sama.
        if (_session.Runner is not { } runner)
        {
            Outcome = new ApplyOutcome(new("Belum dapat diproses", Tone.Warn), _session.WriteDisabledReason!, null, []);
            return;
        }
        await RunAsync(row.Kuid, () => runner.ReconcileAsync(row.TxId, discardPendingEdit));
    }

    private async Task RunAsync(Kuid kuid, Func<Task<TxResult>> action)
    {
        _busy = true;
        RefreshCommands();
        try
        {
            var r = await action();
            var details = r.ConflictingFiles is { Count: > 0 } c
                ? new List<string> { "Diubah di luar transaksi asal (tidak ditimpa): " + string.Join(", ", c) }
                : new List<string>();
            Outcome = new ApplyOutcome(Labels.Transaction(r.Status), r.Message, r.TxId, details);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Outcome = new ApplyOutcome(new("Perlu tindakan", Tone.Err), $"Proses terhenti: {ex.Message}", null, []);
        }
        finally
        {
            _busy = false;
            // Isi aset mungkin berubah; draft memakai isi terbaru saat aset dibuka lagi.
            foreach (var (k, ws) in _session.Workspaces)
                if (References.SameAsset(k, kuid)) ws.InvalidateConfig();
            Refresh();
        }
    }

    private string NameOf(Kuid kuid) =>
        _session.Library.Search(new LibraryQuery(kuid.ToString(), Limit: 1)).FirstOrDefault()?.Name ?? kuid.ToString();
}
