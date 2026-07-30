using System.Windows;
using ScanVault.App.ViewModels;

namespace ScanVault.App;

public partial class ScanHistoryWindow : Window
{
    public ScanHistoryWindow() => InitializeComponent();

    private void OnChangeDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is ScanHistoryViewModel viewModel && viewModel.OpenAssetCommand.CanExecute(null))
        {
            viewModel.OpenAssetCommand.Execute(null);
        }
    }
}
