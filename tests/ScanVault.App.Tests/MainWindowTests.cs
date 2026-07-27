using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using ScanVault.App.Services;
using ScanVault.App.ViewModels;
using ScanVault.Core.Models;

namespace ScanVault.App.Tests;

public sealed class MainWindowTests
{
    [Fact]
    public void RealizesAssetCardWithViewModelOwnedHoverState()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            global::ScanVault.App.App? application = null;
            global::ScanVault.App.MainWindow? window = null;
            try
            {
                application = new();
                application.InitializeComponent();
                using var card = new AssetCardViewModel(
                    CreateAsset(),
                    new NullImageLoader(),
                    new NullInteractions(),
                    static _ => Task.CompletedTask,
                    static _ => { },
                    NullLogger<AssetCardViewModel>.Instance);
                window = new()
                {
                    DataContext = new WindowDataContext([card]),
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    Left = -10_000,
                    Top = -10_000
                };

                window.Show();
                window.UpdateLayout();

                var listBox = Assert.IsType<ListBox>(FindVisualChild<ListBox>(window));
                Assert.NotNull(listBox.ItemContainerGenerator.ContainerFromIndex(0));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                window?.Close();
                application?.Shutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "WPF layout test timed out.");

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            if (FindVisualChild<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private static AssetSummary CreateAsset() =>
        new(
            "xaml-binding",
            "XAML Binding Test",
            "Surface",
            @"C:\fixtures\asset",
            @"C:\fixtures\asset\xaml-binding.json",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            DateTimeOffset.UnixEpoch);

    private sealed record WindowDataContext(IReadOnlyList<AssetCardViewModel> Assets)
    {
        public IReadOnlyList<AssetSortOption> SortOptions { get; } = [];
    }

    private sealed class NullImageLoader : IImageLoader
    {
        public Task<ImageSource?> LoadAsync(
            string? path,
            int decodePixelWidth,
            CancellationToken cancellationToken) =>
            Task.FromResult<ImageSource?>(null);
    }

    private sealed class NullInteractions : IAssetInteractionService
    {
        public void CopyText(string text) { }

        public void OpenFolder(string folderPath) { }
    }
}
