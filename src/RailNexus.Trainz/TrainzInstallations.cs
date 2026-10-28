namespace RailNexus.Trainz;

/// <summary>Lokasi instalasi yang umum (PRD 7 langkah 1). Pengguna tetap dapat memilih folder lain.</summary>
public static class TrainzInstallations
{
    public const string ToolName = "TrainzUtil.exe";

    public static IReadOnlyList<(string Name, string Folder)> KnownFolders { get; } =
    [
        ("TRS19 (Steam)", @"C:\Program Files (x86)\Steam\steamapps\common\Trainz Railroad Simulator 2019"),
        ("TRS19", @"C:\Program Files\N3V Games\Trainz Railroad Simulator 2019"),
        ("TRS19", @"C:\Program Files (x86)\N3V Games\Trainz Railroad Simulator 2019"),
    ];

    public static string ToolPath(string folder) => Path.Combine(folder, ToolName);

    public static bool HasTool(string folder) => File.Exists(ToolPath(folder));
}
