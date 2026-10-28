using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RailNexus.App.Core.ViewModels;

namespace RailNexus.App.Views;

public partial class SoundView : UserControl
{
    public SoundView() => InitializeComponent();

    private void ChooseReplacement_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SoundFileItem item) return;
        var dialog = new OpenFileDialog { Title = "Pilih berkas WAV pengganti", Filter = "WAV (*.wav)|*.wav|Semua berkas (*.*)|*.*" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) item.ChooseReplacement(dialog.FileName);
    }
}
