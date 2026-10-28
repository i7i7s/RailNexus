using System.Collections.ObjectModel;
using RailNexus.Core;

namespace RailNexus.App.Core.ViewModels;

public sealed class TestKuidItem
{
    public TestKuidItem(string kuid, Action<TestKuidItem> remove)
    {
        Kuid = kuid;
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public string Kuid { get; }
    public RelayCommand RemoveCommand { get; }
}

/// <summary>
/// UI-25 Pengaturan: instalasi aktif, aset uji yang boleh ditulis, lokasi dan ukuran backup,
/// serta kemampuan profil yang sudah diuji. Menyimpan pengaturan tidak menyentuh draft aktif.
/// </summary>
public sealed class SettingsViewModel : ObservableObject, IPage
{
    private readonly SettingsStore _store;
    private readonly AppSession? _session;
    private readonly Action _changeInstallation;
    private string _newKuid = "";
    private string? _kuidError;
    private bool _dirty;
    private string? _savedMessage;

    public SettingsViewModel(SettingsStore store, IConnectionService connections, AppSession? session, Action changeInstallation)
    {
        _store = store;
        _session = session;
        _changeInstallation = changeInstallation;
        foreach (var k in store.Settings.WritableTestKuids) TestKuids.Add(NewItem(k));
        foreach (var c in connections.Capabilities()) Capabilities.Add(c);
        DataRoot = connections.DataRoot;
        AddKuidCommand = new RelayCommand(AddKuid);
        SaveCommand = new RelayCommand(Save, () => _dirty);
        ChangeInstallationCommand = new RelayCommand(changeInstallation);
        Footer = new FooterAction("Simpan pengaturan", SaveCommand, () => _dirty ? "Ada perubahan yang belum disimpan." : _savedMessage ?? "Tidak ada perubahan.");
        ComputeBackup();
    }

    public string Title => "Pengaturan";
    public FooterAction Footer { get; }
    public string? BackLabel => null;
    public void Back() { }

    public string Installation => _session?.ProfileLabel ?? "Belum terhubung";
    public string TrainzFolder => _store.Settings.TrainzFolder ?? "Belum dipilih (dicari di lokasi umum)";
    public string SettingsPath => _store.Path;
    public string DataRoot { get; }
    public string BackupSummary { get; private set; } = "";
    public string Language => "Bahasa Indonesia";

    public ObservableCollection<TestKuidItem> TestKuids { get; } = new();
    public ObservableCollection<Capability> Capabilities { get; } = new();

    public string NewKuid { get => _newKuid; set => Set(ref _newKuid, value ?? ""); }
    public string? KuidError { get => _kuidError; private set => Set(ref _kuidError, value); }

    public RelayCommand AddKuidCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand ChangeInstallationCommand { get; }

    public string WriteNote =>
        "Penulisan aset dibatasi ke KUID di daftar ini sampai profil peran lolos uji di Trainz asli. " +
        "Perubahan daftar berlaku setelah Anda menyambung ulang.";

    private TestKuidItem NewItem(string kuid) => new(kuid, i => { TestKuids.Remove(i); MarkDirty(); });

    private void AddKuid()
    {
        if (!Kuid.TryParse(NewKuid, out var k))
        {
            KuidError = "Tulis KUID lengkap, mis. <kuid:-2:117104311>.";
            return;
        }
        if (TestKuids.Any(t => Kuid.TryParse(t.Kuid, out var e) && e.UserId == k.UserId && e.ContentId == k.ContentId))
        {
            KuidError = "KUID ini sudah ada di daftar.";
            return;
        }
        KuidError = null;
        TestKuids.Add(NewItem(k.ToString()));
        NewKuid = "";
        MarkDirty();
    }

    private void MarkDirty()
    {
        _dirty = true;
        _savedMessage = null;
        SaveCommand.Refresh();
        Footer.Refresh();
    }

    private void Save()
    {
        _store.Settings.WritableTestKuids = TestKuids.Select(t => t.Kuid).ToList();
        try
        {
            _store.Save();
            _dirty = false;
            _savedMessage = "Tersimpan. Sambungkan ulang agar daftar aset uji berlaku.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _savedMessage = $"Pengaturan belum tersimpan: {ex.Message}";
        }
        SaveCommand.Refresh();
        Footer.Refresh();
    }

    private void ComputeBackup()
    {
        if (_session is null) { BackupSummary = "Sambungkan ke Trainz untuk melihat riwayat dan backup."; return; }
        var ids = _session.Store.AllTxIds().ToList();
        var total = ids.Sum(_session.Store.BackupBytes);
        BackupSummary = $"{ids.Count} pekerjaan · backup {HistoryRow.FormatSize(total)}. Backup tidak dihapus otomatis; menghapusnya berarti Undo untuk pekerjaan itu tidak lagi tersedia.";
    }
}
