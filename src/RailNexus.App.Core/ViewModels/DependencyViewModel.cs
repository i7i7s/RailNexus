using System.Collections.ObjectModel;
using System.Windows.Input;
using RailNexus.Core;
using RailNexus.Core.Dependencies;
using RailNexus.Core.Roles;

namespace RailNexus.App.Core.ViewModels;

public static class TargetLabels
{
    public static StatusLabel Of(TargetState s) => s switch
    {
        TargetState.Installed => new("Siap digunakan", Tone.Ok),
        TargetState.Faulty => new("Aset bermasalah", Tone.Err),
        TargetState.OpenForEdit => new("Masih terbuka untuk diedit", Tone.Info),
        TargetState.Missing => new("Komponen belum tersedia", Tone.Warn),
        _ => new("Belum ada di indeks", Tone.Unknown),
    };
}

/// <summary>Satu referensi di inventaris: peran, lokasi, target, status, dan tindakan yang tersedia.</summary>
public sealed record DependencyRow(
    string Role,
    string Location,
    string TargetName,
    string TargetKuid,
    StatusLabel Status,
    StatusLabel Support,
    string? SymbolicName,
    ICommand? Replace,
    ICommand? OpenTarget);

/// <summary>Satu node graf turunan; indentasi mengikuti kedalaman.</summary>
public sealed record GraphRow(int Depth, string Name, string Kuid, StatusLabel Status, string Note)
{
    public double Indent => (Depth - 1) * 20;
}

/// <summary>
/// "Semua komponen & dependency" (UI-04 inventaris, RN-008): referensi langsung per peran dengan
/// status target, serta dependency turunan yang dimuat bertahap dengan batas kedalaman dan deteksi siklus.
/// </summary>
public sealed class DependencyViewModel : ObservableObject, IPage
{
    public const int MaxDepth = 3;

    private readonly AppSession _session;
    private readonly AssetWorkspace _workspace;
    private readonly AssetNavigation _nav;
    private string? _message;

    public DependencyViewModel(AppSession session, AssetWorkspace workspace, FooterAction footer, AssetNavigation nav)
    {
        _session = session;
        _workspace = workspace;
        _nav = nav;
        Footer = footer;
        ReadTransitiveCommand = new AsyncCommand(ReadTransitiveAsync, () => _workspace.Document is not null);
        Build();
    }

    public string Title => "Komponen & dependency";
    public FooterAction Footer { get; }
    public string? BackLabel => "Kembali ke Mau edit apa?";
    public void Back() => _nav.Back();

    public ObservableCollection<DependencyRow> Rows { get; } = new();
    public ObservableCollection<GraphRow> Graph { get; } = new();
    public AsyncCommand ReadTransitiveCommand { get; }
    public string? Message { get => _message; private set => Set(ref _message, value); }
    public string? Unavailable => _workspace.Document is null ? _workspace.ReadError ?? "Isi aset belum terbaca. Kembali dan buka ulang aset." : null;

    public string Summary => Rows.Count == 0 ? "Tidak ada referensi KUID di config ini."
        : $"{Rows.Count} referensi langsung · {Rows.Count(r => r.Status.Tone is Tone.Err or Tone.Warn)} perlu perhatian";

    private void Build()
    {
        Rows.Clear();
        if (_workspace.Document is not { } doc) return;
        foreach (var r in DependencyInventory.Collect(doc, _workspace.Entry.Kuid).OrderBy(r => r.InKuidTable).ThenBy(r => r.Location, StringComparer.OrdinalIgnoreCase))
        {
            var entry = _session.Find(r.Target);
            var role = RoleCatalog.ById(r.RoleId);
            ICommand? replace = role is not null && r.Support is SupportLevel.Guided or SupportLevel.AutomatedTested
                ? new RelayCommand(() => _nav.OpenRole(role)) : null;
            ICommand? open = entry is null ? null : new RelayCommand(() => _nav.OpenAsset(entry));
            Rows.Add(new DependencyRow(r.RoleLabel, r.Location, entry?.Name ?? r.Target.ToString(), r.Target.ToString(),
                TargetLabels.Of(_session.TargetState(r.Target)), Labels.Support(r.Support), r.SymbolicName, replace, open));
        }
        BuildGraph();
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>Dependency suatu aset diketahui bila config-nya sudah dibaca di sesi ini; selain itu null.</summary>
    private IReadOnlyList<Kuid>? KnownDependencies(Kuid kuid)
    {
        var ws = _session.Workspaces.Values.FirstOrDefault(w => References.SameAsset(w.Entry.Kuid, kuid));
        return ws?.Document is { } d
            ? DependencyInventory.Collect(d, ws.Entry.Kuid).Select(r => r.Target).ToList()
            : null;
    }

    private void BuildGraph()
    {
        Graph.Clear();
        var graph = DependencyGraph.Build(_workspace.Entry.Kuid, KnownDependencies, MaxDepth);
        foreach (var n in graph.Nodes)
        {
            var note = n.IsCycle ? "Siklus: sudah muncul di atas rantai ini"
                : n.Depth == MaxDepth ? "Batas kedalaman"
                : n.Expanded ? ""
                : "Dependency-nya belum dibaca";
            Graph.Add(new GraphRow(n.Depth, _session.Find(n.Kuid)?.Name ?? n.Kuid.ToString(), n.Kuid.ToString(),
                TargetLabels.Of(_session.TargetState(n.Kuid)), note));
        }
    }

    /// <summary>
    /// Membaca config target langsung agar dependency turunannya terlihat. Memakai jalur baca yang sama
    /// (edit → baca → revert); target yang tidak boleh dibuka melaporkan alasannya.
    /// </summary>
    private async Task ReadTransitiveAsync()
    {
        var failures = new List<string>();
        var read = 0;
        foreach (var target in Rows.Select(r => r.TargetKuid).Distinct())
        {
            var kuid = Kuid.Parse(target);
            if (References.SameAsset(kuid, _workspace.Entry.Kuid)) continue;
            if (_session.Find(kuid) is not { } entry) { failures.Add($"{target}: belum ada di indeks"); continue; }
            var ws = _session.Workspace(entry);
            if (ws.Document is not null) { read++; continue; }
            var r = await _session.AssetReader.ReadSnapshotAsync(entry.Kuid);
            if (r.IsSuccess) { ws.SetSnapshot(r.Value!); read++; }
            else failures.Add($"{entry.Name}: {r.Message}");
        }
        BuildGraph();
        Message = failures.Count == 0 ? $"{read} dependency langsung sudah dibaca."
            : $"{read} terbaca, {failures.Count} belum: " + string.Join(" · ", failures);
    }
}
