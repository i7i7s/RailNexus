using System.Text;

namespace RailNexus.Core.Texture;

/// <summary>
/// Isi <c>.texture.txt</c>: pasangan key=value per baris (PRD 13.4). Seperti config, teks asli
/// disimpan apa adanya; hanya nilai key yang diubah yang ditulis ulang, sehingga key yang tidak
/// dikenal, komentar, urutan, dan akhir baris tetap terjaga.
/// </summary>
public sealed class TextureMetadata
{
    /// <summary>Isi baris tanpa akhir baris, dan akhir barisnya sendiri ("\r\n", "\n", atau "" untuk baris terakhir).</summary>
    private readonly List<(string Text, string End)> _lines;
    private readonly bool _bom;

    public string NewLine { get; }

    private TextureMetadata(List<(string, string)> lines, string newLine, bool bom)
    {
        _lines = lines;
        NewLine = newLine;
        _bom = bom;
    }

    public static TextureMetadata CreateNew() => new([], "\r\n", false);

    /// <summary>Membaca metadata lama. Gagal bila bukan UTF-8 yang dapat ditulis ulang tanpa merusak byte, atau bila ambigu.</summary>
    public static (TextureMetadata? Metadata, string Error) Parse(byte[] bytes)
    {
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var enc = new UTF8Encoding(bom);
        var text = enc.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        if (!(bom ? enc.GetPreamble() : []).Concat(enc.GetBytes(text)).SequenceEqual(bytes))
            return (null, "Encoding .texture.txt tidak dikenali sebagai UTF-8; metadata tidak diubah otomatis.");

        var lines = new List<(string, string)>();
        var start = 0;
        while (start < text.Length)
        {
            var nl = text.IndexOf('\n', start);
            if (nl < 0) { lines.Add((text[start..], "")); break; }
            var crlf = nl > start && text[nl - 1] == '\r';
            lines.Add((text[start..(crlf ? nl - 1 : nl)], crlf ? "\r\n" : "\n"));
            start = nl + 1;
        }
        var newLine = lines.Select(l => l.Item2).FirstOrDefault(e => e.Length > 0) ?? "\r\n";
        var meta = new TextureMetadata(lines, newLine, bom);

        var dupes = meta.Keys().GroupBy(k => k, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0)
            return (null, $"Key {string.Join(", ", dupes)} muncul lebih dari sekali; metadata ambigu tidak diubah otomatis.");
        return (meta, "");
    }

    private static (string Key, int ValueStart)? Split(string line)
    {
        var eq = line.IndexOf('=');
        if (eq <= 0) return null;
        var key = line[..eq].Trim();
        if (key.Length == 0 || key.StartsWith(';') || key.StartsWith("//", StringComparison.Ordinal) || key.StartsWith('#')) return null;
        return (key, eq + 1);
    }

    public IEnumerable<string> Keys() => _lines.Select(l => Split(l.Text)).OfType<(string Key, int ValueStart)>().Select(x => x.Key);

    public string? Get(string key)
    {
        foreach (var (line, _) in _lines)
            if (Split(line) is { } kv && kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                return line[kv.ValueStart..].Trim();
        return null;
    }

    /// <summary>Mengubah nilai key yang ada (ejaan key lama dipertahankan) atau menambah baris baru di akhir.</summary>
    public void Set(string key, string value)
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            var (line, end) = _lines[i];
            if (Split(line) is not { } kv || !kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            _lines[i] = (line[..kv.ValueStart] + value, end);
            return;
        }
        if (_lines.Count > 0 && _lines[^1].End.Length == 0) _lines[^1] = (_lines[^1].Text, NewLine);
        _lines.Add(($"{key}={value}", NewLine));
    }

    public byte[] ToBytes()
    {
        var sb = new StringBuilder();
        foreach (var (line, end) in _lines) sb.Append(line).Append(end);
        var enc = new UTF8Encoding(_bom);
        return (_bom ? enc.GetPreamble() : []).Concat(enc.GetBytes(sb.ToString())).ToArray();
    }
}
