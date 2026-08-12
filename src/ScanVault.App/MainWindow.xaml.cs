using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ScanVault.App.ViewModels;
using ScanVault.Core.Models;

namespace ScanVault.App;

public partial class MainWindow : Window
{
    private MainViewModel? subscribedViewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow
        {
            Owner = this,
            DataContext = DataContext
        };
        window.ShowDialog();
    }

    private async void OnSaveSmartCollectionClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var editor = viewModel.CreateSmartCollectionEditorForCurrentFilters();
        if (ShowSmartCollectionDialog(editor) == true)
        {
            await viewModel.CreateSmartCollectionAsync(editor);
        }
    }

    private async void OnEditSmartCollectionClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var editor = viewModel.CreateSmartCollectionEditorForSelected();
        if (editor is not null && ShowSmartCollectionDialog(editor) == true)
        {
            await viewModel.EditSelectedSmartCollectionAsync(editor);
        }
    }

    private async void OnDeleteSmartCollectionClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { SelectedSmartCollection.IsUser: true } viewModel)
        {
            return;
        }

        var result = MessageBox.Show(
            $"Delete smart collection '{viewModel.SelectedSmartCollection.Name}'?",
            "ScanVault smart collections",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            await viewModel.DeleteSelectedSmartCollectionAsync();
        }
    }

    private bool? ShowSmartCollectionDialog(SmartCollectionEditorViewModel editor)
    {
        var window = new SmartCollectionDialogWindow
        {
            Owner = this,
            DataContext = editor
        };
        return window.ShowDialog();
    }

    private async void OnScanHistoryClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        try
        {
            var history = await viewModel.CreateScanHistoryViewModelAsync(CancellationToken.None);
            var window = new ScanHistoryWindow
            {
                Owner = this,
                DataContext = history
            };
            window.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Scan History could not be opened.{Environment.NewLine}{exception.Message}",
                "ScanVault scan history",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void OnExportReportClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        try
        {
            var export = await viewModel.CreateExportReportViewModelAsync(CancellationToken.None);
            var window = new ExportReportWindow
            {
                Owner = this,
                DataContext = export
            };
            window.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Export Report could not be opened.{Environment.NewLine}{exception.Message}",
                "ScanVault report export",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void OnDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        try
        {
            var diagnostics = await viewModel.CreateDiagnosticsViewModelAsync(
                CancellationToken.None);
            var window = new DiagnosticsWindow
            {
                Owner = this,
                DataContext = diagnostics
            };
            window.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Diagnostics could not be opened.{Environment.NewLine}{exception.Message}",
                "ScanVault diagnostics",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void OnSortSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel ||
            sender is not ComboBox { SelectedValue: AssetSortMode mode } ||
            mode == viewModel.SortMode)
        {
            return;
        }

        try
        {
            await viewModel.ChangeSortAsync(mode);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"The sort preference could not be saved.{Environment.NewLine}{exception.Message}",
                "ScanVault sorting",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnSmartCollectionDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.ApplySmartCollectionCommand.CanExecute(null))
        {
            viewModel.ApplySmartCollectionCommand.Execute(null);
            e.Handled = true;
        }
    }

    private async void OnAssetCardLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AssetCardViewModel card })
        {
            return;
        }

        try
        {
            await card.LoadThumbnailAsync();
        }
        catch (OperationCanceledException)
        {
            // Recycling the card invalidated this image request.
        }
    }

    private void OnAssetCardUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AssetCardViewModel card })
        {
            card.CancelImageLoad();
            card.EndHover();
        }
    }

    private async void OnAssetCardMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AssetCardViewModel card })
        {
            await card.BeginHoverAsync();
        }
    }

    private void OnAssetCardMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AssetCardViewModel card })
        {
            card.EndHover();
        }
    }

    private void OnAssetCardDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: AssetCardViewModel card })
        {
            AssetList.SelectedItem = card;
            if (card.OpenPreviewCommand.CanExecute(null))
            {
                card.OpenPreviewCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

    private void OnAssetCardRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DependencyObject source &&
            ItemsControl.ContainerFromElement(AssetList, source) is ListBoxItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void OnAssetListPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Enter && viewModel.OpenSelectedPreviewCommand.CanExecute(null))
        {
            viewModel.OpenSelectedPreviewCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.C &&
                 Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            var command = viewModel.OpenComparisonCommand.CanExecute(null)
                ? viewModel.OpenComparisonCommand
                : viewModel.AddSelectedToComparisonCommand;
            if (command.CanExecute(null))
            {
                command.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.C &&
                 Keyboard.Modifiers == ModifierKeys.Control &&
                 viewModel.CopySelectedFolderCommand.CanExecute(null))
        {
            // Ctrl+C is scoped to the asset list, so normal TextBox copy remains intact.
            viewModel.CopySelectedFolderCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnFolderSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SelectFolder(e.NewValue as FolderNode);
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.Preview.PropertyChanged -= OnPreviewPropertyChanged;
            subscribedViewModel.AssetComparisonRequested -= OnAssetComparisonRequested;
            subscribedViewModel.ContentInventoryRequested -= OnContentInventoryRequested;
        }

        subscribedViewModel = e.NewValue as MainViewModel;
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.Preview.PropertyChanged += OnPreviewPropertyChanged;
            subscribedViewModel.AssetComparisonRequested += OnAssetComparisonRequested;
            subscribedViewModel.ContentInventoryRequested += OnContentInventoryRequested;
        }
    }

    private void OnContentInventoryRequested(ContentInventoryViewModel viewModel)
    {
        var window = new ContentInventoryWindow
        {
            Owner = this,
            DataContext = viewModel
        };
        window.ShowDialog();
    }
    private void OnAssetComparisonRequested(AssetComparisonViewModel viewModel)
    {
        var window = new AssetComparisonWindow
        {
            Owner = this,
            DataContext = viewModel
        };
        window.Show();
    }
    private void OnPreviewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PreviewViewModel.IsOpen) ||
            subscribedViewModel?.Preview.IsOpen != false)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            AssetList.Focus();
            if (AssetList.SelectedItem is not null &&
                AssetList.ItemContainerGenerator.ContainerFromItem(AssetList.SelectedItem)
                    is ListBoxItem item)
            {
                item.Focus();
            }
        });
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        DataContextChanged -= OnDataContextChanged;
        Closed -= OnClosed;
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.Preview.PropertyChanged -= OnPreviewPropertyChanged;
            subscribedViewModel.AssetComparisonRequested -= OnAssetComparisonRequested;
            subscribedViewModel.ContentInventoryRequested -= OnContentInventoryRequested;
        }
    }
}
