using System.Windows.Controls;
using System.Windows.Input;
using RailNexus.App.Core.ViewModels;

namespace RailNexus.App.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
        Loaded += (_, _) => Search.Focus();
    }

    private void Open()
    {
        if (DataContext is LibraryViewModel vm && vm.OpenCommand.CanExecute(null)) vm.OpenCommand.Execute(null);
    }

    private void Results_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Open();

    private void Results_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Open();
        e.Handled = true;
    }
}
