using System.Windows;

namespace ScanVault.App;

public partial class DiagnosticsWindow : Window
{
    public DiagnosticsWindow() => InitializeComponent();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
