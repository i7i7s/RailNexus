using RailNexus.Core.Config;
using RailNexus.Core.Roles;
using RailNexus.Core.Texture;

namespace RailNexus.Core.Dependencies;

/// <summary>Empat kelas masalah (PRD 12.2).</summary>
public enum ProblemClass
{
    /// <summary>Identitas referensi diketahui, konten lokal belum tersedia.</summary>
    NotInstalled,
    /// <summary>Referensi ada tetapi target gagal validasi.</summary>
    InstalledFaulty,
    /// <summary>Ada bukti ketidaksesuaian lokal (config/berkas).</summary>
    Inconsistent,
    /// <summary>Mesin perbaikan belum memiliki dasar yang cukup.</summary>
    NotUnderstood,
}

public enum RepairActionKind
{
    /// <summary>Buka pemilihan pengganti untuk peran yang didukung.</summary>
    ChooseReplacement,
    /// <summary>Buka aset target untuk diagnosis sendiri.</summary>
    OpenTarget,
    /// <summary>Tambahkan entri kuid-table (aturan R001) ke draft.</summary>
    AddKuidTableEntry,
    OpenTextureWorkshop,
    OpenEditor,
}

public sealed record RepairAction(RepairActionKind Kind, string Label, string? RoleId = null, Kuid? Target = null, string? RuleId = null);

/// <summary>Kartu masalah: penyebab, bukti, dampak, dan tindakan yang tersedia (PRD 12.1 langkah 4).</summary>
public sealed record Problem(
    ProblemClass Class,
    string Title,
    string Cause,
    string Evidence,
    string Impact,
    IReadOnlyList<RepairAction> Actions,
    Kuid? Target = null,
    string? Location = null);

/// <summary>
/// Aturan perbaikan berversi (PRD 12.3): ID, versi, bukti pemicu, syarat, perubahan pasti,
/// pemulihan, dan pemeriksaan sesudahnya. Belum ada aturan berstatus otomatis; semua terpandu.
/// </summary>
public sealed record RepairRule(string Id, int Version, string Title, string Trigger, string Condition, string Change, string Recovery, string Check);

public static class RepairRules
{
    public static RepairRule AddKuidTableEntry { get; } = new(
        "R001", 1, "Tambahkan dependency ke kuid-table",
        Trigger: "KUID dipakai pada lokasi terstruktur config tetapi tidak tercantum di kuid-table.",
        Condition: "Config ter-parse tanpa error dan memiliki tepat satu kuid-table yang ditutup.",
        Change: "Satu entri baru dengan nama angka berikutnya yang belum dipakai; entri lain dan nama simbolik tidak diubah.",
        Recovery: "Undo transaksi memulihkan config byte demi byte.",
        Check: "Validasi Trainz setelah submit; masalah baru dilaporkan dan Undo ditawarkan.");

    public static IReadOnlyList<RepairRule> All { get; } = [AddKuidTableEntry];
}

public static class Diagnosis
{
    /// <param name="state">Keadaan target menurut indeks profil aktif.</param>
    /// <param name="selfMissingDependencies">Flag M dari Trainz untuk aset ini.</param>
    public static IReadOnlyList<Problem> Diagnose(
        Kuid self,
        bool selfMissingDependencies,
        ConfigDocument? doc,
        IReadOnlyList<DependencyRef> refs,
        Func<Kuid, TargetState> state,
        ValidationReport? validation,
        IReadOnlyList<InventoryFinding> files)
    {
        var problems = new List<Problem>();
        if (doc is { HasErrors: true })
        {
            var first = doc.Diagnostics.First(d => d.Severity == Severity.Error);
            problems.Add(new Problem(ProblemClass.Inconsistent, "Config tidak dapat dibaca dengan pasti",
                $"Baris {first.Line}: {first.Message}", $"{doc.Diagnostics.Count(d => d.Severity == Severity.Error)} error struktur ({first.Code}).",
                "Perubahan otomatis pada aset ini dihentikan sampai struktur diperbaiki.",
                [new RepairAction(RepairActionKind.OpenEditor, "Buka editor lanjutan")], Location: $"baris {first.Line}"));
        }

        // Satu kartu per target, dengan semua lokasi pemakaiannya sebagai bukti.
        foreach (var group in refs.GroupBy(r => (r.Target.UserId, r.Target.ContentId)))
        {
            var target = group.First().Target;
            var locations = string.Join(", ", group.Select(g => g.Location).Distinct());
            var replaceable = group.FirstOrDefault(g => g.RoleId is not (DependencyInventory.KuidTableRole or DependencyInventory.OtherRole)
                                                        && g.Support is SupportLevel.Guided or SupportLevel.AutomatedTested);
            switch (state(target))
            {
                case TargetState.Missing:
                    problems.Add(new Problem(ProblemClass.NotInstalled, $"Komponen belum tersedia: {target}",
                        "Aset yang dirujuk tidak terpasang di profil ini.", $"Dipakai di: {locations}. Status Trainz: tidak dikenal di profil ini.",
                        "Bagian yang memakai komponen ini tidak dapat tampil atau berfungsi.",
                        ReplacementActions(replaceable, target), target, locations));
                    break;
                case TargetState.Faulty:
                    // AC-08: target terpasang tetapi bermasalah tidak disebut belum diunduh.
                    problems.Add(new Problem(ProblemClass.InstalledFaulty, $"Komponen terpasang tetapi bermasalah: {target}",
                        "Target ada di profil ini, tetapi gagal validasi Trainz.", $"Dipakai di: {locations}. Status Trainz: faulty.",
                        "Aset ini dapat ikut bermasalah sampai target diperbaiki atau diganti.",
                        [new RepairAction(RepairActionKind.OpenTarget, "Buka diagnosis target", Target: target),
                         .. ReplacementActions(replaceable, target)], target, locations));
                    break;
                case TargetState.NotIndexed when selfMissingDependencies:
                    problems.Add(new Problem(ProblemClass.NotInstalled, $"Kemungkinan belum tersedia: {target}",
                        "Trainz menandai aset ini memiliki komponen yang belum tersedia, dan target ini tidak ada di indeks pustaka.",
                        $"Dipakai di: {locations}. Indeks mungkin belum memuat kategori target; status pasti belum dibaca.",
                        "Belum dapat dipastikan sampai status target dibaca.",
                        ReplacementActions(replaceable, target), target, locations));
                    break;
            }
        }

        // R001: referensi terstruktur yang tidak tercantum di kuid-table.
        var table = doc?.Root.Named("kuid-table").Where(t => t.Container is not null).ToList() ?? [];
        if (doc is { HasErrors: false } && table.Count == 1)
        {
            var listed = refs.Where(r => r.InKuidTable).Select(r => (r.Target.UserId, r.Target.ContentId)).ToHashSet();
            foreach (var g in refs.Where(r => !r.InKuidTable && !listed.Contains((r.Target.UserId, r.Target.ContentId)))
                         .GroupBy(r => (r.Target.UserId, r.Target.ContentId)))
            {
                var target = g.First().Target;
                problems.Add(new Problem(ProblemClass.Inconsistent, $"Belum tercantum di kuid-table: {target}",
                    "KUID dipakai di config tetapi tidak ada di daftar dependency aset.",
                    $"Dipakai di: {string.Join(", ", g.Select(x => x.Location))}. Aturan {RepairRules.AddKuidTableEntry.Id} v{RepairRules.AddKuidTableEntry.Version}.",
                    "Belum dibuktikan sebagai penyebab error; periksa laporan validator.",
                    [new RepairAction(RepairActionKind.AddKuidTableEntry, "Tambahkan ke kuid-table", Target: target, RuleId: RepairRules.AddKuidTableEntry.Id)],
                    target, g.First().Location));
            }
        }

        // AC-30: konflik muatan ditunjukkan sebelum penerapan.
        if (doc is { HasErrors: false })
            foreach (var q in Cargo.QueueInspector.Inspect(doc).Where(q => q.Conflicts.Count > 0))
                problems.Add(new Problem(ProblemClass.Inconsistent, $"Konflik muatan pada antrean '{q.Name}'",
                    string.Join(" ", q.Conflicts), $"Lokasi: {q.Path}.", "Muatan dapat tampil atau berperilaku tidak sesuai di Trainz.",
                    [new RepairAction(RepairActionKind.OpenEditor, "Buka editor lanjutan")], Location: q.Path));

        foreach (var f in files.Where(f => f.Kind is InventoryKind.MissingTarget or InventoryKind.UnreadableMetadata))
            problems.Add(new Problem(ProblemClass.Inconsistent, $"Metadata tekstur bermasalah: {f.Path}", f.Message,
                "Berkas lokal aset (bukan dependency ber-KUID).", "Tekstur dapat gagal dibangun Trainz.",
                [new RepairAction(RepairActionKind.OpenTextureWorkshop, "Buka Workshop Tekstur")], Location: f.Path));

        // Pesan validator yang belum dipetakan ditampilkan apa adanya, bukan ditebak.
        if (validation is { } v)
            foreach (var e in v.Errors)
            {
                var mentioned = refs.FirstOrDefault(r => e.Contains(r.Target.ToString(), StringComparison.OrdinalIgnoreCase)
                                                         || e.Contains($"{r.Target.UserId}:{r.Target.ContentId}", StringComparison.Ordinal));
                problems.Add(new Problem(ProblemClass.NotUnderstood, "Laporan validator Trainz", e,
                    mentioned is null ? "Pesan asli validator." : $"Pesan asli validator; menyebut referensi di {mentioned.Location}.",
                    "Aturan perbaikan untuk pesan ini belum ada; periksa manual.",
                    [new RepairAction(RepairActionKind.OpenEditor, "Buka editor lanjutan")], mentioned?.Target, mentioned?.Location));
            }
        return problems;
    }

    private static IReadOnlyList<RepairAction> ReplacementActions(DependencyRef? replaceable, Kuid target) =>
        replaceable is null ? [] : [new RepairAction(RepairActionKind.ChooseReplacement, "Pilih pengganti", replaceable.RoleId, target)];
}
