using RailNexus.Core;
using RailNexus.Core.Library;
using RailNexus.Core.Roles;
using RailNexus.Core.Tx;

namespace RailNexus.App.Core;

/// <summary>Nada warna status. Warna hanya pendukung; setiap status selalu disertai label teks.</summary>
public enum Tone { Ok, Warn, Err, Info, Mute, Unknown }

/// <summary>Label status berbahasa Indonesia (PRD 6: istilah teknis diterjemahkan).</summary>
public sealed record StatusLabel(string Text, Tone Tone);

public static class Labels
{
    public static StatusLabel Asset(AssetState state) => state switch
    {
        AssetState.Installed => new("Siap digunakan", Tone.Ok),
        AssetState.Faulty => new("Aset bermasalah", Tone.Err),
        AssetState.OpenForEdit => new("Masih terbuka untuk diedit", Tone.Info),
        AssetState.Missing => new("Tidak terpasang", Tone.Mute),
        _ => new("Status belum diketahui", Tone.Unknown),
    };

    /// <summary>Penanda tambahan selain status utama.</summary>
    public static IReadOnlyList<StatusLabel> Extra(LibraryEntry e)
    {
        var list = new List<StatusLabel>();
        if (e.MissingDependencies) list.Add(new("Komponen belum tersedia", Tone.Warn));
        if (e.LocallyModified) list.Add(new("Diubah lokal", Tone.Mute));
        if (e.Payware) list.Add(new("Berbayar", Tone.Mute));
        return list;
    }

    public static string Filter(LibraryFilter f) => f switch
    {
        LibraryFilter.Faulty => "Bermasalah",
        LibraryFilter.MissingDependencies => "Komponen belum tersedia",
        LibraryFilter.OpenForEdit => "Masih diedit",
        LibraryFilter.LocallyModified => "Diubah lokal",
        _ => "Semua",
    };

    public static StatusLabel Support(SupportLevel s) => s switch
    {
        SupportLevel.AutomatedTested => new("Otomatis teruji", Tone.Ok),
        SupportLevel.Guided => new("Terpandu", Tone.Info),
        SupportLevel.InspectOnly => new("Inspeksi/manual", Tone.Mute),
        _ => new("Belum didukung", Tone.Mute),
    };

    public static StatusLabel Transaction(TxStatus s) => s switch
    {
        TxStatus.Succeeded => new("Berhasil", Tone.Ok),
        TxStatus.SucceededWithWarnings => new("Berhasil dengan catatan", Tone.Warn),
        TxStatus.Blocked => new("Dihentikan sebelum menulis", Tone.Mute),
        TxStatus.Conflict => new("Konflik", Tone.Warn),
        TxStatus.RolledBack => new("Dibatalkan dan dipulihkan", Tone.Warn),
        TxStatus.Uncertain => new("Hasil submit belum pasti", Tone.Unknown),
        _ => new("Perlu tindakan", Tone.Err),
    };

    public static StatusLabel Candidate(CandidateVerdict v) => v switch
    {
        CandidateVerdict.Suitable => new("Cocok", Tone.Ok),
        CandidateVerdict.SuitableKindUnverified => new("Jenis belum dapat dipastikan", Tone.Warn),
        _ => new("Tidak sesuai", Tone.Mute),
    };
}
