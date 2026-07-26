using System.IO;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using ScanVault.App.Services;
using ScanVault.App.ViewModels;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;

namespace ScanVault.App.Tests;

public sealed class ViewModelTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "ScanVault.App.Tests",
        Guid.NewGuid().ToString("N"));

    public ViewModelTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task SettingsRequireExplicitSaveBeforeRescan()
    {
        var store = new MemorySettingsStore(new(root));
        var viewModel = new SettingsViewModel(store);
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.LibraryRoot = Path.Combine(root, "changed");
        Directory.CreateDirectory(viewModel.LibraryRoot);

        Assert.True(viewModel.IsDirty);
        Assert.False(viewModel.CanRescan);
        Assert.True(await viewModel.SaveAsync(CancellationToken.None));
        Assert.False(viewModel.IsDirty);
        Assert.True(viewModel.CanRescan);
    }

    [Fact]
    public async Task MainViewModelFiltersSelectedFolderAndEnablesCommands()
    {
        var nature = Path.Combine(root, "Nature");
        var urban = Path.Combine(root, "Urban");
        Directory.CreateDirectory(nature);
        Directory.CreateDirectory(urban);
        var assets = new[]
        {
            CreateAsset("nature", nature),
            CreateAsset("urban", urban)
        };
        var settingsStore = new MemorySettingsStore(new(root));
        using var viewModel = new MainViewModel(
            new MemoryIndex(assets),
            new NoOpScanService(),
            settingsStore,
            new NullImageLoader(),
            NullLogger<MainViewModel>.Instance);

        await viewModel.InitializeAsync(CancellationToken.None);
        var rootNode = Assert.Single(viewModel.Folders);
        var natureNode = Assert.Single(rootNode.Children, static node => node.Name == "Nature");
        viewModel.SelectFolder(natureNode);

        var visible = Assert.Single(viewModel.Assets);
        Assert.Equal("nature", visible.Id);
        Assert.True(viewModel.RescanCommand.CanExecute(null));

        viewModel.Settings.LibraryRoot = urban;
        Assert.False(viewModel.RescanCommand.CanExecute(null));
        Assert.True(viewModel.SaveSettingsCommand.CanExecute(null));
    }

    [Fact]
    public async Task PreviewStateOpensAndClosesWithoutRequiringAnImage()
    {
        using var preview = new PreviewViewModel(new NullImageLoader());
        var asset = CreateAsset("preview", root);

        await preview.OpenAsync(asset);

        Assert.True(preview.IsOpen);
        Assert.Same(asset, preview.Asset);
        preview.Close();
        Assert.False(preview.IsOpen);
        Assert.Null(preview.Asset);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private static AssetSummary CreateAsset(string id, string folder) =>
        new(
            id,
            id,
            "surface",
            folder,
            Path.Combine(folder, $"{id}.json"),
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

    private sealed class MemorySettingsStore(LibrarySettings settings) : ISettingsStore
    {
        private LibrarySettings value = settings;

        public Task<LibrarySettings> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(value);

        public Task SaveAsync(LibrarySettings settings, CancellationToken cancellationToken)
        {
            value = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryIndex(IReadOnlyList<AssetSummary> assets) : IAssetIndex
    {
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<AssetSummary>> GetAssetsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(assets);

        public Task<IndexUpdateResult> ReplaceLibraryAsync(
            string libraryRoot,
            IReadOnlyList<AssetSummary> replacement,
            ScanResult draftResult,
            CancellationToken cancellationToken) =>
            Task.FromResult(new IndexUpdateResult(0, 0, 0));
    }

    private sealed class NoOpScanService : ILibraryScanService
    {
        public Task<ScanResult> ScanAsync(
            LibrarySettings settings,
            IProgress<ScanProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ScanResult(0, 0, 0, 0, 0, 0, [], [], [], TimeSpan.Zero));
    }

    private sealed class NullImageLoader : IImageLoader
    {
        public Task<ImageSource?> LoadAsync(
            string? path,
            int decodePixelWidth,
            CancellationToken cancellationToken) =>
            Task.FromResult<ImageSource?>(null);
    }
}
