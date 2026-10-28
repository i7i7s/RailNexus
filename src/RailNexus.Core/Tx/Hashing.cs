using System.Security.Cryptography;

namespace RailNexus.Core.Tx;

public static class Hashing
{
    public static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>Hash berkas, atau null bila berkas tidak ada.</summary>
    public static string? HashFileOrNull(string path) =>
        File.Exists(path) ? Sha256(File.ReadAllBytes(path)) : null;
}

public static class SafePath
{
    /// <summary>
    /// Menggabungkan folder dan path relatif, menolak path yang keluar dari folder
    /// (PRD 13.3: output tidak ditulis keluar folder tujuan).
    /// </summary>
    public static string Combine(string root, string relative)
    {
        // Path aset Trainz memakai '\'; di luar Windows tanda ini juga diperlakukan sebagai pemisah
        // agar "..\luar.txt" tetap terdeteksi keluar folder.
        relative = relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relative))
            throw new ArgumentException($"Path harus relatif: '{relative}'", nameof(relative));
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Path keluar dari folder aset: '{relative}'", nameof(relative));
        return full;
    }
}
