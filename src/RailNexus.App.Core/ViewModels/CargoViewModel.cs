using RailNexus.Core.Cargo;

namespace RailNexus.App.Core.ViewModels;

public sealed record QueueRow(string Name, string Capacity, string Initial, IReadOnlyList<string> Products, IReadOnlyList<string> Conflicts, IReadOnlyList<string> Uncertain)
{
    public StatusLabel Status => Conflicts.Count > 0 ? new("Ada konflik", Tone.Err)
        : Uncertain.Count > 0 ? new("Pemeriksaan belum lengkap", Tone.Unknown)
        : new("Tidak ada konflik terlihat", Tone.Ok);
}

/// <summary>UI-10 Produk/muatan (P1, inspeksi): antrean, kapasitas, isi awal, produk yang diterima, konflik (AC-30).</summary>
public sealed class CargoViewModel : ObservableObject, IPage
{
    private readonly Action _back;

    public CargoViewModel(AppSession session, AssetWorkspace workspace, FooterAction footer, Action back)
    {
        _back = back;
        Footer = footer;
        if (workspace.Document is { } doc)
            foreach (var q in QueueInspector.Inspect(doc))
                Rows.Add(new QueueRow(q.Name,
                    q.Size?.ToString() ?? "tidak disebut",
                    $"{q.InitialCount?.ToString() ?? "tidak disebut"} · {(q.InitialKuid is { } k ? session.Find(k)?.Name ?? k.ToString() : "produk awal tidak disebut")}",
                    q.ProductKinds.Select(p => session.Find(p)?.Name ?? p.ToString()).ToList(), q.Conflicts, q.Uncertain));
    }

    public string Title => "Produk & muatan";
    public FooterAction Footer { get; }
    public string? BackLabel => "Kembali ke Mau edit apa?";
    public void Back() => _back();
    public List<QueueRow> Rows { get; } = new();
    public string Note => "Inspeksi saja. Penyuntingan muatan belum tersedia; perubahan manual dapat dilakukan lewat editor lanjutan.";
}
