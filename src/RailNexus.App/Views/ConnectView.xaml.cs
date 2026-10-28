using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RailNexus.App.Core.ViewModels;

namespace RailNexus.App.Views;

public partial class ConnectView : UserControl
{
    public ConnectView() => InitializeComponent();

    private void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Pilih folder instalasi Trainz (berisi TrainzUtil.exe)" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true && DataContext is ConnectViewModel vm)
            vm.AddFolder(dialog.FolderName);
    }
}
