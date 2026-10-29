using System.Globalization;
using RailNexus.Core.Config;

namespace RailNexus.Core.Cargo;

/// <summary>Satu antrean muatan (container di dalam "queues") beserta konflik yang terlihat.</summary>
public sealed record QueueInfo(
    string Name,
    string Path,
    int? Size,
    int? InitialCount,
    Kuid? InitialKuid,
    IReadOnlyList<Kuid> ProductKinds,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> Uncertain);

/// <summary>
/// Inspeksi muatan/produk (PRD 10.1 P1, UI-10, AC-30). Hanya membaca: tag yang dipahami adalah
/// size, initial-count, initial-kuid, dan product-kinds; tag lain ditandai belum dipahami sehingga
/// hasil pemeriksaan tidak diklaim lengkap. Struktur diuji dengan config sintetis; perlu corpus nyata.
/// </summary>
public static class QueueInspector
{
    private static readonly HashSet<string> Known = new(["size", "initial-count", "initial-kuid", "product-kinds"], StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<QueueInfo> Inspect(ConfigDocument doc)
    {
        var result = new List<QueueInfo>();
        foreach (var queues in doc.Root.Named("queues").Where(t => t.Container is not null))
        foreach (var q in queues.Container!.Children.Where(c => c.Container is not null))
        {
            var c = q.Container!;
            int? Number(string name) => c.Named(name).FirstOrDefault()?.Value is { Kind: ValueKind.Number } v
                                        && int.TryParse(v.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
            var size = Number("size");
            var initialCount = Number("initial-count");
            var initialKuid = c.Named("initial-kuid").FirstOrDefault()?.Value?.Kuid;
            var products = c.Named("product-kinds").Where(t => t.Container is not null)
                .SelectMany(t => t.Container!.Children).Select(t => t.Value?.Kuid).OfType<Kuid>().ToList();

            var conflicts = new List<string>();
            var uncertain = new List<string>();
            if (size is { } s && initialCount is { } ic && ic > s)
                conflicts.Add($"Isi awal {ic} melebihi kapasitas {s}.");
            if (initialKuid is { } ik && products.Count > 0 && !products.Any(p => Roles.References.SameAsset(p, ik)))
                conflicts.Add($"Produk awal {ik} tidak ada di daftar produk yang diterima antrean ini.");
            if (initialCount is > 0 && initialKuid is null)
                uncertain.Add("Isi awal lebih dari nol tetapi produk awal tidak disebut.");
            foreach (var other in c.Children.Select(t => t.Name).Where(n => !Known.Contains(n)).Distinct(StringComparer.OrdinalIgnoreCase))
                uncertain.Add($"Tag '{other}' belum dipahami; pemeriksaan antrean ini mungkin tidak lengkap.");
            result.Add(new QueueInfo(q.Name, q.Path, size, initialCount, initialKuid, products, conflicts, uncertain));
        }
        return result;
    }
}
