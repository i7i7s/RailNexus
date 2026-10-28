using System.Globalization;
using RailNexus.Core.Config;

namespace RailNexus.Core.Roles;

/// <summary>Catatan aset lokal minimum untuk menilai kandidat (PRD 24: AssetRecord).</summary>
public sealed record AssetRecord(Kuid Kuid, string Name, string Kind, AssetState State);

public enum CandidateVerdict { Suitable, SuitableKindUnverified, Rejected }

public sealed record CandidateCheck(AssetRecord Asset, CandidateVerdict Verdict, string Reason);

/// <summary>Hasil penyusunan satu perubahan: edit teks + penjelasan yang ditampilkan di ringkasan.</summary>
public sealed record SlotChangePlan(
    bool Ok,
    string Error,
    IReadOnlyList<TextEdit> Edits,
    /// <summary>Langkah yang diurus otomatis, untuk panel "Diurus otomatis".</summary>
    IReadOnlyList<string> Handled,
    /// <summary>Hal yang belum dapat dipastikan, untuk ditampilkan apa adanya.</summary>
    IReadOnlyList<string> Uncertain)
{
    public static SlotChangePlan Fail(string error) => new(false, error, [], [], []);
}

/// <summary>
/// Mesin modifikasi umum (PRD 9). Semua peran — klakson, suara mesin, tracksound, bogie —
/// memakai jalur yang sama: temukan slot → nilai kandidat → bentuk edit lokasi + sinkron kuid-table.
/// </summary>
public static class ModificationEngine
{
    public static CandidateCheck Evaluate(IRole role, RoleSlot slot, AssetRecord candidate)
    {
        if (References.SameAsset(candidate.Kuid, slot.Current))
            return new(candidate, CandidateVerdict.Rejected, "Sama dengan yang dipakai sekarang.");
        switch (candidate.State)
        {
            case AssetState.Missing:
                return new(candidate, CandidateVerdict.Rejected, "Komponen belum tersedia di profil ini.");
            case AssetState.Faulty:
                return new(candidate, CandidateVerdict.Rejected, "Aset bermasalah di profil ini.");
            case AssetState.Unknown:
                return new(candidate, CandidateVerdict.Rejected, "Status aset belum diketahui.");
        }
        if (role.CandidateKinds.Count == 0)
            return new(candidate, CandidateVerdict.SuitableKindUnverified,
                $"Jenis '{candidate.Kind}' belum dapat dipastikan cocok untuk {role.Label.ToLowerInvariant()}.");
        return role.CandidateKinds.Contains(candidate.Kind, StringComparer.OrdinalIgnoreCase)
            ? new(candidate, CandidateVerdict.Suitable, "Jenis cocok.")
            : new(candidate, CandidateVerdict.Rejected, $"Jenis berbeda: '{candidate.Kind}', dibutuhkan {string.Join(" / ", role.CandidateKinds)}.");
    }

    public static SlotChangePlan PlanReplace(ConfigDocument doc, IRole role, string slotPath, Kuid replacement)
    {
        if (doc.HasErrors) return SlotChangePlan.Fail("Config memiliki error struktur; perubahan otomatis tidak dilakukan.");

        var slot = role.Locate(doc).FirstOrDefault(s => s.Path.Equals(slotPath, StringComparison.OrdinalIgnoreCase));
        if (slot is null) return SlotChangePlan.Fail($"Lokasi '{slotPath}' tidak ditemukan untuk peran {role.Label}.");
        if (slot.Support is SupportLevel.InspectOnly or SupportLevel.NotSupported)
            return SlotChangePlan.Fail($"{role.Label} hanya dapat diperiksa; perubahan dilakukan lewat editor lanjutan.");
        if (References.SameAsset(slot.Current, replacement))
            return SlotChangePlan.Fail("Pengganti sama dengan yang dipakai sekarang.");

        var (tag, err) = ConfigEditor.Single(doc, slot.Path);
        if (tag is null) return SlotChangePlan.Fail(err);
        var set = ConfigEditor.SetKuid(doc, tag, replacement, $"{role.Label}: {slot.Label}");
        if (!set.Ok) return SlotChangePlan.Fail(set.Message);

        var edits = new List<TextEdit> { set.Edit! };
        var handled = new List<string> { $"{slot.Label}: {slot.Current} → {replacement} di '{slot.Path}'" };
        var uncertain = new List<string>();
        if (slot.Context.Count > 0)
            handled.Add("Dipertahankan: " + string.Join(", ", slot.Context.Select(kv => $"{kv.Key} {kv.Value}")));

        var sync = SyncKuidTable(doc, slot, replacement);
        if (!sync.Ok) return SlotChangePlan.Fail(sync.Error);
        edits.AddRange(sync.Edits);
        handled.AddRange(sync.Handled);
        uncertain.AddRange(sync.Uncertain);

        return new SlotChangePlan(true, "", edits, handled, uncertain);
    }

    /// <summary>
    /// Sinkron daftar dependency berdasarkan penggunaan (PRD 9.3). Nama entri tidak pernah
    /// dinomori ulang; entri lama hanya diganti bila tidak dipakai di tempat lain dan aset
    /// tidak memuat script (AC-02, AC-28).
    /// </summary>
    private static SlotChangePlan SyncKuidTable(ConfigDocument doc, RoleSlot slot, Kuid replacement)
    {
        var tables = doc.Root.Named("kuid-table").Where(t => t.Container is not null).ToList();
        if (tables.Count == 0) return new(true, "", [], [], []);
        if (tables.Count > 1) return SlotChangePlan.Fail("kuid-table muncul lebih dari sekali; sinkron dependency tidak ditebak.");
        var table = tables[0].Container!;
        if (table.Close is null) return SlotChangePlan.Fail("kuid-table tidak ditutup.");

        var uses = References.Collect(doc);
        var oldEntries = table.Children.Where(c => c.Value?.Kuid is { } k && References.SameAsset(k, slot.Current)).ToList();
        var otherUses = uses.Count(u => !u.InKuidTable && References.SameAsset(u.Kuid, slot.Current)
                                        && !u.Path.Equals(slot.Path, StringComparison.OrdinalIgnoreCase));
        var newAlreadyListed = table.Children.Any(c => c.Value?.Kuid is { } k && References.SameAsset(k, replacement));
        var hasScript = References.HasScript(doc);

        var edits = new List<TextEdit>();
        var handled = new List<string>();
        var uncertain = new List<string>();

        if (oldEntries.Count > 0 && otherUses == 0 && !hasScript && !newAlreadyListed)
        {
            foreach (var e in oldEntries)
            {
                var r = ConfigEditor.SetKuid(doc, e, replacement, $"kuid-table {e.Name}");
                if (!r.Ok) return SlotChangePlan.Fail(r.Message);
                edits.Add(r.Edit!);
                handled.Add($"kuid-table: entri '{e.Name}' diperbarui; nama dipertahankan");
            }
            return new(true, "", edits, handled, uncertain);
        }

        if (!newAlreadyListed)
        {
            var name = NextFreeName(table);
            var add = ConfigEditor.AppendTag(doc, table, name, replacement.ToString(), "kuid-table tambah");
            if (!add.Ok) return SlotChangePlan.Fail(add.Message);
            edits.Add(add.Edit!);
            handled.Add($"kuid-table: entri '{name}' ditambahkan");
        }
        foreach (var e in oldEntries)
        {
            var why = otherUses > 0 ? $"masih dipakai di {otherUses} lokasi lain"
                : hasScript ? "aset memuat script yang mungkin memakai nama ini"
                : "pengganti sudah tercantum";
            if (otherUses > 0 || hasScript)
                uncertain.Add($"kuid-table: entri lama '{e.Name}' dipertahankan karena {why}.");
            else
                handled.Add($"kuid-table: entri lama '{e.Name}' dipertahankan ({why}).");
        }
        return new(true, "", edits, handled, uncertain);
    }

    private static string NextFreeName(ConfigContainer table)
    {
        var used = table.Children.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var max = table.Children
            .Select(c => int.TryParse(c.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1)
            .DefaultIfEmpty(-1).Max();
        var next = max + 1;
        while (used.Contains(next.ToString(CultureInfo.InvariantCulture))) next++;
        return next.ToString(CultureInfo.InvariantCulture);
    }
}
