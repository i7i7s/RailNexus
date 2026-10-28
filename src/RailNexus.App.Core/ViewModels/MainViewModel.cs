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
    private readonly SettingsStore? _settings;
    private readonly IAudioPlayer _audio;
    private AppSession? _session;
    private IPage _page;
    private LibraryViewModel? _library;
    private AssetWorkspace? _active;
    private AssetWorkspace? _lastAsset;
    private DraftPanelViewModel? _draft;

    public MainViewModel(IConnectionService connections, SettingsStore? settings = null, IAudioPlayer? audio = null)
    {
        _connections = connections;
        _settings = settings;
        _audio = audio ?? new NullAudioPlayer();
        Nav =
        [
            new NavItem("pustaka", "Pustaka", "Sambungkan ke Trainz terlebih dahulu."),
            new NavItem("dependency", "Dependency", "Buka aset dari Pustaka terlebih dahulu."),
            new NavItem("perbaikan", "Perbaikan", "Sambungkan ke Trainz terlebih dahulu."),
            new NavItem("workshop", "Workshop", null),
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
            var old = _page;
            if (!Set(ref _page, value)) return;
            (old as IDisposable)?.Dispose();
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
    public bool ShowDraftPanel => ActiveAsset is not null &&
        CurrentPage is EditHubViewModel or ReplaceViewModel or EditorViewModel or DependencyViewModel or RepairViewModel or SoundViewModel or CargoViewModel
            or TextureViewModel { IsAssetMode: true };

    public StatusLabel Connection => _session is null ? new("Belum terhubung", Tone.Mute)
        : _session.Runner is null ? new("Terhubung · hanya baca", Tone.Warn)
        : new("Terhubung", Tone.Ok);

    public string ProfileLabel => _session?.ProfileLabel ?? "Belum ada profil";

    private void OnConnected(AppSession session)
    {
        // Draft tetap milik profil yang sama (AC-21); menyambung ulang ke profil itu tidak menghapusnya.
        if (_session is { } old)
        {
            if (old.ProfileLabel == session.ProfileLabel)
                foreach (var (k, ws) in old.Workspaces)
                {
                    ws.InvalidateConfig();
                    session.Workspaces[k] = ws;
                }
            old.Dispose();
        }
        _session = session;
        _library = null;
        if (_lastAsset is { } last && !session.Workspaces.ContainsValue(last))
        {
            _lastAsset = null;
            Nav.Single(n => n.Key == "dependency").DisabledReason = "Buka aset dari Pustaka terlebih dahulu.";
        }
        if (_settings is not null && session.InstallationFolder is { } folder && _settings.Settings.TrainzFolder != folder)
        {
            _settings.Settings.TrainzFolder = folder;
            try { _settings.Save(); } catch (IOException) { } // pengaturan folder hanya kenyamanan
        }
        Draft = new DraftPanelViewModel(session);
        // Setelah transaksi, isi aset dibaca ulang agar tugas dan draft berikutnya memakai keadaan terbaru.
        Draft.Applied += (_, _) => { if (CurrentPage is EditHubViewModel hub) Loading = hub.LoadAsync(force: true); };
        Nav[0].DisabledReason = null;
        Nav.Single(n => n.Key == "riwayat").DisabledReason = null;
        Nav.Single(n => n.Key == "perbaikan").DisabledReason = null;
        OnPropertyChanged(nameof(Session));
        OnPropertyChanged(nameof(Connection));
        OnPropertyChanged(nameof(ProfileLabel));
        ShowLibrary();
    }

    private void Navigate(NavItem item)
    {
        // Berpindah halaman atau profil ditunda selama transaksi tulis berjalan (PRD 7).
        if (Draft?.ApplyCommand.IsRunning == true) return;
        switch (item.Key)
        {
            case "pustaka":
                ShowLibrary();
                break;
            case "workshop":
                ActiveAsset = null;
                CurrentPage = new WorkshopViewModel(OpenTextureFolder, _session is null ? null : () =>
                    CurrentPage = new BatchViewModel(_session, Path.Combine(_connections.DataRoot, "presets")));
                MarkActive("workshop");
                break;
            case "dependency" when _session is not null && _lastAsset is { } last:
                ActiveAsset = last;
                CurrentPage = new DependencyViewModel(_session, last, Draft!.Footer, AssetNav(last));
                MarkActive("dependency");
                break;
            case "perbaikan" when _session is not null:
                ActiveAsset = null;
                CurrentPage = new RepairListViewModel(_session, entry =>
                {
                    var ws = _session.Workspace(entry);
                    OpenRepair(ws);
                    if (ws.Document is null) Loading = ((RepairViewModel)CurrentPage).CheckCommand.ExecuteAsync();
                });
                MarkActive("perbaikan");
                break;
            case "riwayat" when _session is not null:
                ActiveAsset = null;
                CurrentPage = new HistoryViewModel(_session);
                MarkActive("riwayat");
                break;
            case "pengaturan":
                ActiveAsset = null;
                CurrentPage = _settings is null || _session is null
                    ? new ConnectViewModel(_connections, OnConnected)
                    : new SettingsViewModel(_settings, _connections, _session, ShowConnect);
                MarkActive("pengaturan");
                break;
        }
    }

    private void ShowConnect()
    {
        ActiveAsset = null;
        CurrentPage = new ConnectViewModel(_connections, OnConnected);
        MarkActive("pengaturan");
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
        RememberAsset(ws);
        var hub = NewHub(ws);
        CurrentPage = hub;
        MarkActive("pustaka");
        await hub.LoadAsync();
    }

    private void OpenRole(IRole role)
    {
        if (_session is null || Draft is null || ActiveAsset is null) return;
        var ws = ActiveAsset;
        CurrentPage = new ReplaceViewModel(_session, ws, role, Draft.Footer, () => BackToHub(ws), _audio);
    }

    private string StagingRoot => Path.Combine(_connections.DataRoot, "staging");

    private EditHubViewModel NewHub(AssetWorkspace ws) =>
        new(_session!, ws, Draft!.Footer, new HubActions(
            OpenRole, ShowLibrary,
            OpenHistory: () => OpenAssetHistory(ws),
            OpenEditor: () => OpenEditor(ws),
            OpenTexture: () => OpenTexture(ws),
            OpenDependency: () => CurrentPage = new DependencyViewModel(_session!, ws, Draft!.Footer, AssetNav(ws)),
            OpenRepair: () => OpenRepair(ws),
            OpenSound: () => CurrentPage = new SoundViewModel(ws, _audio, Draft!.Footer, () => BackToHub(ws)),
            OpenCargo: () => CurrentPage = new CargoViewModel(_session!, ws, Draft!.Footer, () => BackToHub(ws))));

    private AssetNavigation AssetNav(AssetWorkspace ws) =>
        new(OpenRole, entry => Loading = OpenAssetAsync(entry), () => OpenTexture(ws), () => OpenEditor(ws), () => BackToHub(ws));

    private void OpenTexture(AssetWorkspace ws) => CurrentPage = new TextureViewModel(ws, StagingRoot, () => BackToHub(ws));

    private void OpenRepair(AssetWorkspace ws)
    {
        ActiveAsset = ws;
        CurrentPage = new RepairViewModel(_session!, ws, Draft!.Footer, AssetNav(ws));
        MarkActive("perbaikan");
    }

    private void OpenTextureFolder()
    {
        ActiveAsset = null;
        CurrentPage = new TextureViewModel(null, StagingRoot, () => Navigate(Nav.Single(n => n.Key == "workshop")));
        MarkActive("workshop");
    }

    private void OpenEditor(AssetWorkspace ws)
    {
        if (Draft is null) return;
        CurrentPage = new EditorViewModel(ws, Draft.Footer, () => BackToHub(ws));
    }

    private void RememberAsset(AssetWorkspace ws)
    {
        _lastAsset = ws;
        Nav.Single(n => n.Key == "dependency").DisabledReason = null;
    }

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
