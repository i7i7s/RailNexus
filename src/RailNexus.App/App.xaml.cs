using System.IO;
using System.Windows;
using System.Windows.Threading;
using RailNexus.App.Core;
using RailNexus.App.Core.ViewModels;
using RailNexus.Core.Process;

namespace RailNexus.App;

public partial class App : Application
{
    private MainViewModel? _main;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        // Lokasi data sama dengan CLI Tahap 0 agar indeks pustaka dan riwayat transaksi dipakai bersama.
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RailNexus");
        var settings = new SettingsStore(Path.Combine(appData, "app-settings.json"));
        if (!File.Exists(settings.Path)) settings.Save();

        var profilePath = Path.Combine(AppContext.BaseDirectory, "profiles", "trs19.json");
        var connections = new TrainzConnectionService(settings.Settings, profilePath, Path.Combine(appData, "tahap0"), new ProcessRunner());

        _main = new MainViewModel(connections, settings, new WpfAudioPlayer());
        MainWindow = new MainWindow { DataContext = _main };
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _main?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Pesan tanpa menyalahkan pengguna; aset tidak ditulis di luar transaksi, jadi aplikasi dapat dilanjutkan.
        MessageBox.Show(MainWindow,
            $"Terjadi kesalahan yang belum tertangani:\n\n{e.Exception.Message}\n\nPerubahan aset hanya ditulis lewat transaksi. Bila sebuah penerapan terputus, jalankan 'railnexus list' lalu 'railnexus reconcile <id>' dari CLI.",
            "RailNexus", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
