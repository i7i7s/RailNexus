namespace RailNexus.Core.Tx;

/// <summary>
/// Menjalankan satu rencana perubahan untuk satu aset melalui tahapan PRD 21.2:
/// periksa â†’ kunci â†’ buka edit â†’ verifikasi dasar â†’ snapshot â†’ tulis â†’ submit â†’ periksa â†’ tutup.
/// Setiap fase dicatat di jurnal sebelum langkah berikutnya dimulai.
/// </summary>
public sealed class TransactionRunner(ITrainzAdapter adapter, TxStore store, Func<string, long>? freeSpaceProvider = null)
{
    /// <summary>Ruang cadangan minimum di luar ukuran snapshot.</summary>
    public long SpareBytes { get; init; } = 16 * 1024 * 1024;

    private readonly Func<string, long> _freeSpace = freeSpaceProvider ?? DefaultFreeSpace;

    private static long DefaultFreeSpace(string path) =>
        new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace;

    public Task<TxResult> ApplyAsync(EditPlan plan, CancellationToken ct = default) =>
        RunAsync(plan, "edit", undoOf: null, ct);

    /// <summary>
    /// Undo membuat transaksi baru yang mengembalikan preimage. Berkas yang diubah di luar
    /// transaksi asal memicu konflik, bukan ditimpa (AC-13, AC-14).
    /// </summary>
    public async Task<TxResult> UndoAsync(string txId, CancellationToken ct = default)
    {
        var m = store.LoadManifest(txId);
        if (m is null) return new TxResult(txId, TxStatus.Blocked, "Manifest transaksi tidak ditemukan; undo tidak dapat dilakukan.");
        if (store.LastPhase(txId) != TxPhase.Closed)
            return new TxResult(txId, TxStatus.Blocked, "Transaksi asal belum selesai. Jalankan rekonsiliasi dulu.");

        var changes = m.Files
            .Select(f => new FileChange(f.RelativePath, f.ExistedBefore ? store.ReadPreimage(txId, f.RelativePath) : null))
            .ToList();
        var expected = m.Files.ToDictionary(f => f.RelativePath, f => f.AfterHash, StringComparer.OrdinalIgnoreCase);
        var plan = new EditPlan(m.Kuid, $"Undo: {m.Purpose}", changes, expected);
        return await RunAsync(plan, "undo", txId, ct).ConfigureAwait(false);
    }

    private async Task<TxResult> RunAsync(EditPlan plan, string kind, string? undoOf, CancellationToken ct)
    {
        // Path yang keluar dari folder aset ditolak sebelum apa pun dibuka.
        foreach (var c in plan.Changes) SafePath.Combine(Path.GetTempPath(), c.RelativePath);

        var id = store.NewTxId();
        store.Append(id, TxPhase.Started, $"{kind}: {plan.Purpose}");
        // Manifest awal tanpa berkas, agar rekonsiliasi selalu tahu aset mana yang terlibat.
        store.SaveManifest(new TxManifest(id, kind, plan.Kuid, plan.Purpose, DateTimeOffset.UtcNow, [], undoOf));

        // Periksa ulang keadaan terbaru.
        var status = await adapter.GetStatusAsync(plan.Kuid, ct).ConfigureAwait(false);
        if (status.Outcome == Outcome.TrainzNotRunning)
            return Close(id, TxStatus.Blocked, status.Message);
        if (!status.IsSuccess)
            return Close(id, TxStatus.Blocked, $"Status aset tidak dapat dibaca: {status.Message}");
        switch (status.Value!.State)
        {
            case AssetState.OpenForEdit:
                return Close(id, TxStatus.Blocked, "Aset sudah terbuka untuk diedit di luar RailNexus. Pekerjaan tersebut tidak diambil alih.");
            case AssetState.Missing:
                return Close(id, TxStatus.Blocked, "Aset tidak terpasang di profil ini.");
            case AssetState.Unknown:
                return Close(id, TxStatus.Blocked, $"Status aset tidak dikenali ('{status.Value.RawState}'). Penulisan dihentikan.");
        }
        store.Append(id, TxPhase.Prechecked, status.Value.RawState);

        // Aset yang sudah bermasalah divalidasi dulu agar hasil akhir dapat dibandingkan.
        ValidationReport? before = null;
        if (status.Value.State == AssetState.Faulty)
        {
            var v = await adapter.ValidateAsync(plan.Kuid, ct).ConfigureAwait(false);
            if (v.IsSuccess) before = v.Value;
        }

        IDisposable? lockHandle;
        try { lockHandle = store.AcquireLock(plan.Kuid); }
        catch (IOException) { return Close(id, TxStatus.Blocked, "Aset sedang dikerjakan transaksi RailNexus lain."); }

        using (lockHandle)
        {
            store.Append(id, TxPhase.Locked);

            var edit = await adapter.BeginEditAsync(plan.Kuid, ct).ConfigureAwait(false);
            if (!edit.IsSuccess || string.IsNullOrWhiteSpace(edit.Value))
                return Close(id, TxStatus.Blocked, $"Aset tidak dapat dibuka untuk diedit: {edit.Message}");
            var folder = edit.Value;
            store.Append(id, TxPhase.EditOpened, folder);

            // Verifikasi dasar: keadaan berkas harus sama dengan saat draft disusun.
            var conflicts = new List<string>();
            if (plan.ExpectedBaseHashes is { } expected)
            {
                foreach (var (rel, hash) in expected)
                {
                    var current = Hashing.HashFileOrNull(SafePath.Combine(folder, rel));
                    if (!string.Equals(current, hash, StringComparison.OrdinalIgnoreCase)) conflicts.Add(rel);
                }
            }
            if (conflicts.Count > 0)
            {
                await RevertQuietly(id, plan.Kuid, ct).ConfigureAwait(false);
                return Close(id, TxStatus.Conflict,
                    "Berkas berubah sejak rencana disusun. Penerapan dihentikan agar perubahan itu tidak tertimpa.", conflicts: conflicts);
            }
            store.Append(id, TxPhase.BaseVerified);

            // Snapshot: byte asli setiap berkas yang akan diubah/dihapus.
            var preimages = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
            long needed = 0;
            foreach (var c in plan.Changes)
            {
                var path = SafePath.Combine(folder, c.RelativePath);
                var bytes = File.Exists(path) ? await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false) : null;
                preimages[c.RelativePath] = bytes;
                needed += bytes?.LongLength ?? 0;
            }
            if (_freeSpace(store.Root) < needed + SpareBytes)
            {
                await RevertQuietly(id, plan.Kuid, ct).ConfigureAwait(false);
                return Close(id, TxStatus.Blocked, "Ruang disk tidak cukup untuk backup. Aset tidak ditulis.");
            }
            foreach (var (rel, bytes) in preimages)
                if (bytes is not null) store.SavePreimage(id, rel, bytes);

            var manifest = new TxManifest(
                id, kind, plan.Kuid, plan.Purpose, DateTimeOffset.UtcNow,
                plan.Changes.Select(c => new SnapshotEntry(
                    c.RelativePath,
                    preimages[c.RelativePath] is { } pre ? Hashing.Sha256(pre) : null,
                    c.NewBytes is { } nb ? Hashing.Sha256(nb) : null,
                    preimages[c.RelativePath] is not null)).ToList(),
                undoOf);
            store.SaveManifest(manifest);
            store.Append(id, TxPhase.Snapshotted, $"{preimages.Count} berkas");

            // Tulis.
            store.Append(id, TxPhase.Writing);
            try
            {
                foreach (var c in plan.Changes)
                {
                    var path = SafePath.Combine(folder, c.RelativePath);
                    if (c.NewBytes is null) { if (File.Exists(path)) File.Delete(path); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await File.WriteAllBytesAsync(path, c.NewBytes, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return await RollBack(id, plan.Kuid, folder, preimages, $"Penulisan gagal: {ex.Message}", ct).ConfigureAwait(false);
            }
            store.Append(id, TxPhase.Written);

            // Submit. Hasil yang belum pasti tidak diulang secara buta (AC-11).
            store.Append(id, TxPhase.Submitting);
            var commit = await adapter.CommitAsync(plan.Kuid, ct).ConfigureAwait(false);
            switch (commit.Outcome)
            {
                case Outcome.Success:
                    store.Append(id, TxPhase.Submitted);
                    break;
                case Outcome.Unknown:
                    store.Append(id, TxPhase.SubmitUncertain, commit.Message);
                    return new TxResult(id, TxStatus.Uncertain,
                        "Submit sudah dikirim tetapi hasilnya belum dapat dipastikan. Jalankan rekonsiliasi; submit tidak diulang otomatis.");
                default:
                    return await RollBack(id, plan.Kuid, folder, preimages, $"Submit ditolak: {commit.Message}", ct).ConfigureAwait(false);
            }

            // Periksa hasil nyata.
            var after = await adapter.GetStatusAsync(plan.Kuid, ct).ConfigureAwait(false);
            var validation = await adapter.ValidateAsync(plan.Kuid, ct).ConfigureAwait(false);
            store.Append(id, TxPhase.Verified, after.Value?.RawState ?? after.Message);

            if (!after.IsSuccess || !validation.IsSuccess)
                return Close(id, TxStatus.SucceededWithWarnings, "Tersubmit, tetapi hasil belum dapat diverifikasi.", validation.Value, before: before);

            // Bandingkan masalah sebelum dan sesudah (PRD 21.4, AC-24).
            var afterErrors = validation.Value!.Errors;
            var beforeErrors = before?.Errors ?? [];
            var added = afterErrors.Except(beforeErrors, StringComparer.Ordinal).ToList();
            var resolved = beforeErrors.Except(afterErrors, StringComparer.Ordinal).ToList();
            var faultyNow = after.Value!.State == AssetState.Faulty || validation.Value.HasErrors;

            if (faultyNow)
            {
                var msg = added.Count > 0
                    ? $"Tersubmit, tetapi perubahan ini menambah {added.Count} masalah baru. Pertimbangkan Undo."
                    : resolved.Count > 0
                        ? $"Perbaikan sebagian: {resolved.Count} dari {beforeErrors.Count} masalah selesai, {afterErrors.Count} masih ada."
                        : $"Tersubmit. Aset sudah bermasalah sebelum perubahan ({afterErrors.Count} masalah) dan perubahan ini tidak menambahnya.";
                return Close(id, TxStatus.SucceededWithWarnings, msg, validation.Value, before: before);
            }
            if (validation.Value.Warnings.Count > 0)
                return Close(id, TxStatus.SucceededWithWarnings, "Tersubmit dengan peringatan validasi.", validation.Value, before: before);
            var done = beforeErrors.Count > 0
                ? $"Perubahan diterapkan; {beforeErrors.Count} masalah lama selesai dan aset lolos validasi."
                : "Perubahan sudah diterapkan dan diperiksa.";
            return Close(id, TxStatus.Succeeded, done, validation.Value, before: before);
        }
    }

    /// <summary>
    /// Rekonsiliasi transaksi yang belum ditutup (crash atau submit belum pasti).
    /// Hanya membaca keadaan dan membatalkan edit yang belum di-commit milik transaksi ini.
    /// </summary>
    public async Task<TxResult> ReconcileAsync(string txId, CancellationToken ct = default) =>
        await ReconcileAsync(txId, discardPendingEdit: false, ct).ConfigureAwait(false);

    /// <param name="discardPendingEdit">
    /// Pilihan eksplisit pengguna: bila aset masih terbuka setelah submit yang belum pasti, buang edit
    /// yang dibuka transaksi ini, lalu cocokkan isi terpasang dengan hash sebelum/sesudah.
    /// </param>
    public async Task<TxResult> ReconcileAsync(string txId, bool discardPendingEdit, CancellationToken ct = default)
    {
        var last = store.LastPhase(txId);
        if (last is null) return new TxResult(txId, TxStatus.Blocked, "Jurnal tidak ditemukan.");
        if (last == TxPhase.Closed) return new TxResult(txId, TxStatus.Blocked, "Transaksi sudah ditutup.");

        var manifest = store.LoadManifest(txId);
        if (manifest is null) return new TxResult(txId, TxStatus.NeedsAttention, "Manifest tidak ditemukan; periksa aset secara manual.");
        var kuid = manifest.Kuid;
        var journal = store.ReadJournal(txId);
        var submitted = journal.Any(e => e.Phase is TxPhase.Submitting);
        // Hanya edit yang dibuka transaksi ini yang boleh dibatalkan; edit milik orang lain tidak disentuh.
        var openedByUs = journal.Any(e => e.Phase is TxPhase.EditOpened);

        var status = await adapter.GetStatusAsync(kuid, ct).ConfigureAwait(false);
        if (status.Outcome == Outcome.TrainzNotRunning) return new TxResult(txId, TxStatus.NeedsAttention, status.Message);

        if (!submitted)
        {
            if (openedByUs && status.Value?.State == AssetState.OpenForEdit)
            {
                var rv = await adapter.RevertAsync(kuid, ct).ConfigureAwait(false);
                if (!rv.IsSuccess) return new TxResult(txId, TxStatus.NeedsAttention, $"Edit belum di-commit tetapi revert gagal: {rv.Message}");
            }
            store.Append(txId, TxPhase.RolledBack, "rekonsiliasi: edit belum di-commit dibatalkan");
            return Close(txId, TxStatus.RolledBack, "Penulisan terputus sebelum submit. Edit dibatalkan; aset kembali seperti semula.");
        }

        if (status.Value?.State == AssetState.OpenForEdit)
        {
            if (!discardPendingEdit || !openedByUs)
                return new TxResult(txId, TxStatus.NeedsAttention,
                    "Aset masih terbuka untuk diedit setelah submit; submit kemungkinan belum diterima. " +
                    "Pilih: batalkan edit tertunda (aman, aset kembali ke isi terpasang) atau periksa di Content Manager.");
            var rv = await adapter.RevertAsync(kuid, ct).ConfigureAwait(false);
            if (!rv.IsSuccess) return new TxResult(txId, TxStatus.NeedsAttention, $"Revert edit tertunda gagal: {rv.Message}");
            store.Append(txId, TxPhase.RollingBack, "rekonsiliasi: edit tertunda dibatalkan atas pilihan pengguna");
        }

        // Baca isi terpasang tanpa mengubahnya: buka edit, hitung hash, revert.
        var edit = await adapter.BeginEditAsync(kuid, ct).ConfigureAwait(false);
        if (!edit.IsSuccess) return new TxResult(txId, TxStatus.Uncertain, $"Isi aset belum dapat dibaca: {edit.Message}");
        var files = manifest.Files.Select(f => (f, now: Hashing.HashFileOrNull(SafePath.Combine(edit.Value!, f.RelativePath)))).ToList();
        await adapter.RevertAsync(kuid, ct).ConfigureAwait(false);

        if (files.All(x => string.Equals(x.now, x.f.AfterHash, StringComparison.OrdinalIgnoreCase)))
        {
            store.Append(txId, TxPhase.Submitted, "rekonsiliasi: isi sesuai hasil transaksi");
            return Close(txId, TxStatus.Succeeded, "Rekonsiliasi: submit ternyata sudah diterima.");
        }
        if (files.All(x => string.Equals(x.now, x.f.BeforeHash, StringComparison.OrdinalIgnoreCase)))
        {
            store.Append(txId, TxPhase.RolledBack, "rekonsiliasi: isi masih keadaan awal");
            return Close(txId, TxStatus.RolledBack, "Rekonsiliasi: submit tidak diterima; aset masih seperti semula.");
        }
        return new TxResult(txId, TxStatus.NeedsAttention, "Isi aset tidak cocok dengan keadaan sebelum maupun sesudah transaksi.");
    }

    private async Task<TxResult> RollBack(string id, Kuid kuid, string folder, Dictionary<string, byte[]?> preimages, string reason, CancellationToken ct)
    {
        store.Append(id, TxPhase.RollingBack, reason);
        try
        {
            foreach (var (rel, bytes) in preimages)
            {
                var path = SafePath.Combine(folder, rel);
                if (bytes is null) { if (File.Exists(path)) File.Delete(path); }
                else await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Lanjut ke revert; revert membuang edit yang belum di-commit.
            store.Append(id, TxPhase.RollingBack, $"pemulihan berkas lokal gagal: {ex.Message}");
        }
        var rv = await adapter.RevertAsync(kuid, ct).ConfigureAwait(false);
        if (!rv.IsSuccess)
            return new TxResult(id, TxStatus.NeedsAttention, $"{reason} Revert juga gagal: {rv.Message}. Backup tersedia di riwayat.");
        store.Append(id, TxPhase.RolledBack);
        return Close(id, TxStatus.RolledBack, $"{reason} Perubahan dibatalkan; aset kembali seperti semula.");
    }

    private async Task RevertQuietly(string id, Kuid kuid, CancellationToken ct)
    {
        var rv = await adapter.RevertAsync(kuid, ct).ConfigureAwait(false);
        store.Append(id, TxPhase.RolledBack, rv.IsSuccess ? "edit dibatalkan tanpa penulisan" : $"revert gagal: {rv.Message}");
    }

    private TxResult Close(string id, TxStatus status, string message, ValidationReport? validation = null,
        IReadOnlyList<string>? conflicts = null, ValidationReport? before = null)
    {
        store.Append(id, TxPhase.Closed, status.ToString());
        return new TxResult(id, status, message, validation, conflicts, before);
    }
}
