using System.Windows;

namespace RailNexus.App;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void CopyKuid_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveKuid.Text.Length > 0) Clipboard.SetText(ActiveKuid.Text);
    }
}
