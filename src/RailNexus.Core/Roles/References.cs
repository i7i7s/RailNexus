using RailNexus.Core.Config;

namespace RailNexus.Core.Roles;

/// <summary>Satu penggunaan KUID di config. Lokasi penggunaan adalah target patch, bukan identitas KUID (PRD 9.3).</summary>
public sealed record KuidUse(string Path, Kuid Kuid, bool InKuidTable, string? SymbolicName);

public static class References
{
    public static IReadOnlyList<KuidUse> Collect(ConfigDocument doc)
    {
        var uses = new List<KuidUse>();
        foreach (var t in doc.AllTags())
        {
            if (t.Value is null) continue;
            var inTable = IsKuidTableEntry(t);
            if (t.Value.Kuid is { } k) uses.Add(new KuidUse(t.Path, k, inTable, inTable ? t.Name : null));
            foreach (var lk in t.Value.ListKuids) uses.Add(new KuidUse(t.Path, lk, inTable, inTable ? t.Name : null));
        }
        return uses;
    }

    public static bool IsKuidTableEntry(ConfigTag t) =>
        t.Parent is { } p && p.Path.Equals("kuid-table", StringComparison.OrdinalIgnoreCase);

    /// <summary>Aset yang memuat script dapat memakai nama di kuid-table; entrinya tidak dibuang tanpa bukti.</summary>
    public static bool HasScript(ConfigDocument doc) =>
        doc.Root.Children.Any(c => c.Name.Equals("script", StringComparison.OrdinalIgnoreCase)
                                   || c.Name.Equals("class", StringComparison.OrdinalIgnoreCase)
                                   || c.Name.Equals("script-include-table", StringComparison.OrdinalIgnoreCase));

    /// <summary>Kesamaan KUID tanpa membedakan revisi untuk pencocokan penggunaan.</summary>
    public static bool SameAsset(Kuid a, Kuid b) => a.UserId == b.UserId && a.ContentId == b.ContentId;
}
