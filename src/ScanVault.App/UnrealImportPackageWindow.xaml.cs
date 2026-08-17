using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using ScanVault.App.ViewModels;

namespace ScanVault.App;

public partial class UnrealImportPackageWindow : Window
{
    public UnrealImportPackageWindow()
    {
        InitializeComponent();
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not UnrealImportPackageViewModel viewModel)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            CheckPathExists = true,
            DefaultExt = viewModel.DestinationExtension,
            FileName = viewModel.DefaultFileName,
            Filter = "ScanVault UE manifests (*.scanvault-ue.json)|*.scanvault-ue.json|JSON files (*.json)|*.json",
            OverwritePrompt = false,
            Title = "Export UE import package",
            ValidateNames = true
        };
        if (!string.IsNullOrWhiteSpace(viewModel.ExportFolder))
        {
            dialog.InitialDirectory = viewModel.ExportFolder;
        }

        if (dialog.ShowDialog(this) == true)
        {
            viewModel.DestinationPath = dialog.FileName;
        }
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not UnrealImportPackageViewModel { CanExport: true } viewModel)
        {
            return;
        }

        if (File.Exists(viewModel.DestinationPath))
        {
            var result = MessageBox.Show(
                "The destination manifest already exists. Replace it after the package is generated successfully?",
                "ScanVault UE import package",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        await viewModel.ExportAsync(CancellationToken.None);
    }

    private void OnCopyManifestClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is UnrealImportPackageViewModel viewModel)
        {
            Clipboard.SetText(viewModel.CopyManifest());
        }
    }

    private void OnOpenExportFolderClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not UnrealImportPackageViewModel viewModel)
        {
            return;
        }

        var folder = viewModel.ExportFolder;
        if (!Directory.Exists(folder))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true
        });
    }
}
