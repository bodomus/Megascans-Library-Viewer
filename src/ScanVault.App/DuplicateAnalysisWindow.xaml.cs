using System.Windows;
using ScanVault.App.ViewModels;

namespace ScanVault.App;

public partial class DuplicateAnalysisWindow : Window
{
    public DuplicateAnalysisWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        if (DataContext is DuplicateAnalysisViewModel viewModel)
        {
            viewModel.Dispose();
        }
    }
}
