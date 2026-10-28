namespace RailNexus.Core.Tx;

/// <summary>Satu perubahan berkas. NewBytes null berarti berkas dihapus.</summary>
public sealed record FileChange(string RelativePath, byte[]? NewBytes);

/// <summary>
/// Rencana perubahan untuk satu aset. ExpectedBaseHashes berisi hash berkas saat
/// draft disusun (null = berkas belum ada). Bila keadaan terkini berbeda, penerapan
/// berhenti karena konflik (AC-10).
/// </summary>
public sealed record EditPlan(
    Kuid Kuid,
    string Purpose,
    IReadOnlyList<FileChange> Changes,
    IReadOnlyDictionary<string, string?>? ExpectedBaseHashes = null);

public enum TxPhase
{
    Started,
    Prechecked,
    Locked,
    EditOpened,
    BaseVerified,
    Snapshotted,
    Writing,
    Written,
    Submitting,
    Submitted,
    SubmitUncertain,
    Verified,
    RollingBack,
    RolledBack,
    Closed,
}

public enum TxStatus
{
    Succeeded,
    SucceededWithWarnings,
    /// <summary>Dihentikan sebelum ada penulisan; aset tidak berubah.</summary>
    Blocked,
    Conflict,
    /// <summary>Penulisan dibatalkan dan aset dikembalikan.</summary>
    RolledBack,
    /// <summary>Submit sudah dikirim tetapi hasilnya belum diketahui; perlu rekonsiliasi.</summary>
    Uncertain,
    /// <summary>Gagal dan pemulihan juga tidak dapat dipastikan; perlu tindakan pengguna.</summary>
    NeedsAttention,
}

public sealed record SnapshotEntry(string RelativePath, string? BeforeHash, string? AfterHash, bool ExistedBefore);

public sealed record TxManifest(
    string TxId,
    string Kind,
    Kuid Kuid,
    string Purpose,
    DateTimeOffset StartedAt,
    IReadOnlyList<SnapshotEntry> Files,
    string? UndoOf = null);

public sealed record TxResult(
    string TxId,
    TxStatus Status,
    string Message,
    ValidationReport? Validation = null,
    IReadOnlyList<string>? ConflictingFiles = null,
    /// <summary>Hasil validasi sebelum perubahan, bila aset sudah bermasalah.</summary>
    ValidationReport? ValidationBefore = null);
