using RailNexus.Core.Config;

namespace RailNexus.Core.Roles;

/// <summary>Tingkat dukungan per peran (PRD 10.1).</summary>
public enum SupportLevel
{
    /// <summary>Otomatis teruji — hanya setelah lolos uji integrasi pada build terpilih.</summary>
    AutomatedTested,
    /// <summary>Terpandu — perubahan dibentuk otomatis, pengguna meninjau sebelum menerapkan.</summary>
    Guided,
    /// <summary>Inspeksi/manual — ditampilkan, tidak diubah otomatis.</summary>
    InspectOnly,
    NotSupported,
}

/// <summary>Satu lokasi yang dapat diedit: field, slot, atau target di dalam aturan.</summary>
public sealed record RoleSlot(
    string RoleId,
    string Label,
    string Path,
    Kuid Current,
    SupportLevel Support,
    /// <summary>Nilai di sekitar slot yang wajib dipertahankan (orientasi, prioritas, kondisi).</summary>
    IReadOnlyList<KeyValuePair<string, string>> Context,
    string? Note = null);

public interface IRole
{
    string Id { get; }
    string Label { get; }
    /// <summary>Istilah komunitas, ditampilkan sebagai keterangan kecil.</summary>
    string CommunityTerm { get; }
    /// <summary>KIND target yang diterima. Kosong = belum diverifikasi; jenis hanya ditampilkan.</summary>
    IReadOnlyList<string> CandidateKinds { get; }
    IEnumerable<RoleSlot> Locate(ConfigDocument doc);
}

/// <summary>Field KUID tunggal di tingkat atas, mis. hornsound dan enginesound (KIND Traincar).</summary>
public sealed class TopLevelFieldRole(string id, string label, string tag, IReadOnlyList<string> kinds, SupportLevel support, string? note = null) : IRole
{
    public string Id => id;
    public string Label => label;
    public string CommunityTerm => tag;
    public IReadOnlyList<string> CandidateKinds => kinds;

    public IEnumerable<RoleSlot> Locate(ConfigDocument doc)
    {
        var hits = doc.Root.Named(tag).ToList();
        if (hits.Count != 1) yield break; // tidak ada, atau ganda (ambigu) → tidak ditawarkan
        if (hits[0].Value is { Kind: ValueKind.Kuid, Kuid: { } k })
            yield return new RoleSlot(id, label, hits[0].Path, k, support, [], note);
    }
}

/// <summary>Slot bogie di container "bogeys"; orientasi tiap slot dipertahankan (PRD 10.5).</summary>
public sealed class BogieRole(SupportLevel support, string? note = null) : IRole
{
    public string Id => "bogie";
    public string Label => "Bogie";
    public string CommunityTerm => "bogeys";
    public IReadOnlyList<string> CandidateKinds => ["bogey"];

    public IEnumerable<RoleSlot> Locate(ConfigDocument doc)
    {
        var containers = doc.Root.Named("bogeys").Where(t => t.Container is not null).ToList();
        if (containers.Count != 1) yield break;
        foreach (var slot in containers[0].Container!.Children.Where(c => c.Container is not null))
        {
            var bogeys = slot.Container!.Named("bogey").ToList();
            if (bogeys.Count != 1 || bogeys[0].Value is not { Kind: ValueKind.Kuid, Kuid: { } k }) continue;
            var context = slot.Container.Children
                .Where(c => c != bogeys[0] && c.Value is not null)
                .Select(c => new KeyValuePair<string, string>(c.Name, c.Value!.Text))
                .ToList();
            yield return new RoleSlot(Id, $"Slot {slot.Name}", bogeys[0].Path, k, support, context, note);
        }
    }
}

/// <summary>
/// Target KUID di dalam aturan container "track-sound". Hanya target yang diganti;
/// kondisi dan prioritas ikut ditampilkan sebagai konteks dan tidak disentuh (AC-03).
/// </summary>
public sealed class TrackSoundRole(SupportLevel support, string? note = null) : IRole
{
    public string Id => "tracksound";
    public string Label => "Suara roda-rel";
    public string CommunityTerm => "track-sound";
    public IReadOnlyList<string> CandidateKinds => [];

    public IEnumerable<RoleSlot> Locate(ConfigDocument doc)
    {
        foreach (var ts in doc.AllTags().Where(t => t.Container is not null && t.Name.Equals("track-sound", StringComparison.OrdinalIgnoreCase)))
        foreach (var rule in ts.Container!.Children.Where(c => c.Container is not null))
        {
            var context = rule.Container!.Children
                .Where(c => c.Value is { Kind: not ValueKind.Kuid })
                .Select(c => new KeyValuePair<string, string>(c.Name, c.Value!.Text))
                .ToList();
            var targets = rule.Container.Children.Where(c => c.Value is { Kind: ValueKind.Kuid }).ToList();
            foreach (var target in targets)
            {
                // Bila satu aturan memuat beberapa KUID (mis. filter rel + suara), label menyebut nama tag
                // agar pengguna memilih target yang benar; peran tidak ditebak dari kemiripan nama.
                var label = targets.Count == 1 ? $"Aturan {rule.Name}" : $"Aturan {rule.Name} · {target.Name}";
                yield return new RoleSlot(Id, label, target.Path, target.Value!.Kuid!.Value, support, context, note);
            }
        }
    }
}

public static class RoleCatalog
{
    private const string UntilIntegration = "Menunggu uji integrasi Tahap 0 sebelum menjadi 'Otomatis teruji'.";

    /// <summary>
    /// Peran P0 (PRD 10.1). Semua berstatus Terpandu sampai lolos uji pada TRS19 asli.
    /// KIND target klakson dan suara rel belum diverifikasi, sehingga jenis tidak dipakai untuk menolak.
    /// </summary>
    public static IReadOnlyList<IRole> P0 { get; } =
    [
        new TopLevelFieldRole("hornsound", "Klakson", "hornsound", [], SupportLevel.Guided, UntilIntegration),
        new TopLevelFieldRole("enginesound", "Suara mesin", "enginesound", ["enginesound"], SupportLevel.Guided, UntilIntegration),
        new TrackSoundRole(SupportLevel.Guided, "Struktur aturan diverifikasi dengan corpus config nyata. " + UntilIntegration),
        new BogieRole(SupportLevel.Guided, UntilIntegration),
    ];

    private const string P1Note = "Peran P1 (PRD 10.1). KIND target mengikuti dokumentasi KIND Traincar dan belum diuji pada build ini; " +
                                  "kandidat dengan KIND belum terbaca tidak ditawarkan. Kecocokan perilaku tetap perlu uji di Trainz.";

    /// <summary>
    /// Peran P1 terpandu (PRD 10.1): field KUID tunggal pada kendaraan. Kontrak kabin, attachment,
    /// dan parameter fisika tidak diperiksa otomatis; karena itu KIND target diwajibkan.
    /// </summary>
    public static IReadOnlyList<IRole> P1 { get; } =
    [
        new TopLevelFieldRole("interior", "Interior/kabin", "interior", ["interior"], SupportLevel.Guided, P1Note),
        new TopLevelFieldRole("pantograph", "Pantograf", "pantograph", ["pantograph"], SupportLevel.Guided, P1Note),
        new TopLevelFieldRole("enginespec", "Spesifikasi mesin", "enginespec", ["enginespec"], SupportLevel.Guided, P1Note),
    ];

    public static IReadOnlyList<IRole> All { get; } = [.. P0, .. P1];

    public static IRole? ById(string id) => All.FirstOrDefault(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
