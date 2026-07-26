using System.Windows;
using System.Windows.Input;
using ScanVault.App.ViewModels;
using ScanVault.Core.Models;

namespace ScanVault.App;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow
        {
            Owner = this,
            DataContext = DataContext
        };
        window.ShowDialog();
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

    private void OnFolderSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SelectFolder(e.NewValue as FolderNode);
        }
    }
}
