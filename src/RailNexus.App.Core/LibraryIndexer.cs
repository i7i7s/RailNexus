using RailNexus.Core;
using RailNexus.Core.Config;
using RailNexus.Core.Library;
using RailNexus.Trainz;

namespace RailNexus.App.Core;

public sealed record IndexResult(bool Ok, string Message, int Stored);

/// <summary>Mengisi indeks pustaka dan membaca KIND aset dari Trainz (operasi baca saja).</summary>
public interface ILibraryIndexer
{
    Task<IndexResult> IndexAsync(IReadOnlyList<string> categories, IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>KIND dari config aset; disimpan ke indeks bila berhasil.</summary>
    Task<OpResult<string>> ReadKindAsync(Kuid kuid, CancellationToken ct = default);
}

/// <summary>
/// Pengindeksan lewat searchbycategory + filterbystatus. Selama searchbycategory/printconfig belum
/// terverifikasi di profil build, hasilnya Unknown dan indeks tidak diubah (PRD 23.2).
/// </summary>
public sealed class TrainzLibraryIndexer(TrainzUtilAdapter adapter, LibraryIndex library) : ILibraryIndexer
{
    public const int BatchSize = 100;

    public async Task<IndexResult> IndexAsync(IReadOnlyList<string> categories, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var stored = 0;
        foreach (var category in categories)
        {
            progress?.Report($"Mencari aset kategori {category}…");
            var search = await adapter.SearchCategoryAsync(category, ct).ConfigureAwait(false);
            if (!search.IsSuccess) return new IndexResult(false, Explain("searchbycategory", search.Outcome, search.Message), stored);

            var kuids = search.Value!;
            var done = 0;
            foreach (var batch in kuids.Chunk(BatchSize))
            {
                var r = await adapter.GetStatusesAsync(batch, ct).ConfigureAwait(false);
                if (!r.IsSuccess) return new IndexResult(false, Explain("status", r.Outcome, r.Message), stored);
                library.Upsert(r.Value!);
                stored += r.Value!.Count;
                done += batch.Length;
                progress?.Report($"Kategori {category}: {done}/{kuids.Count} aset diperiksa");
            }
        }
        return new IndexResult(true, $"{stored} aset diperbarui di indeks.", stored);
    }

    public async Task<OpResult<string>> ReadKindAsync(Kuid kuid, CancellationToken ct = default)
    {
        var text = await adapter.PrintConfigAsync(kuid, ct).ConfigureAwait(false);
        if (!text.IsSuccess) return new OpResult<string>(text.Outcome, null, Explain("printconfig", text.Outcome, text.Message), text.Raw);
        var kind = ConfigDocument.Parse(text.Value!).TopLevelString("kind");
        if (string.IsNullOrWhiteSpace(kind)) return OpResult<string>.Unknown("Config tidak memuat tag kind.", text.Raw);
        library.SetKind(kuid, kind);
        return OpResult<string>.Ok(kind, text.Raw);
    }

    internal static string Explain(string command, Outcome outcome, string message) => outcome switch
    {
        Outcome.Unknown when message.Contains("belum terverifikasi", StringComparison.OrdinalIgnoreCase) =>
            $"Perintah '{command}' belum terverifikasi untuk build ini, jadi hasilnya tidak dipakai. " +
            "Sementara itu isi indeks dengan 'railnexus index' di CLI.",
        Outcome.TrainzNotRunning => "Trainz belum tersambung. Buka Trainz lalu coba lagi.",
        _ => $"{command}: {message}",
    };
}
