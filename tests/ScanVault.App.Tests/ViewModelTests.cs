using System.IO;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScanVault.App.Services;
using ScanVault.App.ViewModels;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Infrastructure.Configuration;

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
    public async Task CheckAllInventoryFilterTogglesEveryContentFilter()
    {
        var settingsStore = new MemorySettingsStore(new(root));
        using var viewModel = CreateMainViewModel([], settingsStore, new RecordingInteractions());

        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.ToggleAllInventoryFiltersAsync(CancellationToken.None);

        Assert.True(viewModel.FilterAll);
        Assert.True(viewModel.FilterHasFbx);
        Assert.True(viewModel.FilterHasLods);
        Assert.True(viewModel.FilterHasBillboard);
        Assert.True(viewModel.FilterHasAtlas);
        Assert.True(viewModel.FilterComplete);
        Assert.True(viewModel.FilterIncomplete);
        Assert.True(viewModel.FilterAmbiguous);
        Assert.Equal(settingsStore.Value.InventoryFilter, viewModel.InventoryFilter);

        await viewModel.ToggleAllInventoryFiltersAsync(CancellationToken.None);

        Assert.False(viewModel.FilterAll);
        Assert.Equal(AssetInventoryFilter.None, viewModel.InventoryFilter);
        Assert.Equal(AssetInventoryFilter.None, settingsStore.Value.InventoryFilter);
    }
    [Theory]
    [InlineData(IndexCompatibilityState.Missing, true, "No index exists")]
    [InlineData(IndexCompatibilityState.NewerVersionUnsupported, false, "newer ScanVault version")]
    [InlineData(IndexCompatibilityState.Corrupted, false, "database file was preserved")]
    public async Task MainViewModelExplainsCompatibilityAndGatesRescan(
        IndexCompatibilityState state,
        bool canWrite,
        string expectedStatus)
    {
        var settingsStore = new MemorySettingsStore(new(root));
        var compatibility = CompatibilityFor(state, canWrite);
        using var viewModel = CreateMainViewModel(
            [],
            settingsStore,
            new RecordingInteractions(),
            compatibility);

        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.Contains(expectedStatus, viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(canWrite, viewModel.RescanCommand.CanExecute(null));
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


    [Fact]
    public async Task ViewModelDisposeCanRunMoreThanOnce()
    {
        var settingsStore = new MemorySettingsStore(new(root));
        var viewModel = CreateMainViewModel([], settingsStore, new RecordingInteractions());

        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.Dispose();
        viewModel.Dispose();
    }

    [Fact]
    public async Task PreviewDisposeCanRunMoreThanOnceAfterLoading()
    {
        var preview = new PreviewViewModel(new NullImageLoader());
        var asset = CreateAsset("preview-dispose", "Preview Dispose", root, "test");

        await preview.OpenAsync(asset);

        preview.Dispose();
        preview.Dispose();
    }

    [Fact]
    public async Task AssetCardDisposeCanRunMoreThanOnceAfterThumbnailLoad()
    {
        var asset = CreateAsset("card-dispose", "Card Dispose", root, "test");
        var card = new AssetCardViewModel(
            asset,
            new NullImageLoader(),
            new RecordingInteractions(),
            static _ => Task.CompletedTask,
            static _ => { },
            NullLogger<AssetCardViewModel>.Instance);

        await card.LoadThumbnailAsync();

        card.Dispose();
        card.Dispose();
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private static MainViewModel CreateMainViewModel(
        IReadOnlyList<AssetSummary> assets,
        MemorySettingsStore settingsStore,
        RecordingInteractions interactions,
        IndexCompatibilityInfo? compatibility = null)
    {
        var index = new MemoryIndex(assets, compatibility);
        var buildInfo = ApplicationBuildInfo.Create(
            "9.8.7",
            "9.8.7-test+abcdef1",
            "abcdef1",
            "Test",
            "Test runtime",
            "Test OS",
            "X64");
        var paths = new ScanVaultPaths(
            Path.Combine(Path.GetTempPath(), "ScanVault.App.Tests", "index.db"),
            Path.Combine(Path.GetTempPath(), "ScanVault.App.Tests", "settings.json"),
            Path.Combine(Path.GetTempPath(), "ScanVault.App.Tests", "cache"));
        return new(
            index,
            new NoOpScanService(),
            settingsStore,
            new MemorySmartCollectionStore(),
            new NullImageLoader(),
            interactions,
            buildInfo,
            new DiagnosticsService(index, paths, buildInfo),
            NullLoggerFactory.Instance,
            NullLogger<MainViewModel>.Instance);
    }

    private static IndexCompatibilityInfo CompatibilityFor(
        IndexCompatibilityState state,
        bool canWrite) => new(
            state,
            state == IndexCompatibilityState.Missing ? null : 2,
            state == IndexCompatibilityState.Missing ? null : 2,
            IsReadable: state is IndexCompatibilityState.Compatible or IndexCompatibilityState.RequiresRescan,
            CanWrite: canWrite,
            RequiresRescan: state == IndexCompatibilityState.RequiresRescan,
            state switch
            {
                IndexCompatibilityState.Missing => "No index exists. Run Rescan to create it.",
                IndexCompatibilityState.NewerVersionUnsupported =>
                    "Index was created by a newer ScanVault version.",
                IndexCompatibilityState.Corrupted =>
                    "Index could not be read. The database file was preserved.",
                _ => "Index is compatible."
            });

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

    private sealed class MemorySmartCollectionStore : ISmartCollectionStore
    {
        public IReadOnlyList<SmartCollectionRecord> Value { get; private set; } = [];

        public Task<IReadOnlyList<SmartCollectionRecord>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Value);

        public Task SaveAsync(IReadOnlyList<SmartCollectionRecord> collections, CancellationToken cancellationToken)
        {
            Value = collections.ToArray();
            return Task.CompletedTask;
        }
    }

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

    private sealed class MemoryIndex(
        IReadOnlyList<AssetSummary> assets,
        IndexCompatibilityInfo? compatibility = null) : IAssetIndex
    {
        public IndexCompatibilityInfo Compatibility { get; } = compatibility ?? new(
            IndexCompatibilityState.Compatible, 2, 2, true, true, false, "Index is compatible.");
        public bool RequiresNormalizationRescan => Compatibility.RequiresRescan;

        public Task<IndexCompatibilityInfo> InspectCompatibilityAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(Compatibility);
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IndexDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new IndexDiagnostics(Compatibility, assets.Count, null));

        public Task<IReadOnlyList<AssetSummary>> GetAssetsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(assets);

        public Task<string> BeginScanRunAsync(string libraryRoot, string applicationVersion, string commitSha, CancellationToken cancellationToken) =>
            Task.FromResult("scan-run");

        public Task FinishScanRunAsync(string scanRunId, ScanRunStatus status, string? errorMessage, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ScanRunSummary>> GetScanRunsAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScanRunSummary>>([]);

        public Task<IReadOnlyList<ScanChangeSummary>> GetScanChangesAsync(string scanRunId, AssetChangeKind kind, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScanChangeSummary>>([]);

        public Task<IndexUpdateResult> ReplaceLibraryAsync(
            string libraryRoot,
            IReadOnlyList<AssetSummary> replacement,
            ScanResult draftResult,
            string scanRunId,
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
