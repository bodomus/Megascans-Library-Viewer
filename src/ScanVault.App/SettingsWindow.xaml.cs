using System.IO;
using System.Windows;
using Microsoft.Win32;
using ScanVault.App.ViewModels;

namespace ScanVault.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Choose the Megascans library root",
            Multiselect = false
        };
        if (Directory.Exists(viewModel.Settings.LibraryRoot))
        {
            dialog.InitialDirectory = viewModel.Settings.LibraryRoot;
        }

        if (dialog.ShowDialog(this) == true)
        {
            viewModel.Settings.LibraryRoot = dialog.FolderName;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
