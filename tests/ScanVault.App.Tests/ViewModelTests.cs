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

    // Unit test: settings save gates rescans and persists sort preferences.
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

    // Unit test: main catalog composition preserves folder, search, sort, and selection behavior.
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

    // Unit test: inventory filter command toggles the complete content-filter set.
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
    // Unit test: index compatibility state controls status messaging and rescan availability.
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

    // Unit test: asset card display fields handle normalized metadata and missing optional rows.
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

    // Unit test: preview state can open and close without loading an image.
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


    // Unit test: main view model disposal is idempotent.
    [Fact]
    public async Task ViewModelDisposeCanRunMoreThanOnce()
    {
        var settingsStore = new MemorySettingsStore(new(root));
        var viewModel = CreateMainViewModel([], settingsStore, new RecordingInteractions());

        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.Dispose();
        viewModel.Dispose();
    }

    // Unit test: preview disposal is idempotent after an open operation.
    [Fact]
    public async Task PreviewDisposeCanRunMoreThanOnceAfterLoading()
    {
        var preview = new PreviewViewModel(new NullImageLoader());
        var asset = CreateAsset("preview-dispose", "Preview Dispose", root, "test");

        await preview.OpenAsync(asset);

        preview.Dispose();
        preview.Dispose();
    }

    // Unit test: asset card disposal is idempotent after thumbnail loading.
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

    // Unit test: comparison tray preserves selection and rotates the third asset deterministically.
    [Fact]
    public async Task ComparisonTrayPreservesSingleSelectionAndUsesFifoForThirdAsset()
    {
        var assets = new[]
        {
            CreateAsset("one", "One", root, "one"),
            CreateAsset("two", "Two", root, "two"),
            CreateAsset("three", "Three", root, "three")
        };
        using var viewModel = CreateMainViewModel(
            assets,
            new MemorySettingsStore(new(root)),
            new RecordingInteractions());
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.SelectedCard = viewModel.Assets.Single(card => card.Asset.Id == "one");
        viewModel.AddSelectedToComparisonCommand.Execute(null);
        Assert.Equal("One", viewModel.ComparisonLeftName);
        Assert.False(viewModel.CanOpenComparison);

        viewModel.SelectedCard = viewModel.Assets.Single(card => card.Asset.Id == "two");
        viewModel.AddSelectedToComparisonCommand.Execute(null);
        Assert.True(viewModel.CanOpenComparison);
        Assert.Equal("Ready to compare", viewModel.ComparisonStateText);
        Assert.Equal("two", viewModel.SelectedCard.Asset.Id);

        viewModel.SelectedCard = viewModel.Assets.Single(card => card.Asset.Id == "three");
        viewModel.AddSelectedToComparisonCommand.Execute(null);
        Assert.Equal("Two", viewModel.ComparisonLeftName);
        Assert.Equal("Three", viewModel.ComparisonRightName);
        Assert.Equal(2, viewModel.ComparisonCount);

        AssetComparisonViewModel? requested = null;
        viewModel.AssetComparisonRequested += comparison => requested = comparison;
        viewModel.OpenComparisonCommand.Execute(null);
        Assert.NotNull(requested);
        requested.Dispose();
    }

    // Unit test: duplicate analysis routes open-file and open-folder actions to distinct targets.
    [Fact]
    public async Task DuplicateAnalysisOpenAssetAndFolderUseDistinctTargets()
    {
        var folder = Path.Combine(root, "DuplicateA");
        Directory.CreateDirectory(folder);
        var asset = CreateAsset("same", "Stone", folder, "rock");
        var result = DuplicateResult([DuplicateGroup(asset)]);
        var interactions = new RecordingInteractions();
        using var viewModel = new DuplicateAnalysisViewModel(
            new NoOpDuplicateAnalysisService(),
            new MemoryIndex([asset], latestDuplicateAnalysis: result),
            new(root),
            [asset],
            interactions,
            static (_, _) => { },
            NullLogger<DuplicateAnalysisViewModel>.Instance);
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.OpenAssetCommand.Execute(null);
        viewModel.OpenFolderCommand.Execute(null);

        Assert.Equal(asset.JsonPath, interactions.OpenedFile);
        Assert.Equal(asset.AssetFolderPath, interactions.OpenedFolder);
    }

    // Regression test: duplicate comparison resolves same-ID members by physical JSON path.
    [Fact]
    public async Task DuplicateAnalysisCompareResolvesSameIdMembersByJsonPath()
    {
        var left = CreateAsset("same", "Stone A", Path.Combine(root, "A"), "rock");
        var right = CreateAsset("SAME", "Stone B", Path.Combine(root, "B"), "rock");
        Directory.CreateDirectory(left.AssetFolderPath);
        Directory.CreateDirectory(right.AssetFolderPath);
        var result = DuplicateResult([DuplicateGroup(left, right)]);
        AssetSummary? comparedLeft = null;
        AssetSummary? comparedRight = null;
        using var viewModel = new DuplicateAnalysisViewModel(
            new NoOpDuplicateAnalysisService(),
            new MemoryIndex([left, right], latestDuplicateAnalysis: result),
            new(root),
            [left, right],
            new RecordingInteractions(),
            (first, second) =>
            {
                comparedLeft = first;
                comparedRight = second;
            },
            NullLogger<DuplicateAnalysisViewModel>.Instance);
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.CompareSelectedPairCommand.Execute(null);

        Assert.Same(left, comparedLeft);
        Assert.Same(right, comparedRight);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private static MainViewModel CreateMainViewModel(
        IReadOnlyList<AssetSummary> assets,
        MemorySettingsStore settingsStore,
        RecordingInteractions interactions,
        IndexCompatibilityInfo? compatibility = null)
    {
        var index = new MemoryIndex(assets, compatibility: compatibility);
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
            new NoOpReportExportService(),
            new MemoryUnrealMaterialProfileStore(),
            new NoOpUnrealImportPackageExportService(),
            new NoOpDuplicateAnalysisService(),
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

    private static DuplicateAnalysisResult DuplicateResult(IReadOnlyList<DuplicateGroupResult> groups)
    {
        var run = new DuplicateAnalysisRun(
            "duplicate-run",
            "library",
            "library",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DuplicateAnalysisStatus.Completed,
            false,
            groups.SelectMany(static group => group.Members).Count(),
            groups.SelectMany(static group => group.Members).Count(),
            0,
            0,
            0,
            TimeSpan.Zero,
            null,
            new(
                groups.Count(static group => group.Category is DuplicateCategory.ExactIdDuplicate or DuplicateCategory.ExactContentDuplicate),
                groups.Count(static group => group.Category == DuplicateCategory.ConflictingIdDuplicate),
                groups.Count(static group => group.Category == DuplicateCategory.ProbableDuplicate),
                groups.Count(static group => group.Category == DuplicateCategory.PartialDuplicate),
                groups.SelectMany(static group => group.Members).Count(),
                groups.Sum(static group => group.EstimatedDuplicateSizeBytes)));
        return new(run, groups);
    }

    private static DuplicateGroupResult DuplicateGroup(params AssetSummary[] assets) =>
        new(
            "group",
            DuplicateCategory.ExactIdDuplicate,
            DuplicateConfidence.Exact,
            [new("Asset ID", "Same normalized Asset ID.")],
            ["Asset ID"],
            [],
            0,
            assets.Select(static asset => new DuplicateGroupMember(
                asset.Id,
                asset.Name,
                asset.AssetType,
                asset.AssetFolderPath,
                asset.AssetFolderPath,
                asset.JsonPath,
                asset.Content.Completeness,
                0,
                0,
                DuplicateHashStatus.NotRequired)).ToArray());

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
        DuplicateAnalysisResult? latestDuplicateAnalysis = null,
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

        public Task<IReadOnlyList<AssetSummary>> GetDuplicateAnalysisSourcesAsync(
            string libraryRoot,
            CancellationToken cancellationToken) =>
            Task.FromResult(assets);

        public Task<DuplicateAnalysisResult?> GetLatestDuplicateAnalysisAsync(
            string libraryRoot,
            bool includeStale,
            CancellationToken cancellationToken) =>
            Task.FromResult(latestDuplicateAnalysis);

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


    private sealed class NoOpReportExportService : IReportExportService
    {
        public Task<ReportExportResult> ExportAsync(ReportExportRequest request, IProgress<ReportProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ReportExportResult(request.DestinationPath, null, request.Assets.Count, 0, 0, TimeSpan.Zero));
    }

    private sealed class MemoryUnrealMaterialProfileStore : IUnrealMaterialProfileStore
    {
        public IReadOnlyList<UnrealMaterialProfile> Profiles { get; private set; } = [];

        public Task<IReadOnlyList<UnrealMaterialProfile>> LoadUserProfilesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Profiles);

        public Task SaveUserProfilesAsync(IReadOnlyList<UnrealMaterialProfile> profiles, CancellationToken cancellationToken)
        {
            Profiles = profiles.ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpUnrealImportPackageExportService : IUnrealImportPackageExportService
    {
        public string Serialize(UnrealImportPackage package) => "{}";

        public Task<UnrealImportPackageExportResult> ExportAsync(
            UnrealImportPackage package,
            string destinationPath,
            CancellationToken cancellationToken) =>
            Task.FromResult(new UnrealImportPackageExportResult(destinationPath, 0));
    }

    private sealed class NoOpDuplicateAnalysisService : IDuplicateAnalysisService
    {
        public Task<DuplicateAnalysisResult> AnalyzeAsync(
            LibrarySettings settings,
            IProgress<DuplicateAnalysisProgress>? progress,
            CancellationToken cancellationToken)
        {
            var run = new DuplicateAnalysisRun(
                "test",
                settings.LibraryRoot,
                settings.LibraryRoot,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                DuplicateAnalysisStatus.Completed,
                false,
                0,
                0,
                0,
                0,
                0,
                TimeSpan.Zero,
                null,
                new(0, 0, 0, 0, 0, 0));
            return Task.FromResult(new DuplicateAnalysisResult(run, []));
        }
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
        public string? OpenedFolder { get; private set; }
        public string? OpenedFile { get; private set; }

        public void CopyText(string text) => CopiedText = text;

        public void OpenFolder(string folderPath) => OpenedFolder = folderPath;
        public void OpenFile(string filePath) => OpenedFile = filePath;
    }
}
