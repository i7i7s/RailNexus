using System.Text;
using RailNexus.Core;
using RailNexus.Core.Tx;

namespace RailNexus.Tests;

public sealed class TransactionTests : IDisposable
{
    private static readonly Kuid Asset = new(999001, 1201, null);
    private const string Original = "kuid <kuid:999001:1201>\r\ndescription \"lama\"\r\n// komentar\r\n";
    private const string Changed = "kuid <kuid:999001:1201>\r\ndescription \"baru\"\r\n// komentar\r\n";

    private readonly SimulatedTrainz _sim = new();
    private readonly string _storeDir = Path.Combine(Path.GetTempPath(), "rn-store-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly TxStore _store;

    public TransactionTests()
    {
        _store = new TxStore(_storeDir);
        _sim.Install(Asset, new() { ["config.txt"] = Encoding.UTF8.GetBytes(Original) });
    }

    public void Dispose()
    {
        _sim.Dispose();
        try { Directory.Delete(_storeDir, recursive: true); } catch (IOException) { }
    }

    private TransactionRunner Runner(Func<string, long>? free = null) => new(_sim, _store, free);

    private static EditPlan Plan(string content = Changed, string? baseText = Original) => new(
        Asset, "uji", [new FileChange("config.txt", Encoding.UTF8.GetBytes(content))],
        new Dictionary<string, string?> { ["config.txt"] = baseText is null ? null : Hashing.Sha256(Encoding.UTF8.GetBytes(baseText)) });

    private string Installed() => Encoding.UTF8.GetString(_sim.InstalledFile(Asset, "config.txt")!);

    [Fact]
    public async Task Apply_then_undo_restores_exact_bytes_AC13()
    {
        var r = await Runner().ApplyAsync(Plan());
        Assert.Equal(TxStatus.Succeeded, r.Status);
        Assert.Equal(Changed, Installed());
        Assert.False(_sim.IsOpen(Asset));

        var u = await Runner().UndoAsync(r.TxId);
        Assert.Equal(TxStatus.Succeeded, u.Status);
        Assert.Equal(Original, Installed());
        Assert.Equal(r.TxId, _store.LoadManifest(u.TxId)!.UndoOf);
    }

    [Fact]
    public async Task Asset_already_open_for_edit_is_not_taken_over_AC09()
    {
        var external = _sim.OpenExternally(Asset);
        var r = await Runner().ApplyAsync(Plan());
        Assert.Equal(TxStatus.Blocked, r.Status);
        Assert.Equal(0, _sim.CommitCalls);
        Assert.Equal(0, _sim.RevertCalls);
        Assert.True(_sim.IsOpen(Asset));
        Assert.Equal(external, _sim.OpenFolderOf(Asset));
    }

    [Fact]
    public async Task File_changed_after_plan_stops_with_conflict_AC10()
    {
        _sim.ExternalEdit(Asset, "config.txt", Encoding.UTF8.GetBytes("diubah editor lain"));
        var r = await Runner().ApplyAsync(Plan());
        Assert.Equal(TxStatus.Conflict, r.Status);
        Assert.Contains("config.txt", r.ConflictingFiles!);
        Assert.Equal(0, _sim.CommitCalls);
        Assert.Equal("diubah editor lain", Installed());
        Assert.False(_sim.IsOpen(Asset));
    }

    [Fact]
    public async Task Commit_timeout_is_uncertain_and_not_retried_AC11()
    {
        _sim.Commit = CommitBehavior.TimeoutButApplied;
        var r = await Runner().ApplyAsync(Plan());
        Assert.Equal(TxStatus.Uncertain, r.Status);
        Assert.Equal(1, _sim.CommitCalls);
        Assert.Contains(r.TxId, _store.FindIncomplete());

        _sim.Commit = CommitBehavior.Accept;
        var rec = await Runner().ReconcileAsync(r.TxId);
        Assert.Equal(TxStatus.Succeeded, rec.Status);
        Assert.Equal(1, _sim.CommitCalls);
        Assert.Empty(_store.FindIncomplete());
    }

    [Fact]
    public async Task Commit_timeout_without_effect_reconciles_to_rolled_back()
    {
        _sim.Commit = CommitBehavior.TimeoutNotApplied;
        var r = await Runner().ApplyAsync(Plan());
        Assert.Equal(TxStatus.Uncertain, r.Status);

        // Trainz membuang edit yang menggantung; status tidak lagi terbuka.
        await _sim.RevertAsync(Asset);
        var rec = await Runner().ReconcileAsync(r.TxId);
        Assert.Equal(TxStatus.RolledBack, rec.Status);
        Assert.Equal(Original, Installed());
    }

    [Fact]
    public async Task Timeout_leaving_asset_open_needs_explicit_choice_then_rolls_back()
    {
        _sim.Commit = CommitBehavior.TimeoutNotApplied; // aset tetap terbuka, seperti uji nyata 100 ms
        var r = await Runner().ApplyAsync(Plan());
        Assert.Equal(TxStatus.Uncertain, r.Status);

        var first = await Runner().ReconcileAsync(r.TxId);
        Assert.Equal(TxStatus.NeedsAttention, first.Status);
        Assert.True(_sim.IsOpen(Asset));

        var second = await Runner().ReconcileAsync(r.TxId, discardPendingEdit: true);
        Assert.Equal(TxStatus.RolledBack, second.Status);
        Assert.False(_sim.IsOpen(Asset));
        Assert.Equal(Original, Installed());
        Assert.Equal(1, _sim.CommitCalls);
    }

    [Fact]
    public async Task Crash_after_write_before_submit_is_found_and_recovered_AC12()
    {
        // Susun keadaan seperti aplikasi mati setelah fase Written.
        var id = _store.NewTxId();
        _store.Append(id, TxPhase.Started, "edit: uji");
        _store.SaveManifest(new TxManifest(id, "edit", Asset, "uji", DateTimeOffset.UtcNow,
            [new SnapshotEntry("config.txt", Hashing.Sha256(Encoding.UTF8.GetBytes(Original)), Hashing.Sha256(Encoding.UTF8.GetBytes(Changed)), true)]));
        var folder = (await _sim.BeginEditAsync(Asset)).Value!;
        _store.Append(id, TxPhase.EditOpened, folder);
        File.WriteAllText(Path.Combine(folder, "config.txt"), Changed);
        _store.Append(id, TxPhase.Written);

        Assert.Contains(id, _store.FindIncomplete());
        var rec = await Runner().ReconcileAsync(id);
        Assert.Equal(TxStatus.RolledBack, rec.Status);
        Assert.False(_sim.IsOpen(Asset));
        Assert.Equal(Original, Installed());
        Assert.Equal(0, _sim.CommitCalls);
    }

    [Fact]
    public async Task Reconcile_does_not_revert_an_edit_it_never_opened()
    {
        var id = _store.NewTxId();
        _store.Append(id, TxPhase.Started, "edit: uji");
        _store.SaveManifest(new TxManifest(id, "edit", Asset, "uji", DateTimeOffset.UtcNow, []));
        _sim.OpenExternally(Asset);

        await Runner().ReconcileAsync(id);
        Assert.True(_sim.IsOpen(Asset));
        Assert.Equal(0, _sim.RevertCalls);
    }

    [Fact]
    public async Task Undo_after_external_edit_shows_conflict_and_keeps_external_change_AC14()
    {
        var r = await Runner().ApplyAsync(Plan());
        _sim.ExternalEdit(Asset, "config.txt", Encoding.UTF8.GetBytes("edit eksternal"));

        var u = await Runner().UndoAsync(r.TxId);
        Assert.Equal(TxStatus.Conflict, u.Status);
        Assert.Equal("edit eksternal", Installed());
    }

    [Fact]
    public async Task Not_enough_disk_for_snapshot_writes_nothing_AC22()
    {
        var r = await Runner(free: _ => 1024).ApplyAsync(Plan());
        Assert.Equal(TxStatus.Blocked, r.Status);
        Assert.Equal(0, _sim.CommitCalls);
        Assert.Equal(Original, Installed());
        Assert.False(_sim.IsOpen(Asset));
    }

    [Fact]
    public async Task Rejected_commit_rolls_back()
    {
        _sim.Commit = CommitBehavior.Reject;
        var r = await Runner().ApplyAsync(Plan());
        Assert.Equal(TxStatus.RolledBack, r.Status);
        Assert.Equal(Original, Installed());
        Assert.False(_sim.IsOpen(Asset));
    }

    [Fact]
    public async Task Faulty_after_submit_is_not_reported_as_plain_success()
    {
        _sim.Faulty.Add(Asset);
        var r = await Runner().ApplyAsync(Plan());
        Assert.Equal(TxStatus.SucceededWithWarnings, r.Status);
        Assert.NotNull(r.Validation);
        Assert.True(r.Validation!.HasErrors);
    }

    private void FaultyWith(params string[] errors)
    {
        _sim.Faulty.Add(Asset);
        _sim.Errors[Asset] = errors.ToList();
    }

    [Fact]
    public async Task Existing_fault_unchanged_is_not_blamed_on_the_change()
    {
        FaultyWith("VE176: a");
        var r = await Runner().ApplyAsync(Plan());
        Assert.Equal(TxStatus.SucceededWithWarnings, r.Status);
        Assert.Contains("tidak menambah", r.Message);
        Assert.NotNull(r.ValidationBefore);
    }

    [Fact]
    public async Task New_fault_suggests_undo()
    {
        FaultyWith("VE1: lama");
        _sim.ErrorsAfterCommit[Asset] = ["VE1: lama", "VE2: baru"];
        var r = await Runner().ApplyAsync(Plan());
        Assert.Contains("menambah 1 masalah baru", r.Message);
    }

    [Fact]
    public async Task Partial_repair_is_reported_as_partial_not_healthy_AC24()
    {
        FaultyWith("VE1", "VE2", "VE3", "VE4");
        _sim.ErrorsAfterCommit[Asset] = ["VE3", "VE4"];
        var r = await Runner().ApplyAsync(Plan());
        Assert.Equal(TxStatus.SucceededWithWarnings, r.Status);
        Assert.Contains("Perbaikan sebagian: 2 dari 4", r.Message);
    }

    [Fact]
    public async Task Full_repair_is_success()
    {
        FaultyWith("VE176: a");
        _sim.ErrorsAfterCommit[Asset] = [];
        var r = await Runner().ApplyAsync(Plan());
        Assert.Equal(TxStatus.Succeeded, r.Status);
        Assert.Contains("masalah lama selesai", r.Message);
    }

    [Fact]
    public async Task Trainz_not_running_blocks_before_anything()
    {
        _sim.Running = false;
        var r = await Runner().ApplyAsync(Plan());
        Assert.Equal(TxStatus.Blocked, r.Status);
        Assert.Equal(TxPhase.Closed, _store.LastPhase(r.TxId));
    }

    [Fact]
    public async Task Created_file_is_removed_on_undo()
    {
        var plan = new EditPlan(Asset, "tambah berkas",
            [new FileChange("baru.texture.txt", Encoding.UTF8.GetBytes("Primary=baru.tga"))],
            new Dictionary<string, string?> { ["baru.texture.txt"] = null });
        var r = await Runner().ApplyAsync(plan);
        Assert.Equal(TxStatus.Succeeded, r.Status);
        Assert.NotNull(_sim.InstalledFile(Asset, "baru.texture.txt"));

        var u = await Runner().UndoAsync(r.TxId);
        Assert.Equal(TxStatus.Succeeded, u.Status);
        Assert.Null(_sim.InstalledFile(Asset, "baru.texture.txt"));
    }

    [Fact]
    public async Task Path_escaping_asset_folder_is_rejected()
    {
        var plan = new EditPlan(Asset, "jahat", [new FileChange("..\\luar.txt", [1])]);
        await Assert.ThrowsAsync<ArgumentException>(() => Runner().ApplyAsync(plan));
    }
}
