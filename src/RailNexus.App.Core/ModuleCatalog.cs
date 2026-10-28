namespace RailNexus.App.Core;

/// <summary>Satu modul RailNexus dan kebutuhan utilitas lama yang dipenuhinya (PRD 19.1).</summary>
public sealed record ModuleInfo(string Module, string Utility, string Function, string Stage, StatusLabel Status);

/// <summary>
/// Peta sembilan utilitas pada Workshop (RN-024, AC-34). Status mencerminkan apa yang benar-benar
/// ada di aplikasi ini, bukan target PRD; diperbarui setiap kali sebuah modul bertambah.
/// </summary>
public static class ModuleCatalog
{
    private static readonly StatusLabel Basic = new("Tersedia · dasar", Tone.Ok);
    private static readonly StatusLabel Limited = new("Terbatas", Tone.Info);
    private static readonly StatusLabel NotYet = new("Belum tersedia", Tone.Mute);
    private static readonly StatusLabel Research = new("Riset format (P2)", Tone.Unknown);

    public static IReadOnlyList<ModuleInfo> All { get; } =
    [
        new("Editor Aset + Pusat Perbaikan", "AssetX",
            "Formulir, struktur, teks, dan diff config; diagnosis dependency.", "P0 terbatas · P1 penuh", Limited),
        new("Workshop Suara", "Soundz",
            "Dengarkan berkas WAV yang dapat diakses (A/B), pilih pengganti, ganti WAV lokal lewat transaksi.", "P0 preview dasar · P1 set", Limited),
        new("Lihat Model", "Trainz Mesh Viewer 2",
            "Orbit, LOD, material, attachment.", "P2", Research),
        new("Gerak & Animasi", "Train Swayer Utility",
            "Diagnosis mekanisme gerak yang didukung build.", "P1 diagnosis · P2 authoring", NotYet),
        new("Workshop Mesh: Bayangan", "QuickShadows",
            "Geometri bayangan untuk profil legacy.", "P2", Research),
        new("Workshop Tekstur: Konversi", "Images2TGA",
            "PNG/TGA → TGA 24/32-bit dengan verifikasi baca ulang; benturan nama; staging; perakit kanal PBR.", "P0 gambar umum · P1 PBR · P2 .texture", Basic),
        new("Workshop Mesh: Konversi Legacy", "PM2IM",
            "Mesh .pm → .im.", "P2", Research),
        new("Titik Pemasangan", "Attachment Maker",
            "Titik attachment lewat jalur ekspor teruji.", "P2", Research),
        new("Workshop Tekstur: Metadata", "TextureTXT",
            "Temukan gambar tanpa .texture.txt dan buat/perbarui metadata.", "P0 dasar · P1 lanjutan", Basic),
    ];
}
