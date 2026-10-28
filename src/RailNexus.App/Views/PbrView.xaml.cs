using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RailNexus.App.Core.ViewModels;

namespace RailNexus.App.Views;

public partial class PbrView : UserControl
{
    public PbrView() => InitializeComponent();

    private void PickImage_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ChannelSlot slot) return;
        var dialog = new OpenFileDialog { Title = $"Gambar sumber untuk kanal {slot.Name}", Filter = "Gambar (*.png;*.tga)|*.png;*.tga|Semua berkas (*.*)|*.*" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) slot.ImagePath = dialog.FileName;
    }

    private void PickExport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Pilih folder tujuan" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true && DataContext is PbrViewModel vm) vm.ExportFolder = dialog.FolderName;
    }
}
