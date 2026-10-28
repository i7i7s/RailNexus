using System.Globalization;
using System.Text.RegularExpressions;
using RailNexus.Core;

namespace RailNexus.Trainz;

public enum OutputKind
{
    /// <summary>Respons lengkap dengan 0 error.</summary>
    Clean,
    TrainzNotRunning,
    /// <summary>Respons lengkap yang melaporkan error.</summary>
    Error,
    TimedOut,
    /// <summary>Keluaran tanpa baris penutup yang dikenali.</summary>
    Unrecognized,
}

public enum LineKind
{
    /// <summary>';' — log/echo.</summary>
    Log,
    /// <summary>'+' — hasil.</summary>
    Result,
    /// <summary>'-' — error.</summary>
    Error,
    /// <summary>'!' — peringatan.</summary>
    Warning,
    /// <summary>Baris di luar protokol.</summary>
    Other,
}

/// <summary>Satu baris protokol TrainzUtil: "&lt;awalan&gt; &lt;konteks&gt; : &lt;pesan&gt;".</summary>
public sealed record TuLine(LineKind Kind, string Context, string Message, string Raw)
{
    public Kuid? ContextKuid => Kuid.TryParse(Context, out var k) ? k : null;
}

/// <summary>
/// Respons TrainzUtil TRS19 build 117026 (bukti: fixtures/trainzutil/trs19). Exit code selalu 0,
/// termasuk saat error; keberhasilan ditentukan dari baris penutup "OK (n Errors, m Warnings)".
/// </summary>
public sealed partial record TuResponse(IReadOnlyList<TuLine> Lines, int? Errors, int? Warnings)
{
    [GeneratedRegex(@"^(?<p>[;+\-!])\s(?<ctx><NULL>|<[^>]+>)\s:\s?(?<msg>.*)$")]
    private static partial Regex LineRe();

    [GeneratedRegex(@"^OK \((?<e>\d+) Errors?, (?<w>\d+) Warnings?\)$")]
    private static partial Regex TrailerRe();

    public bool Complete => Errors is not null;
    public IEnumerable<TuLine> Of(LineKind kind) => Lines.Where(l => l.Kind == kind);

    public static TuResponse Parse(RawOutput raw)
    {
        var lines = new List<TuLine>();
        int? errors = null, warnings = null;
        foreach (var text in OutputClassifier.Lines(raw))
        {
            var t = TrailerRe().Match(text);
            if (t.Success)
            {
                errors = int.Parse(t.Groups["e"].Value, CultureInfo.InvariantCulture);
                warnings = int.Parse(t.Groups["w"].Value, CultureInfo.InvariantCulture);
                continue;
            }
            var m = LineRe().Match(text);
            if (!m.Success) { lines.Add(new TuLine(LineKind.Other, "", text, text)); continue; }
            var kind = m.Groups["p"].Value switch
            {
                ";" => LineKind.Log,
                "+" => LineKind.Result,
                "-" => LineKind.Error,
                "!" => LineKind.Warning,
                _ => LineKind.Other,
            };
            lines.Add(new TuLine(kind, m.Groups["ctx"].Value, m.Groups["msg"].Value, text));
        }
        return new TuResponse(lines, errors, warnings);
    }
}

/// <summary>
/// Menormalkan keluaran TrainzUtil. Pola hanya ditambahkan bila ada contoh nyata di
/// fixtures/trainzutil. Apa pun yang tidak dikenali menjadi Unrecognized, bukan sukses.
/// </summary>
public static class OutputClassifier
{
    // Bukti: probe-help.json saat Trainz tertutup. "did not respond" berasal dari string di TrainzUtil.exe.
    private static readonly string[] NotRunningMarkers = ["Trainz is not running", "Trainz did not respond to connection attempt"];

    public static OutputKind Classify(RawOutput raw)
    {
        if (raw.TimedOut) return OutputKind.TimedOut;
        var text = raw.StdOut + "\n" + raw.StdErr;
        if (NotRunningMarkers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase))) return OutputKind.TrainzNotRunning;
        var r = TuResponse.Parse(raw);
        if (!r.Complete) return OutputKind.Unrecognized;
        return r.Errors > 0 ? OutputKind.Error : OutputKind.Clean;
    }

    public static IEnumerable<string> Lines(RawOutput raw) =>
        (raw.StdOut + "\n" + raw.StdErr)
            .Split('\n')
            .Select(l => l.TrimEnd('\r').Trim())
            .Where(l => l.Length > 0);

    public static string FirstLine(RawOutput raw) => Lines(raw).FirstOrDefault() ?? "(tanpa keluaran)";

    /// <summary>Pesan error pertama dari protokol, atau baris pertama bila tidak ada.</summary>
    public static string ErrorSummary(RawOutput raw)
    {
        var errs = TuResponse.Parse(raw).Of(LineKind.Error).Select(l => l.Message).ToList();
        return errs.Count == 0 ? FirstLine(raw) : string.Join(" · ", errs);
    }
}
