using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RailNexus.App.Core.ViewModels;

namespace RailNexus.App.Views;

public partial class TextureView : UserControl
{
    public TextureView() => InitializeComponent();

    private TextureViewModel? Vm => DataContext as TextureViewModel;

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        // Filter hanya memudahkan; isi berkas tetap diperiksa, bukan ekstensinya.
        var dialog = new OpenFileDialog
        {
            Title = "Pilih gambar sumber",
            Multiselect = true,
            Filter = "Gambar (*.png;*.tga)|*.png;*.tga|Semua berkas (*.*)|*.*",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) Vm?.AddFiles(dialog.FileNames);
    }

    private void PickExport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Pilih folder tujuan hasil konversi" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true && Vm is { } vm) vm.ExportFolder = dialog.FolderName;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TextureSourceItem item) Vm?.Remove(item);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) Vm?.AddFiles(files.Where(System.IO.File.Exists));
    }
}
