using System.Collections.ObjectModel;
using RailNexus.Core.Library;
using RailNexus.Core.Roles;

namespace RailNexus.App.Core.ViewModels;

/// <summary>
/// Kerangka bersama (PRD 6.1): navigasi kiri, bar konteks aset, area kerja, ringkasan perubahan,
/// dan satu tindakan utama di bawah. Aset aktif dan draft tetap ada saat berpindah halaman (AC-37).
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const string NotYet = "Belum tersedia di versi ini.";

    private readonly IConnectionService _connections;
    private AppSession? _session;
    private IPage _page;
    private LibraryViewModel? _library;
    private AssetWorkspace? _active;
    private DraftPanelViewModel? _draft;

    public MainViewModel(IConnectionService connections)
    {
        _connections = connections;
        Nav =
        [
            new NavItem("pustaka", "Pustaka", "Sambungkan ke Trainz terlebih dahulu."),
            new NavItem("dependency", "Dependency", NotYet),
            new NavItem("perbaikan", "Perbaikan", NotYet),
            new NavItem("workshop", "Workshop", NotYet),
            new NavItem("riwayat", "Riwayat", "Sambungkan ke Trainz terlebih dahulu."),
            new NavItem("pengaturan", "Pengaturan", null),
        ];
        NavigateCommand = new ParamCommand<NavItem>(Navigate, n => n.IsEnabled);
        _page = new ConnectViewModel(connections, OnConnected);
        BackCommand = new RelayCommand(() => CurrentPage.Back(), () => CurrentPage.BackLabel is not null);
        MarkActive("pengaturan");
    }

    public IReadOnlyList<NavItem> Nav { get; }
    public ParamCommand<NavItem> NavigateCommand { get; }
    public RelayCommand BackCommand { get; }

    public IPage CurrentPage
    {
        get => _page;
        private set
        {
            if (!Set(ref _page, value)) return;
            BackCommand.Refresh();
            OnPropertyChanged(nameof(ShowDraftPanel));
        }
    }

    public AppSession? Session => _session;

    /// <summary>Pembacaan isi aset yang sedang berjalan di latar belakang (selesai bila tidak ada).</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;
    public DraftPanelViewModel? Draft { get => _draft; private set => Set(ref _draft, value); }

    /// <summary>Aset aktif untuk bar konteks atas; null di Pustaka tanpa pilihan dan di layar sambungkan.</summary>
    public AssetWorkspace? ActiveAsset
    {
        get => _active;
        private set
        {
            if (!Set(ref _active, value)) return;
            OnPropertyChanged(nameof(ActiveStatus));
            OnPropertyChanged(nameof(ShowDraftPanel));
            if (Draft is not null) Draft.Workspace = value;
        }
    }

    public StatusLabel? ActiveStatus => ActiveAsset is null ? null : Labels.Asset(ActiveAsset.Entry.State);
    public bool ShowDraftPanel => ActiveAsset is not null && CurrentPage is EditHubViewModel or ReplaceViewModel;

    public StatusLabel Connection => _session is null ? new("Belum terhubung", Tone.Mute)
        : _session.Runner is null ? new("Terhubung · hanya baca", Tone.Warn)
        : new("Terhubung", Tone.Ok);

    public string ProfileLabel => _session?.ProfileLabel ?? "Belum ada profil";

    private void OnConnected(AppSession session)
    {
        _session?.Dispose();
        _session = session;
        Draft = new DraftPanelViewModel(session);
        // Setelah transaksi, isi aset dibaca ulang agar tugas dan draft berikutnya memakai keadaan terbaru.
        Draft.Applied += (_, _) => { if (CurrentPage is EditHubViewModel hub) Loading = hub.LoadAsync(force: true); };
        Nav[0].DisabledReason = null;
        Nav.Single(n => n.Key == "riwayat").DisabledReason = null;
        OnPropertyChanged(nameof(Session));
        OnPropertyChanged(nameof(Connection));
        OnPropertyChanged(nameof(ProfileLabel));
        ShowLibrary();
    }

    private void Navigate(NavItem item)
    {
        switch (item.Key)
        {
            case "pustaka":
                ShowLibrary();
                break;
            case "riwayat" when _session is not null:
                ActiveAsset = null;
                CurrentPage = new HistoryViewModel(_session);
                MarkActive("riwayat");
                break;
            case "pengaturan":
                ActiveAsset = null;
                CurrentPage = new ConnectViewModel(_connections, OnConnected);
                MarkActive("pengaturan");
                break;
        }
    }

    public void ShowLibrary()
    {
        if (_session is null) return;
        _library ??= new LibraryViewModel(_session, entry => Loading = OpenAssetAsync(entry));
        _library.Refresh();
        ActiveAsset = null;
        CurrentPage = _library;
        MarkActive("pustaka");
    }

    /// <summary>Membuka pusat edit untuk aset dari Pustaka (AC-35). Draft aset yang sama dipakai ulang.</summary>
    public async Task OpenAssetAsync(LibraryEntry entry)
    {
        if (_session is null || Draft is null) return;
        var ws = _session.Workspace(entry);
        ActiveAsset = ws;
        var hub = NewHub(ws);
        CurrentPage = hub;
        MarkActive("pustaka");
        await hub.LoadAsync();
    }

    private void OpenRole(IRole role)
    {
        if (_session is null || Draft is null || ActiveAsset is null) return;
        var ws = ActiveAsset;
        CurrentPage = new ReplaceViewModel(_session, ws, role, Draft.Footer, () =>
        {
            BackToHub(ws);
        });
    }

    private EditHubViewModel NewHub(AssetWorkspace ws) =>
        new(_session!, ws, Draft!.Footer, OpenRole, ShowLibrary, () => OpenAssetHistory(ws));

    private void BackToHub(AssetWorkspace ws)
    {
        ActiveAsset = ws;
        var hub = NewHub(ws);
        CurrentPage = hub;
        MarkActive("pustaka");
        Loading = hub.LoadAsync();
    }

    private void OpenAssetHistory(AssetWorkspace ws)
    {
        if (_session is null) return;
        CurrentPage = new HistoryViewModel(_session, ws.Entry.Kuid, () => BackToHub(ws));
    }

    private void MarkActive(string key)
    {
        foreach (var n in Nav) n.IsActive = n.Key == key;
    }

    public void Dispose() => _session?.Dispose();
}
