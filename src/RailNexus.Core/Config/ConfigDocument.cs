using System.Globalization;
using System.Text;

namespace RailNexus.Core.Config;

/// <summary>Rentang karakter pada teks sumber. Semua edit dilakukan sebagai penggantian rentang.</summary>
public readonly record struct Span(int Start, int Length)
{
    public int End => Start + Length;
    public string Of(string text) => text.Substring(Start, Length);
}

public enum ValueKind { String, Kuid, Number, List, Bare }

public sealed record ConfigValue(ValueKind Kind, Span Span, string Text, Kuid? Kuid)
{
    /// <summary>Nilai KUID dari daftar (mis. beberapa KUID dipisah koma).</summary>
    public IReadOnlyList<Kuid> ListKuids { get; init; } = [];
}

public enum Severity { Info, Warning, Error }

public sealed record Diagnostic(string Code, Severity Severity, string Message, Span Span, int Line);

/// <summary>Tag beserta nilai atau container anaknya. Path memakai '/', mis. "bogeys/0/bogey".</summary>
public sealed class ConfigTag
{
    public required string Name { get; init; }
    public required Span NameSpan { get; init; }
    public required string Path { get; init; }
    public required int Line { get; init; }
    public ConfigValue? Value { get; init; }
    public ConfigContainer? Container { get; init; }
    public ConfigContainer? Parent { get; init; }
}

public sealed class ConfigContainer
{
    public required string Path { get; init; }
    /// <summary>Rentang '{' (kosong untuk root).</summary>
    public Span? Open { get; init; }
    /// <summary>Rentang '}' (null bila tidak ditutup atau root).</summary>
    public Span? Close { get; set; }
    public List<ConfigTag> Children { get; } = new();

    public IEnumerable<ConfigTag> Named(string name) =>
        Children.Where(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Dokumen config Trainz yang menyimpan teks asli. Parser tidak menulis ulang apa pun:
/// node menunjuk rentang di teks sumber, sehingga isi di luar bagian yang diubah tetap
/// identik (PRD 11.3, EDIT-AC).
/// </summary>
public sealed class ConfigDocument
{
    public string Text { get; }
    public ConfigContainer Root { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public string NewLine { get; }

    private ConfigDocument(string text, ConfigContainer root, List<Diagnostic> diags)
    {
        Text = text;
        Root = root;
        Diagnostics = diags;
        NewLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }

    public bool HasErrors => Diagnostics.Any(d => d.Severity == Severity.Error);

    public IEnumerable<ConfigTag> AllTags() => Walk(Root);

    private static IEnumerable<ConfigTag> Walk(ConfigContainer c)
    {
        foreach (var t in c.Children)
        {
            yield return t;
            if (t.Container is { } child)
                foreach (var d in Walk(child)) yield return d;
        }
    }

    /// <summary>Semua tag dengan path yang sama (tanpa membedakan huruf besar/kecil).</summary>
    public IReadOnlyList<ConfigTag> Find(string path) =>
        AllTags().Where(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase)).ToList();

    public string? TopLevelString(string name) =>
        Root.Named(name).Select(t => t.Value?.Text).FirstOrDefault(v => v is not null);

    public static ConfigDocument Parse(string text) => new Parser(text).Run();

    // ------------------------------------------------------------------ parser

    private enum Tok { Word, String, Kuid, LBrace, RBrace, Comma, Comment, NewLine, Eof }

    private readonly record struct Token(Tok Kind, Span Span, int Line);

    private sealed class Parser(string text)
    {
        private readonly List<Diagnostic> _diags = new();
        private readonly List<Token> _toks = new();
        private int _pos;

        public ConfigDocument Run()
        {
            Lex();
            var root = new ConfigContainer { Path = "" };
            ParseBody(root, isRoot: true);
            CheckDuplicates(root);
            return new ConfigDocument(text, root, _diags);
        }

        private void Diag(string code, Severity s, string msg, Span span, int line) =>
            _diags.Add(new Diagnostic(code, s, msg, span, line));

        // ---------------- lexer

        private void Lex()
        {
            var i = 0;
            var line = 1;
            while (i < text.Length)
            {
                var c = text[i];
                if (c == '\n') { _toks.Add(new(Tok.NewLine, new(i, 1), line)); line++; i++; continue; }
                if (c is ' ' or '\t' or '\r' or '﻿') { i++; continue; }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    var s = i;
                    while (i < text.Length && text[i] != '\n') i++;
                    _toks.Add(new(Tok.Comment, new(s, i - s), line));
                    continue;
                }
                if (c == '{') { _toks.Add(new(Tok.LBrace, new(i, 1), line)); i++; continue; }
                if (c == '}') { _toks.Add(new(Tok.RBrace, new(i, 1), line)); i++; continue; }
                if (c == ',') { _toks.Add(new(Tok.Comma, new(i, 1), line)); i++; continue; }
                if (c == '"')
                {
                    // String Trainz boleh melebar ke beberapa baris (mis. deskripsi panjang).
                    // Bukti: parse-check pada config asli TRS19 (CFG-STRING/NOVALUE beruntun).
                    var s = i++;
                    var startLine = line;
                    var closed = false;
                    while (i < text.Length)
                    {
                        if (text[i] == '\\' && i + 1 < text.Length) { if (text[i + 1] == '\n') line++; i += 2; continue; }
                        if (text[i] == '"') { i++; closed = true; break; }
                        if (text[i] == '\n') line++;
                        i++;
                    }
                    if (!closed) Diag("CFG-STRING", Severity.Error, "String tidak ditutup tanda kutip sampai akhir berkas.", new(s, i - s), startLine);
                    _toks.Add(new(Tok.String, new(s, i - s), startLine));
                    continue;
                }
                if (c == '<')
                {
                    var s = i;
                    var close = text.IndexOf('>', i);
                    var nl = text.IndexOf('\n', i);
                    if (close > 0 && (nl < 0 || close < nl))
                    {
                        i = close + 1;
                        var span = new Span(s, i - s);
                        if (Kuid.TryParse(span.Of(text), out _)) { _toks.Add(new(Tok.Kuid, span, line)); continue; }
                        _toks.Add(new(Tok.Word, span, line));
                        continue;
                    }
                }
                {
                    var s = i;
                    while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '}' or ',' or '"')
                           && !(text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/'))
                        i++;
                    if (i == s) i++; // karakter tunggal yang tidak dikenali
                    _toks.Add(new(Tok.Word, new(s, i - s), line));
                }
            }
            _toks.Add(new(Tok.Eof, new(text.Length, 0), line));
        }

        // ---------------- parser

        private Token Peek(int ahead = 0) => _toks[Math.Min(_pos + ahead, _toks.Count - 1)];
        private Token Next() => _toks[Math.Min(_pos++, _toks.Count - 1)];

        private void SkipTrivia()
        {
            while (Peek().Kind is Tok.NewLine or Tok.Comment) _pos++;
        }

        private void ParseBody(ConfigContainer container, bool isRoot)
        {
            while (true)
            {
                SkipTrivia();
                var t = Peek();
                if (t.Kind == Tok.Eof)
                {
                    if (!isRoot)
                        Diag("CFG-UNCLOSED", Severity.Error, $"Container '{container.Path}' tidak ditutup.", container.Open ?? t.Span, t.Line);
                    return;
                }
                if (t.Kind == Tok.RBrace)
                {
                    Next();
                    if (isRoot) { Diag("CFG-EXTRA-BRACE", Severity.Error, "Kurung kurawal tutup tanpa pasangan.", t.Span, t.Line); continue; }
                    container.Close = t.Span;
                    return;
                }
                if (t.Kind != Tok.Word)
                {
                    Next();
                    Diag("CFG-UNEXPECTED", Severity.Error, "Isi tidak dikenali; diharapkan nama tag.", t.Span, t.Line);
                    SkipToLineEnd();
                    continue;
                }
                ParseTag(container, Next());
            }
        }

        private void SkipToLineEnd()
        {
            while (Peek().Kind is not (Tok.NewLine or Tok.Eof or Tok.RBrace)) _pos++;
        }

        private void ParseTag(ConfigContainer parent, Token name)
        {
            var tagName = name.Span.Of(text);
            var path = parent.Path.Length == 0 ? tagName : parent.Path + "/" + tagName;

            // Nilai pada baris yang sama.
            var valueToks = new List<Token>();
            while (Peek().Kind is Tok.Word or Tok.String or Tok.Kuid or Tok.Comma) valueToks.Add(Next());

            if (valueToks.Count == 0)
            {
                // Container dapat dibuka di baris yang sama atau di baris berikut.
                var save = _pos;
                SkipTrivia();
                if (Peek().Kind == Tok.LBrace)
                {
                    var open = Next();
                    var child = new ConfigContainer { Path = path, Open = open.Span };
                    parent.Children.Add(new ConfigTag { Name = tagName, NameSpan = name.Span, Path = path, Line = name.Line, Container = child, Parent = parent });
                    ParseBody(child, isRoot: false);
                    return;
                }
                _pos = save;
                // Nilai kosong ditulis printconfig sebagai tag tanpa nilai (mis. 'thumbnail', 'organisation').
                Diag("CFG-NOVALUE", Severity.Info, $"Tag '{tagName}' tidak memiliki nilai.", name.Span, name.Line);
                parent.Children.Add(new ConfigTag { Name = tagName, NameSpan = name.Span, Path = path, Line = name.Line, Parent = parent });
                return;
            }

            if (Peek().Kind == Tok.LBrace)
                Diag("CFG-VALUE-AND-BLOCK", Severity.Error, $"Tag '{tagName}' memiliki nilai sekaligus container.", Peek().Span, Peek().Line);

            parent.Children.Add(new ConfigTag
            {
                Name = tagName, NameSpan = name.Span, Path = path, Line = name.Line, Parent = parent,
                Value = BuildValue(valueToks, tagName, name.Line),
            });
        }

        private ConfigValue BuildValue(List<Token> toks, string tagName, int line)
        {
            var span = new Span(toks[0].Span.Start, toks[^1].Span.End - toks[0].Span.Start);
            if (toks.Count == 1)
            {
                var t = toks[0];
                var raw = t.Span.Of(text);
                return t.Kind switch
                {
                    Tok.String => new ConfigValue(ValueKind.String, span, Unescape(raw), null),
                    Tok.Kuid => new ConfigValue(ValueKind.Kuid, span, raw, Kuid.Parse(raw)),
                    _ when double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _) =>
                        new ConfigValue(ValueKind.Number, span, raw, null),
                    _ => new ConfigValue(ValueKind.Bare, span, raw, null),
                };
            }

            var isList = toks.Where((_, i) => i % 2 == 1).All(t => t.Kind == Tok.Comma)
                         && toks.Where((_, i) => i % 2 == 0).All(t => t.Kind != Tok.Comma);
            if (isList)
            {
                var kuids = toks.Where(t => t.Kind == Tok.Kuid).Select(t => Kuid.Parse(t.Span.Of(text))).ToList();
                return new ConfigValue(ValueKind.List, span, span.Of(text), null) { ListKuids = kuids };
            }

            Diag("CFG-AMBIGUOUS-VALUE", Severity.Warning,
                $"Nilai tag '{tagName}' terdiri dari beberapa bagian yang tidak dikenali; otomasi tidak mengubahnya.", span, line);
            return new ConfigValue(ValueKind.Bare, span, span.Of(text), null);
        }

        private void CheckDuplicates(ConfigContainer c)
        {
            foreach (var g in c.Children.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                var second = g.Skip(1).First();
                Diag("CFG-DUPLICATE", Severity.Warning,
                    $"Tag '{second.Path}' muncul {g.Count()} kali di container yang sama; perubahan otomatis pada tag ini ditolak.",
                    second.NameSpan, second.Line);
            }
            foreach (var t in c.Children)
                if (t.Container is { } child) CheckDuplicates(child);
        }
    }

    internal static string Unescape(string quoted)
    {
        var inner = quoted.Length >= 2 && quoted[^1] == '"' ? quoted[1..^1] : quoted.TrimStart('"');
        if (!inner.Contains('\\')) return inner;
        var sb = new StringBuilder(inner.Length);
        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '\\' && i + 1 < inner.Length)
            {
                var n = inner[++i];
                sb.Append(n switch { 'n' => '\n', 't' => '\t', _ => n });
            }
            else sb.Append(inner[i]);
        }
        return sb.ToString();
    }

    internal static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
}
