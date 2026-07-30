using System.Windows;
using ScanVault.App.ViewModels;

namespace ScanVault.App;

public partial class SmartCollectionDialogWindow : Window
{
    public SmartCollectionDialogWindow()
    {
        InitializeComponent();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SmartCollectionEditorViewModel { CanSave: true })
        {
            DialogResult = true;
            return;
        }

        MessageBox.Show(
            "Smart collection name is required.",
            "ScanVault smart collections",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
