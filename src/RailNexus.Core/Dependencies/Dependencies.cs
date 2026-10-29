using RailNexus.Core.Config;
using RailNexus.Core.Roles;

namespace RailNexus.Core.Dependencies;

/// <summary>Keadaan target referensi menurut indeks profil aktif. NotIndexed ≠ hilang (indeks bisa parsial).</summary>
public enum TargetState { Installed, Faulty, OpenForEdit, Missing, NotIndexed }

/// <summary>Satu referensi KUID di config beserta perannya (PRD 9.2, 24: DependencyEdge).</summary>
public sealed record DependencyRef(
    string Location,
    Kuid Target,
    string RoleId,
    string RoleLabel,
    SupportLevel Support,
    bool InKuidTable,
    string? SymbolicName);

public static class DependencyInventory
{
    public const string KuidTableRole = "kuid-table";
    public const string OtherRole = "lain";

    /// <summary>
    /// Mengumpulkan referensi langsung. Lokasi yang dikenali peran P0/P1 diberi label perannya; sisanya
    /// ditampilkan sebagai referensi lain tanpa menebak semantik dari nama tag (PRD 9.3).
    /// </summary>
    public static IReadOnlyList<DependencyRef> Collect(ConfigDocument doc, Kuid self)
    {
        var slots = RoleCatalog.All.SelectMany(r => r.Locate(doc).Select(s => (Role: r, Slot: s)))
            .GroupBy(x => x.Slot.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var list = new List<DependencyRef>();
        foreach (var use in References.Collect(doc))
        {
            if (References.SameAsset(use.Kuid, self) && use.Path.Equals("kuid", StringComparison.OrdinalIgnoreCase)) continue;
            if (use.InKuidTable)
                list.Add(new DependencyRef(use.Path, use.Kuid, KuidTableRole, "Daftar dependency (kuid-table)", SupportLevel.InspectOnly, true, use.SymbolicName));
            else if (slots.TryGetValue(use.Path, out var hit))
                list.Add(new DependencyRef(use.Path, use.Kuid, hit.Role.Id, $"{hit.Role.Label} · {hit.Slot.Label}", hit.Slot.Support, false, null));
            else
                list.Add(new DependencyRef(use.Path, use.Kuid, OtherRole, "Referensi lain (peran belum dikenali)", SupportLevel.InspectOnly, false, null));
        }
        return list;
    }
}

public sealed record GraphNode(Kuid Kuid, int Depth, Kuid? Parent, bool IsCycle, bool Expanded);

/// <summary>
/// Graf dependency dengan batas kedalaman, deteksi siklus, dan pemuatan bertahap (LIB-02).
/// Sumber dependency mengembalikan null bila daftar aset itu belum diketahui (belum dibaca).
/// </summary>
public sealed class DependencyGraph
{
    private readonly Dictionary<Kuid, GraphNode> _first = new();

    public IReadOnlyList<GraphNode> Nodes { get; }
    public Kuid Root { get; }

    private DependencyGraph(Kuid root, List<GraphNode> nodes)
    {
        Root = root;
        Nodes = nodes;
        foreach (var n in nodes) _first.TryAdd(Identity(n.Kuid), n);
    }

    private static Kuid Identity(Kuid k) => new(k.UserId, k.ContentId, null);

    public static DependencyGraph Build(Kuid root, Func<Kuid, IReadOnlyList<Kuid>?> dependencies, int maxDepth = 3)
    {
        var nodes = new List<GraphNode>();
        var seen = new HashSet<Kuid> { Identity(root) };
        Walk(root, 1, [Identity(root)]);
        return new DependencyGraph(root, nodes);

        void Walk(Kuid parent, int depth, HashSet<Kuid> path)
        {
            if (depth > maxDepth) return;
            foreach (var child in dependencies(parent)?.DistinctBy(Identity) ?? [])
            {
                var id = Identity(child);
                if (path.Contains(id))
                {
                    nodes.Add(new GraphNode(child, depth, parent, IsCycle: true, Expanded: false));
                    continue;
                }
                var known = dependencies(child) is not null;
                var firstVisit = seen.Add(id);
                nodes.Add(new GraphNode(child, depth, parent, false, known && firstVisit && depth < maxDepth));
                // Aset yang sudah dijelajahi di cabang lain tidak diulang (graf bukan pohon).
                if (firstVisit) Walk(child, depth + 1, [.. path, id]);
            }
        }
    }

    /// <summary>Rantai dari akar ke aset ini, untuk "Kendaraan A → Library B → Tekstur C" (PRD 12.1).</summary>
    public IReadOnlyList<Kuid> Chain(Kuid target)
    {
        var chain = new List<Kuid>();
        Kuid? current = target;
        var guard = 0;
        while (current is { } c && guard++ < 64)
        {
            chain.Add(c);
            if (References.SameAsset(c, Root)) break;
            current = _first.TryGetValue(Identity(c), out var n) ? n.Parent : null;
        }
        chain.Reverse();
        return chain;
    }
}
