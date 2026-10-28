using System.Text;

namespace RailNexus.Core.Config;

public sealed record TextEdit(Span Span, string Replacement, string Reason);

public sealed record EditOutcome(bool Ok, string Message, TextEdit? Edit = null)
{
    public static EditOutcome Fail(string message) => new(false, message);
}

/// <summary>
/// Membentuk edit terarah terhadap <see cref="ConfigDocument"/>. Editor tidak pernah
/// menulis ulang dokumen; ia menghasilkan penggantian rentang yang diterapkan sekaligus.
/// </summary>
public static class ConfigEditor
{
    /// <summary>Mencari tepat satu tag pada path. Nol atau lebih dari satu → gagal (AC-06).</summary>
    public static (ConfigTag? Tag, string Error) Single(ConfigDocument doc, string path)
    {
        if (doc.HasErrors) return (null, "Config memiliki error struktur; perubahan otomatis tidak dilakukan.");
        var hits = doc.Find(path);
        return hits.Count switch
        {
            0 => (null, $"'{path}' tidak ditemukan."),
            1 => (hits[0], ""),
            _ => (null, $"'{path}' muncul {hits.Count} kali; struktur ambigu tidak ditebak."),
        };
    }

    public static EditOutcome SetKuid(ConfigDocument doc, ConfigTag tag, Kuid kuid, string reason)
    {
        if (tag.Value is not { Kind: ValueKind.Kuid } v)
            return EditOutcome.Fail($"'{tag.Path}' bukan field KUID tunggal.");
        return new(true, "ok", new TextEdit(v.Span, kuid.ToString(), reason));
    }

    public static EditOutcome SetString(ConfigDocument doc, ConfigTag tag, string value, string reason)
    {
        if (tag.Value is not { } v) return EditOutcome.Fail($"'{tag.Path}' tidak memiliki nilai.");
        if (v.Kind is not (ValueKind.String or ValueKind.Bare))
            return EditOutcome.Fail($"'{tag.Path}' bukan nilai teks.");
        return new(true, "ok", new TextEdit(v.Span, ConfigDocument.Quote(value), reason));
    }

    /// <summary>
    /// Menyisipkan tag baru di akhir container, mengikuti indentasi dan perataan
    /// saudaranya serta gaya akhir baris dokumen.
    /// </summary>
    public static EditOutcome AppendTag(ConfigDocument doc, ConfigContainer container, string name, string valueText, string reason)
    {
        var text = doc.Text;
        var nl = doc.NewLine;
        var sibling = container.Children.LastOrDefault(c => c.Value is not null) ?? container.Children.LastOrDefault();

        string indent;
        int valueColumn;
        if (sibling is not null)
        {
            var lineStart = LineStart(text, sibling.NameSpan.Start);
            indent = text[lineStart..sibling.NameSpan.Start];
            valueColumn = sibling.Value is { } sv ? sv.Span.Start - lineStart : indent.Length + name.Length + 1;
        }
        else if (container.Close is { } cl)
        {
            var closeIndent = text[LineStart(text, cl.Start)..cl.Start];
            indent = (closeIndent.All(char.IsWhiteSpace) ? closeIndent : "") + "  ";
            valueColumn = indent.Length + name.Length + 1;
        }
        else
        {
            indent = "";
            valueColumn = name.Length + 1;
        }
        var pad = Math.Max(1, valueColumn - indent.Length - name.Length);
        var line = indent + name + new string(' ', pad) + valueText;

        if (container.Close is { } close)
        {
            var ls = LineStart(text, close.Start);
            var onOwnLine = text[ls..close.Start].All(c => c is ' ' or '\t');
            return onOwnLine
                ? new(true, "ok", new TextEdit(new Span(ls, 0), line + nl, reason))
                : new(true, "ok", new TextEdit(new Span(close.Start, 0), nl + line + nl, reason));
        }

        if (container.Path.Length != 0) return EditOutcome.Fail("Container tidak ditutup; tag baru tidak disisipkan.");
        var prefix = text.Length > 0 && !text.EndsWith('\n') ? nl : "";
        return new(true, "ok", new TextEdit(new Span(text.Length, 0), prefix + line + nl, reason));
    }

    /// <summary>Menerapkan edit sekaligus. Edit yang tumpang tindih ditolak.</summary>
    public static string Apply(string text, IEnumerable<TextEdit> edits)
    {
        var ordered = edits.OrderBy(e => e.Span.Start).ThenBy(e => e.Span.Length).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            var a = ordered[i - 1];
            var b = ordered[i];
            if (b.Span.Start < a.Span.End || (b.Span.Start == a.Span.Start && a.Span.Length > 0 && b.Span.Length > 0))
                throw new InvalidOperationException($"Edit tumpang tindih: '{a.Reason}' dan '{b.Reason}'.");
        }
        var sb = new StringBuilder(text.Length + 64);
        var pos = 0;
        foreach (var e in ordered)
        {
            sb.Append(text, pos, e.Span.Start - pos).Append(e.Replacement);
            pos = e.Span.End;
        }
        sb.Append(text, pos, text.Length - pos);
        return sb.ToString();
    }

    private static int LineStart(string text, int index)
    {
        var i = text.LastIndexOf('\n', Math.Max(0, index - 1));
        return i < 0 ? 0 : i + 1;
    }
}
