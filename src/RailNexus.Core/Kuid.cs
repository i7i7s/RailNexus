using System.Globalization;
using System.Text.RegularExpressions;

namespace RailNexus.Core;

/// <summary>
/// Identitas aset Trainz. Revisi dipertahankan apa adanya; KUID tidak pernah
/// diratakan menjadi teks nama aset (PRD bagian 24).
/// </summary>
public readonly partial record struct Kuid(int UserId, int ContentId, int? Revision)
{
    [GeneratedRegex(@"^<\s*kuid2?\s*:\s*(-?\d+)\s*:\s*(-?\d+)\s*(?::\s*(\d+)\s*)?>$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    public static bool TryParse(string? text, out Kuid kuid)
    {
        kuid = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var m = Pattern().Match(text.Trim());
        if (!m.Success) return false;
        if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var user)) return false;
        if (!int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var content)) return false;
        int? rev = null;
        if (m.Groups[3].Success)
        {
            if (!int.TryParse(m.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r)) return false;
            rev = r;
        }
        kuid = new Kuid(user, content, rev);
        return true;
    }

    public static Kuid Parse(string text) =>
        TryParse(text, out var k) ? k : throw new FormatException($"Bukan KUID yang valid: '{text}'");

    public override string ToString() => Revision is { } r
        ? string.Create(CultureInfo.InvariantCulture, $"<kuid2:{UserId}:{ContentId}:{r}>")
        : string.Create(CultureInfo.InvariantCulture, $"<kuid:{UserId}:{ContentId}>");
}
