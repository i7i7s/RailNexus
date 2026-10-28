using System.Collections.ObjectModel;

namespace RailNexus.App.Core.ViewModels;

public sealed class InstallationItem(InstallationCandidate candidate) : ObservableObject
{
    public InstallationCandidate Candidate => candidate;
    public string Name => candidate.Name;
    public string Folder => candidate.Folder;
    public StatusLabel ToolStatus => candidate.ToolFound
        ? new("Alat Trainz ditemukan", Tone.Ok)
        : new("TrainzUtil.exe tidak ditemukan", Tone.Err);
}

/// <summary>UI-01 Sambungkan Trainz (PRD 7): pilih instalasi → periksa → hubungkan.</summary>
public sealed class ConnectViewModel : ObservableObject, IPage
{
    private readonly IConnectionService _service;
    private readonly Action<AppSession> _connected;
    private InstallationItem? _selected;
    private ConnectionCheck? _check;
    private bool _checking;

    public ConnectViewModel(IConnectionService service, Action<AppSession> connected)
    {
        _service = service;
        _connected = connected;
        CheckCommand = new AsyncCommand(CheckAsync, () => Selected is not null);
        ConnectCommand = new RelayCommand(Connect, () => Check?.CanOpen == true && !_checking);
        Footer = new FooterAction("Hubungkan", ConnectCommand, () => ConnectHint);
        foreach (var c in service.FindInstallations()) Installations.Add(new InstallationItem(c));
        Selected = Installations.FirstOrDefault();
    }

    public string Title => "Sambungkan RailNexus ke Trainz";
    public FooterAction Footer { get; }
    public string? BackLabel => null;
    public void Back() { }

    public ObservableCollection<InstallationItem> Installations { get; } = new();

    public InstallationItem? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            Check = null;
            CheckCommand.Refresh();
        }
    }

    public ConnectionCheck? Check
    {
        get => _check;
        private set
        {
            Set(ref _check, value);
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(Steps));
            OnPropertyChanged(nameof(ConnectHint));
            ConnectCommand.Refresh();
            Footer.Refresh();
        }
    }

    public bool IsChecking { get => _checking; private set { Set(ref _checking, value); OnPropertyChanged(nameof(Status)); } }

    public IReadOnlyList<CheckStep> Steps => Check?.Steps ?? [];

    public StatusLabel Status => IsChecking ? new("Memeriksa…", Tone.Info) : Check?.State switch
    {
        null or ConnectionState.NotChecked => new("Belum diperiksa", Tone.Mute),
        ConnectionState.Connected => new(Check.Message, Tone.Ok),
        ConnectionState.ReadOnly => new(Check.Message, Tone.Warn),
        ConnectionState.TrainzNotRunning => new("Trainz belum tersambung", Tone.Warn),
        ConnectionState.ToolNotFound => new("Alat Trainz tidak ditemukan", Tone.Err),
        _ => new("Pemeriksaan gagal", Tone.Err),
    };

    public string ConnectHint => Selected is null ? "Pilih instalasi terlebih dahulu."
        : Check is null ? "Periksa koneksi terlebih dahulu."
        : Check.CanOpen ? $"Profil terpilih: {Selected.Name}"
        : Check.Message;

    public AsyncCommand CheckCommand { get; }
    public RelayCommand ConnectCommand { get; }

    /// <summary>Folder dari dialog "Pilih folder instalasi lain…".</summary>
    public void AddFolder(string folder)
    {
        var existing = Installations.FirstOrDefault(i => string.Equals(i.Folder.TrimEnd('\\', '/'), folder.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new InstallationItem(_service.Inspect(folder));
            Installations.Add(existing);
        }
        Selected = existing;
    }

    private async Task CheckAsync()
    {
        if (Selected is null) return;
        IsChecking = true;
        ConnectCommand.Refresh();
        try { Check = await _service.CheckAsync(Selected.Candidate); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            Check = new ConnectionCheck(ConnectionState.Failed, "", $"Pemeriksaan gagal: {ex.Message}",
                [new CheckStep(false, "Pemeriksaan tidak dapat diselesaikan", ex.Message)]);
        }
        finally
        {
            IsChecking = false;
            ConnectCommand.Refresh();
        }
    }

    private void Connect()
    {
        if (Selected is null || Check is not { CanOpen: true } check) return;
        _connected(_service.Open(Selected.Candidate, check));
    }
}
