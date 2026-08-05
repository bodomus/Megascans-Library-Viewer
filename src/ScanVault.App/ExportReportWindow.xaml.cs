using System.IO;
using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using ScanVault.App.ViewModels;
using ScanVault.Core.Models;

namespace ScanVault.App;

public partial class ExportReportWindow : Window
{
    public ExportReportWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ExportReportViewModel viewModel) return;
        var filter = viewModel.SelectedFormat switch
        {
            ReportFormat.Csv => "CSV reports (*.csv)|*.csv",
            ReportFormat.Json => "JSON reports (*.json)|*.json",
            ReportFormat.Markdown => "Markdown reports (*.md)|*.md",
            _ => "All files (*.*)|*.*"
        };
        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            CheckPathExists = true,
            DefaultExt = viewModel.DestinationExtension,
            FileName = viewModel.DefaultFileName,
            Filter = filter,
            OverwritePrompt = false,
            Title = "Export ScanVault report",
            ValidateNames = true
        };
        if (dialog.ShowDialog(this) == true) viewModel.DestinationPath = dialog.FileName;
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ExportReportViewModel { CanExport: true } viewModel) return;
        if (File.Exists(viewModel.DestinationPath))
        {
            var result = MessageBox.Show(
                "The destination file already exists. Replace it after the report is generated successfully?",
                "ScanVault report export",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
        }

        await viewModel.ExportAsync();
    }

    private void OnCancelExportClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is ExportReportViewModel viewModel) viewModel.Cancel();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is not ExportReportViewModel viewModel) return;
        if (viewModel.IsExporting)
        {
            var result = MessageBox.Show(
                "An export is running. Cancel it and close the window?",
                "ScanVault report export",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        viewModel.Dispose();
    }
}
