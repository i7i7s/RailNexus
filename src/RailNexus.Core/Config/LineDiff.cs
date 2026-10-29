namespace RailNexus.Core.Config;

public enum DiffKind { Same, Removed, Added }

/// <summary>Satu baris hasil perbandingan; nomor baris berbasis 1, null bila baris tidak ada di sisi itu.</summary>
public sealed record DiffLine(DiffKind Kind, int? OldLine, int? NewLine, string Text);

/// <summary>
/// Perbandingan per baris untuk tab Perubahan (PRD 11.1). Bagian awal dan akhir yang sama dipangkas
/// dulu, sehingga perubahan kecil pada config panjang tetap murah; bagian tengah memakai LCS.
/// </summary>
public static class LineDiff
{
    /// <summary>Batas sel tabel LCS; di atasnya bagian tengah ditampilkan sebagai satu blok ganti.</summary>
    public const long MaxCells = 4_000_000;

    public static IReadOnlyList<string> SplitLines(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    public static IReadOnlyList<DiffLine> Compare(string oldText, string newText) =>
        Compare(SplitLines(oldText), SplitLines(newText));

    public static IReadOnlyList<DiffLine> Compare(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var result = new List<DiffLine>();
        var prefix = 0;
        while (prefix < a.Count && prefix < b.Count && a[prefix] == b[prefix]) prefix++;
        var suffix = 0;
        while (suffix < a.Count - prefix && suffix < b.Count - prefix && a[a.Count - 1 - suffix] == b[b.Count - 1 - suffix]) suffix++;

        for (var i = 0; i < prefix; i++) result.Add(new DiffLine(DiffKind.Same, i + 1, i + 1, a[i]));

        int n = a.Count - prefix - suffix, m = b.Count - prefix - suffix;
        if ((long)n * m > MaxCells)
        {
            for (var i = 0; i < n; i++) result.Add(new DiffLine(DiffKind.Removed, prefix + i + 1, null, a[prefix + i]));
            for (var j = 0; j < m; j++) result.Add(new DiffLine(DiffKind.Added, null, prefix + j + 1, b[prefix + j]));
        }
        else
        {
            // lcs[i, j] = panjang LCS dari a[prefix+i..] dan b[prefix+j..].
            var lcs = new int[n + 1, m + 1];
            for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                lcs[i, j] = a[prefix + i] == b[prefix + j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

            int x = 0, y = 0;
            while (x < n || y < m)
            {
                if (x < n && y < m && a[prefix + x] == b[prefix + y])
                {
                    result.Add(new DiffLine(DiffKind.Same, prefix + x + 1, prefix + y + 1, a[prefix + x]));
                    x++; y++;
                }
                else if (x < n && (y == m || lcs[x + 1, y] >= lcs[x, y + 1]))
                {
                    // Baris lama didahulukan agar pasangan ganti terbaca "hapus lalu tambah".
                    result.Add(new DiffLine(DiffKind.Removed, prefix + x + 1, null, a[prefix + x]));
                    x++;
                }
                else
                {
                    result.Add(new DiffLine(DiffKind.Added, null, prefix + y + 1, b[prefix + y]));
                    y++;
                }
            }
        }

        for (var k = 0; k < suffix; k++)
        {
            int oi = a.Count - suffix + k, ni = b.Count - suffix + k;
            result.Add(new DiffLine(DiffKind.Same, oi + 1, ni + 1, a[oi]));
        }
        return result;
    }

    /// <summary>Hanya baris berubah beserta beberapa baris konteks di sekitarnya.</summary>
    public static IReadOnlyList<DiffLine> WithContext(IReadOnlyList<DiffLine> lines, int context = 2)
    {
        var keep = new bool[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Kind == DiffKind.Same) continue;
            for (var k = Math.Max(0, i - context); k <= Math.Min(lines.Count - 1, i + context); k++) keep[k] = true;
        }
        return lines.Where((_, i) => keep[i]).ToList();
    }
}
