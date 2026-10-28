using RailNexus.Core;

namespace RailNexus.Tests;

public enum CommitBehavior { Accept, Reject, TimeoutNotApplied, TimeoutButApplied }

/// <summary>
/// Simulator adapter untuk skenario kegagalan (PRD 27: "simulator adapter untuk kegagalan").
/// Isi aset terpasang disimpan di memori; edit dibuka ke folder sementara seperti Trainz.
/// </summary>
public sealed class SimulatedTrainz : ITrainzAdapter, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rn-sim-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Dictionary<Kuid, Dictionary<string, byte[]>> _installed = new();
    private readonly Dictionary<Kuid, string> _open = new();

    public CommitBehavior Commit { get; set; } = CommitBehavior.Accept;
    public bool Running { get; set; } = true;
    public HashSet<Kuid> Faulty { get; } = new();
    public int CommitCalls { get; private set; }
    public int RevertCalls { get; private set; }

    public SimulatedTrainz() => Directory.CreateDirectory(_root);

    public void Install(Kuid kuid, Dictionary<string, byte[]> files) =>
        _installed[kuid] = files.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.OrdinalIgnoreCase);

    public byte[]? InstalledFile(Kuid kuid, string rel) =>
        _installed[kuid].TryGetValue(rel, out var b) ? b : null;

    /// <summary>Mensimulasikan editor lain mengubah aset lalu men-commit-nya.</summary>
    public void ExternalEdit(Kuid kuid, string rel, byte[] bytes) => _installed[kuid][rel] = bytes;

    /// <summary>Mensimulasikan aset yang dibuka untuk diedit di luar RailNexus.</summary>
    public string OpenExternally(Kuid kuid) => OpenFolder(kuid);

    public bool IsOpen(Kuid kuid) => _open.ContainsKey(kuid);
    public string? OpenFolderOf(Kuid kuid) => _open.GetValueOrDefault(kuid);

    public Task<OpResult<string>> GetVersionAsync(CancellationToken ct = default) =>
        Task.FromResult(Running ? OpResult<string>.Ok("sim-1.0") : OpResult<string>.NotRunning());

    public Task<OpResult<AssetStatus>> GetStatusAsync(Kuid kuid, CancellationToken ct = default)
    {
        if (!Running) return Task.FromResult(OpResult<AssetStatus>.NotRunning());
        var state = !_installed.ContainsKey(kuid) ? AssetState.Missing
            : _open.ContainsKey(kuid) ? AssetState.OpenForEdit
            : Faulty.Contains(kuid) ? AssetState.Faulty
            : AssetState.Installed;
        return Task.FromResult(OpResult<AssetStatus>.Ok(new AssetStatus(kuid, state, state.ToString())));
    }

    public Task<OpResult<string>> BeginEditAsync(Kuid kuid, CancellationToken ct = default)
    {
        if (!Running) return Task.FromResult(OpResult<string>.NotRunning());
        if (!_installed.ContainsKey(kuid)) return Task.FromResult(OpResult<string>.Fail("tidak terpasang"));
        if (_open.ContainsKey(kuid)) return Task.FromResult(OpResult<string>.Fail("sudah terbuka"));
        return Task.FromResult(OpResult<string>.Ok(OpenFolder(kuid)));
    }

    private string OpenFolder(Kuid kuid)
    {
        var folder = Path.Combine(_root, $"{kuid.UserId}_{kuid.ContentId}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        foreach (var (rel, bytes) in _installed[kuid])
        {
            var p = Path.Combine(folder, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllBytes(p, bytes);
        }
        _open[kuid] = folder;
        return folder;
    }

    public Task<OpResult<bool>> CommitAsync(Kuid kuid, CancellationToken ct = default)
    {
        CommitCalls++;
        if (!_open.TryGetValue(kuid, out var folder)) return Task.FromResult(OpResult<bool>.Fail("tidak terbuka"));
        switch (Commit)
        {
            case CommitBehavior.Reject:
                return Task.FromResult(OpResult<bool>.Fail("validasi menolak commit"));
            case CommitBehavior.TimeoutNotApplied:
                return Task.FromResult(OpResult<bool>.Unknown("timeout"));
            case CommitBehavior.TimeoutButApplied:
                Apply(kuid, folder);
                return Task.FromResult(OpResult<bool>.Unknown("timeout"));
            default:
                Apply(kuid, folder);
                return Task.FromResult(OpResult<bool>.Ok(true));
        }
    }

    /// <summary>Error validasi per aset; aset dengan error dianggap faulty.</summary>
    public Dictionary<Kuid, List<string>> Errors { get; } = new();
    /// <summary>Error yang berlaku setelah commit berikutnya (meniru efek perubahan).</summary>
    public Dictionary<Kuid, List<string>> ErrorsAfterCommit { get; } = new();

    private void Apply(Kuid kuid, string folder)
    {
        if (ErrorsAfterCommit.Remove(kuid, out var next))
        {
            Errors[kuid] = next;
            if (next.Count == 0) Faulty.Remove(kuid); else Faulty.Add(kuid);
        }
        _installed[kuid] = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(folder, f), File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
        _open.Remove(kuid);
    }

    public Task<OpResult<bool>> RevertAsync(Kuid kuid, CancellationToken ct = default)
    {
        RevertCalls++;
        _open.Remove(kuid);
        return Task.FromResult(OpResult<bool>.Ok(true));
    }

    public Task<OpResult<ValidationReport>> ValidateAsync(Kuid kuid, CancellationToken ct = default) =>
        Task.FromResult(OpResult<ValidationReport>.Ok(new ValidationReport(kuid,
            Errors.TryGetValue(kuid, out var e) ? e : Faulty.Contains(kuid) ? ["simulasi: aset faulty"] : [], [])));

    public Task<OpResult<IReadOnlyList<Kuid>>> ImportFolderAsync(string folder, CancellationToken ct = default) =>
        Task.FromResult(OpResult<IReadOnlyList<Kuid>>.Unsupported("tidak disimulasikan"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
