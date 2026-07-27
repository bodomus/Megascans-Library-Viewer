using System.IO;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
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
    public async Task SettingsRequireExplicitSaveBeforeRescanAndPersistSort()
    {
        var store = new MemorySettingsStore(new(root));
        var viewModel = new SettingsViewModel(store);
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.LibraryRoot = Path.Combine(root, "changed");
        Directory.CreateDirectory(viewModel.LibraryRoot);

        Assert.True(viewModel.IsDirty);
        Assert.False(viewModel.CanRescan);
        Assert.True(await viewModel.SaveAsync(CancellationToken.None));
        await viewModel.SaveSortModeAsync(
            AssetSortMode.ResolutionDescending,
            CancellationToken.None);
        Assert.False(viewModel.IsDirty);
        Assert.True(viewModel.CanRescan);
        Assert.Equal(AssetSortMode.ResolutionDescending, store.Value.SortMode);
    }

    [Fact]
    public async Task MainViewModelComposesFolderSearchSortAndPreservesSelection()
    {
        var nature = Path.Combine(root, "Nature");
        var urban = Path.Combine(root, "Urban");
        Directory.CreateDirectory(nature);
        Directory.CreateDirectory(urban);
        var assets = new[]
        {
            CreateAsset("b", "Mossy Boulder", nature, "rock"),
            CreateAsset("a", "Forest Fern", nature, "plant"),
            CreateAsset("urban", "Brick Wall", urban, "brick")
        };
        var settingsStore = new MemorySettingsStore(new(root));
        var interactions = new RecordingInteractions();
        using var viewModel = CreateMainViewModel(assets, settingsStore, interactions);

        await viewModel.InitializeAsync(CancellationToken.None);
        Assert.Equal("9.8.7", viewModel.ProductVersion);
        Assert.Equal("ScanVault \u2014 Megascans Library Viewer 9.8.7", viewModel.WindowTitle);
        var rootNode = Assert.Single(viewModel.Folders);
        Assert.Equal(3, rootNode.AssetCount);
        var natureNode = Assert.Single(rootNode.Children, static node => node.Name == "Nature");
        Assert.Equal(2, natureNode.AssetCount);
        viewModel.SelectFolder(natureNode);
        Assert.Equal(["a", "b"], viewModel.Assets.Select(card => card.Asset.Id));

        viewModel.SelectedCard = viewModel.Assets[1];
        await viewModel.ChangeSortAsync(AssetSortMode.NameDescending);
        Assert.Equal("b", viewModel.SelectedCard?.Asset.Id);

        viewModel.SearchText = "rock";
        var visible = Assert.Single(viewModel.Assets);
        Assert.Equal("b", visible.Asset.Id);
        Assert.True(viewModel.RescanCommand.CanExecute(null));

        viewModel.CopySelectedFolderCommand.Execute(null);
        Assert.Equal(nature, interactions.CopiedText);
    }

    [Fact]
    public async Task CardFormatsNormalizedHierarchyAndOmitsMissingRows()
    {
        var asset = CreateAsset("ExactCase", "Stone", root, "debris") with
        {
            MaxResolution = new ImageResolution(4096, 2048)
        };
        var interactions = new RecordingInteractions();
        using var card = new AssetCardViewModel(
            asset,
            new NullImageLoader(),
            interactions,
            static _ => Task.CompletedTask,
            static _ => { },
            NullLogger<AssetCardViewModel>.Instance);

        Assert.Equal("ID: ExactCase", card.IdDisplay);
        Assert.Contains("Surface", card.TypeAndCategory, StringComparison.Ordinal);
        Assert.Contains("4096 × 2048", card.CompactDetails, StringComparison.Ordinal);
        Assert.False(card.HasBiome);
        Assert.False(card.HasRegion);
        card.CopyAssetIdCommand.Execute(null);
        Assert.Equal("ExactCase", interactions.CopiedText);
    }

    [Fact]
    public async Task PreviewStateOpensAndClosesWithoutRequiringAnImage()
    {
        using var preview = new PreviewViewModel(new NullImageLoader());
        var asset = CreateAsset("preview", "Preview", root, "test");

        await preview.OpenAsync(asset);

        Assert.True(preview.IsOpen);
        Assert.Same(asset, preview.Asset);
        preview.Close();
        Assert.False(preview.IsOpen);
        Assert.Null(preview.Asset);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private static MainViewModel CreateMainViewModel(
        IReadOnlyList<AssetSummary> assets,
        MemorySettingsStore settingsStore,
        RecordingInteractions interactions) => new(
            new MemoryIndex(assets),
            new NoOpScanService(),
            settingsStore,
            new NullImageLoader(),
            interactions,
            ApplicationBuildInfo.Create(
                "9.8.7",
                "9.8.7-test+abcdef1",
                "abcdef1",
                "Test",
                "Test runtime",
                "Test OS",
                "X64"),
            NullLoggerFactory.Instance,
            NullLogger<MainViewModel>.Instance);

    private static AssetSummary CreateAsset(
        string id,
        string name,
        string folder,
        string tag) =>
        new(
            id,
            name,
            "Surface",
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
            [tag],
            [new AssetTag(AssetTagKind.Descriptive, tag)],
            DateTimeOffset.UnixEpoch);

    private sealed class MemorySettingsStore(LibrarySettings settings) : ISettingsStore
    {
        public LibrarySettings Value { get; private set; } = settings;

        public Task<LibrarySettings> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Value);

        public Task SaveAsync(LibrarySettings settings, CancellationToken cancellationToken)
        {
            Value = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryIndex(IReadOnlyList<AssetSummary> assets) : IAssetIndex
    {
        public bool RequiresNormalizationRescan => false;
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

    private sealed class RecordingInteractions : IAssetInteractionService
    {
        public string? CopiedText { get; private set; }

        public void CopyText(string text) => CopiedText = text;

        public void OpenFolder(string folderPath) { }
    }
}
