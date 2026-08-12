using System.Windows;
using System.Windows.Input;
using ScanVault.App.ViewModels;

namespace ScanVault.App;

public partial class AssetComparisonWindow : Window
{
    public AssetComparisonWindow()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        DifferencesOnlyCheckBox.Focus();
        if (DataContext is not AssetComparisonViewModel viewModel)
        {
            return;
        }

        viewModel.CloseRequested += OnCloseRequested;
        try
        {
            await viewModel.InitializeAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Closing the window cancels comparison and preview loading.
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnCloseRequested() => Close();

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is AssetComparisonViewModel viewModel)
        {
            viewModel.CloseRequested -= OnCloseRequested;
            viewModel.Dispose();
        }
    }
}
