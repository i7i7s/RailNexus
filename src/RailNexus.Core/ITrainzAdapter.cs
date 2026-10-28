namespace RailNexus.Core;

public enum AssetState
{
    Installed,
    OpenForEdit,
    Faulty,
    Missing,
    Unknown,
}

public sealed record AssetStatus(Kuid Kuid, AssetState State, string RawState);

public sealed record ValidationReport(
    Kuid Kuid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>
/// Kontrak internal RailNexus terhadap instalasi Trainz terpilih (PRD 23.2).
/// Nama operasi ini milik RailNexus, bukan klaim nama API resmi Trainz.
/// Adapter menerjemahkannya ke kemampuan nyata build yang dipilih.
/// </summary>
public interface ITrainzAdapter
{
    Task<OpResult<string>> GetVersionAsync(CancellationToken ct = default);
    Task<OpResult<AssetStatus>> GetStatusAsync(Kuid kuid, CancellationToken ct = default);

    /// <summary>Membuka aset untuk diedit; mengembalikan folder kerja yang ditunjuk Trainz.</summary>
    Task<OpResult<string>> BeginEditAsync(Kuid kuid, CancellationToken ct = default);

    /// <summary>Meminta Trainz menyelesaikan perubahan lokal (submit).</summary>
    Task<OpResult<bool>> CommitAsync(Kuid kuid, CancellationToken ct = default);

    /// <summary>Membatalkan edit yang belum di-commit. Tidak memulihkan perubahan yang sudah di-commit.</summary>
    Task<OpResult<bool>> RevertAsync(Kuid kuid, CancellationToken ct = default);

    Task<OpResult<ValidationReport>> ValidateAsync(Kuid kuid, CancellationToken ct = default);

    /// <summary>Mengimpor folder aset; mengembalikan KUID yang dipasang.</summary>
    Task<OpResult<IReadOnlyList<Kuid>>> ImportFolderAsync(string folder, CancellationToken ct = default);
}
