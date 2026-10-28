using System.Text.Json;
using RailNexus.Core;

namespace RailNexus.Trainz;

/// <summary>Satu contoh keluaran nyata TrainzUtil untuk corpus uji (PRD 27).</summary>
public sealed record CorpusEntry(
    string Name,
    string Build,
    DateTimeOffset CapturedAt,
    IReadOnlyList<string> Arguments,
    int? ExitCode,
    bool TimedOut,
    double DurationMs,
    string StdOut,
    string StdErr,
    string Note);

public static class Corpus
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Menyamarkan path pribadi sebelum disimpan (PRD 25: privasi).</summary>
    public static string Redact(string text)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home)) text = text.Replace(home, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        var user = Environment.UserName;
        if (!string.IsNullOrEmpty(user) && user.Length > 2) text = text.Replace(user, "%USERNAME%", StringComparison.OrdinalIgnoreCase);
        return text;
    }

    public static CorpusEntry FromRaw(string name, string build, RawOutput raw, string note = "") => new(
        name, build, DateTimeOffset.UtcNow,
        raw.Arguments.Select(Redact).ToList(),
        raw.ExitCode, raw.TimedOut, Math.Round(raw.Duration.TotalMilliseconds),
        Redact(raw.StdOut), Redact(raw.StdErr), note);

    public static string Save(string directory, CorpusEntry entry)
    {
        Directory.CreateDirectory(directory);
        var safe = string.Concat(entry.Name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));
        var path = Path.Combine(directory, safe + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(entry, Json));
        return path;
    }

    public static CorpusEntry Load(string path) =>
        JsonSerializer.Deserialize<CorpusEntry>(File.ReadAllText(path))!;

    public static RawOutput ToRaw(CorpusEntry e) =>
        new("TrainzUtil.exe", e.Arguments, e.ExitCode, e.StdOut, e.StdErr, TimeSpan.FromMilliseconds(e.DurationMs), e.TimedOut);
}
