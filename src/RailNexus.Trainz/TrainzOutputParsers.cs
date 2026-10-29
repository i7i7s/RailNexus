using System.Text.RegularExpressions;
using RailNexus.Core;

namespace RailNexus.Trainz;

/// <summary>Parser keluaran perintah baca TrainzUtil, diuji dengan keluaran nyata di fixtures/trainzutil/trs19.</summary>
public static partial class TrainzOutputParsers
{
    [GeneratedRegex(@"^\+\s#\d+\s:\s(?<kuid><kuid2?:[^>]+>)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SearchHitRe();

    /// <summary>
    /// Bukti searchbycategory-VE.json/TV.json: baris hasil "+ &lt;kuid&gt; : + #n : &lt;kuid&gt;" didahului
    /// "+ &lt;NULL&gt; : 90 : assets matching…". Hanya baris hasil berformat itu yang diambil.
    /// </summary>
    public static IReadOnlyList<Kuid> SearchCategory(RawOutput raw) =>
        TuResponse.Parse(raw).Of(LineKind.Result)
            .Select(l => SearchHitRe().Match(l.Message.Trim()))
            .Where(m => m.Success)
            .Select(m => Kuid.Parse(m.Groups["kuid"].Value))
            .Distinct()
            .ToList();

    /// <summary>
    /// Bukti printconfig-after-install.json: isi config berada di antara baris log "+ Start of … dump" dan
    /// "+ End of … dump"; baris pertama bisa berisi sisa BOM ('?'). Null bila penanda tidak lengkap.
    /// </summary>
    public static string? PrintConfig(RawOutput raw)
    {
        var lines = TuResponse.Parse(raw).Lines.Where(x => x.Kind == LineKind.Log).Select(x => x.Message).ToList();
        var start = lines.FindIndex(x => x.StartsWith("+ Start of", StringComparison.Ordinal));
        var end = lines.FindIndex(x => x.StartsWith("+ End of", StringComparison.Ordinal));
        if (start < 0 || end <= start) return null;
        var body = lines.Skip(start + 1).Take(end - start - 1).ToList();
        if (body.Count > 0 && body[0].Trim('\uFEFF', '?', ' ').Length == 0) body.RemoveAt(0);
        return string.Join("\n", body) + "\n";
    }
}
