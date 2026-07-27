using System.Diagnostics;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ScanVault.App.Presentation;
using ScanVault.App.Services;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.App.ViewModels;

public sealed record AssetSortOption(AssetSortMode Mode, string Label);

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IAssetIndex index;
    private readonly ILibraryScanService scanService;
    private readonly IImageLoader imageLoader;
    private readonly IAssetInteractionService interactions;
    private readonly DiagnosticsService diagnosticsService;
    private readonly ILogger<MainViewModel> logger;
    private readonly ILogger<AssetCardViewModel> cardLogger;
    private readonly ILogger<DiagnosticsViewModel> diagnosticsLogger;
    private IReadOnlyList<AssetSummary> allAssets = [];
    private CancellationTokenSource? scanCancellation;
    private string searchText = string.Empty;
    private string? selectedFolderPath;
    private string statusText = "Starting ScanVault…";
    private bool isScanning;
    private int indexedAssetCount;
    private AssetSortMode sortMode = AssetSortMode.NameAscending;
    private AssetCardViewModel? selectedCard;
    private ScanAttemptStatus lastScanStatus;
    private TimeSpan? lastScanDuration;
    private string? lastScanResult;

    public MainViewModel(
        IAssetIndex index,
        ILibraryScanService scanService,
        ISettingsStore settingsStore,
        IImageLoader imageLoader,
        IAssetInteractionService interactions,
        ApplicationBuildInfo buildInfo,
        DiagnosticsService diagnosticsService,
        ILoggerFactory loggerFactory,
        ILogger<MainViewModel> logger)
    {
        this.index = index;
        this.scanService = scanService;
        this.imageLoader = imageLoader;
        this.interactions = interactions;
        this.diagnosticsService = diagnosticsService;
        this.logger = logger;
        cardLogger = loggerFactory.CreateLogger<AssetCardViewModel>();
        diagnosticsLogger = loggerFactory.CreateLogger<DiagnosticsViewModel>();
        Settings = new(settingsStore);
        WindowTitle = buildInfo.WindowTitle;
        ProductVersion = buildInfo.ProductVersion;
        Preview = new(imageLoader);
        Settings.PropertyChanged += OnSettingsPropertyChanged;

        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, () => Settings.IsDirty);
        RescanCommand = new AsyncRelayCommand(
            RescanAsync,
            () => Settings.CanRescan && !IsScanning && index.Compatibility.CanWrite);
        CancelScanCommand = new RelayCommand(CancelScan, () => IsScanning);
        ClosePreviewCommand = new RelayCommand(Preview.Close, () => Preview.IsOpen);
        OpenSelectedPreviewCommand = new AsyncRelayCommand(
            _ => SelectedCard is null ? Task.CompletedTask : Preview.OpenAsync(SelectedCard.Asset),
            () => SelectedCard is not null);
        CopySelectedFolderCommand = new RelayCommand(
            CopySelectedFolder,
            () => SelectedCard is not null);
        Preview.PropertyChanged += OnPreviewPropertyChanged;
    }

    public ObservableCollection<AssetCardViewModel> Assets { get; } = [];
    public ObservableCollection<FolderNode> Folders { get; } = [];
    public IReadOnlyList<AssetSortOption> SortOptions { get; } =
    [
        new(AssetSortMode.NameAscending, "Name A–Z"),
        new(AssetSortMode.NameDescending, "Name Z–A"),
        new(AssetSortMode.TypeAscending, "Type A–Z"),
        new(AssetSortMode.ResolutionDescending, "Resolution high to low"),
        new(AssetSortMode.ResolutionAscending, "Resolution low to high"),
        new(AssetSortMode.RecentlyModified, "Recently modified"),
        new(AssetSortMode.OldestModified, "Oldest modified"),
        new(AssetSortMode.AssetIdAscending, "Asset ID A–Z")
    ];
    public SettingsViewModel Settings { get; }
    public PreviewViewModel Preview { get; }
    public AsyncRelayCommand SaveSettingsCommand { get; }
    public AsyncRelayCommand RescanCommand { get; }
    public RelayCommand CancelScanCommand { get; }
    public RelayCommand ClosePreviewCommand { get; }
    public AsyncRelayCommand OpenSelectedPreviewCommand { get; }
    public RelayCommand CopySelectedFolderCommand { get; }
    public string WindowTitle { get; }
    public string ProductVersion { get; }
    public ScanAttemptStatus LastScanStatus => lastScanStatus;
    public TimeSpan? LastScanDuration => lastScanDuration;
    public string? LastScanResult => lastScanResult;

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
            {
                RefreshVisibleAssets();
            }
        }
    }

    public AssetSortMode SortMode
    {
        get => sortMode;
        private set => SetProperty(ref sortMode, value);
    }

    public AssetCardViewModel? SelectedCard
    {
        get => selectedCard;
        set
        {
            if (SetProperty(ref selectedCard, value))
            {
                OpenSelectedPreviewCommand.NotifyCanExecuteChanged();
                CopySelectedFolderCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? SelectedFolderPath
    {
        get => selectedFolderPath;
        private set
        {
            if (SetProperty(ref selectedFolderPath, value))
            {
                RefreshVisibleAssets();
            }
        }
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public bool IsScanning
    {
        get => isScanning;
        private set
        {
            if (SetProperty(ref isScanning, value))
            {
                RescanCommand.NotifyCanExecuteChanged();
                CancelScanCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public int IndexedAssetCount
    {
        get => indexedAssetCount;
        private set => SetProperty(ref indexedAssetCount, value);
    }

    public int VisibleAssetCount => Assets.Count;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await index.InitializeAsync(cancellationToken);
        await Settings.LoadAsync(cancellationToken);
        SortMode = Settings.SortMode;
        allAssets = await index.GetAssetsAsync(cancellationToken);
        RebuildNavigation();
        StatusText = DescribeStartupStatus(index.Compatibility, allAssets.Count);
        NotifyCommandStates();
    }

    public async Task<DiagnosticsViewModel> CreateDiagnosticsViewModelAsync(
        CancellationToken cancellationToken)
    {
        var snapshot = await diagnosticsService.CaptureAsync(
            new(
                Settings.LibraryRoot,
                lastScanStatus,
                lastScanDuration,
                lastScanResult,
                SortMode.ToString(),
                SelectedFolderPath),
            cancellationToken);
        return new(snapshot, interactions, diagnosticsLogger);
    }

    private static string DescribeStartupStatus(
        IndexCompatibilityInfo compatibility,
        int assetCount) => compatibility.State switch
        {
            IndexCompatibilityState.RequiresRescan =>
                $"Loaded {assetCount:N0} assets from an older index. Run Rescan to normalize metadata.",
            IndexCompatibilityState.NewerVersionUnsupported =>
                "Index was created by a newer ScanVault version and cannot be opened safely. See Diagnostics.",
            IndexCompatibilityState.Corrupted =>
                "Index could not be read. The database file was preserved. See Diagnostics.",
            IndexCompatibilityState.Missing =>
                "No index exists. Configure a library root and run Rescan.",
            IndexCompatibilityState.RequiresMigration =>
                "Index migration is required before the catalog can be opened.",
            _ when assetCount == 0 =>
                "No assets indexed. Configure a library root and run Rescan.",
            _ => $"Loaded {assetCount:N0} indexed assets."
        };

    public void SelectFolder(FolderNode? folder) =>
        SelectedFolderPath = folder?.FullPath;

    public async Task ChangeSortAsync(
        AssetSortMode value,
        CancellationToken cancellationToken = default)
    {
        if (SortMode == value)
        {
            return;
        }

        await Settings.SaveSortModeAsync(value, cancellationToken);
        SortMode = value;
        RefreshVisibleAssets();
        StatusText = $"Sorted by {SortOptions.First(option => option.Mode == value).Label}.";
    }

    private async Task SaveSettingsAsync(CancellationToken cancellationToken)
    {
        if (await Settings.SaveAsync(cancellationToken))
        {
            StatusText = "Settings saved. Run Rescan to refresh the index.";
        }

        NotifyCommandStates();
    }

    private async Task RescanAsync(CancellationToken commandCancellation)
    {
        if (!index.Compatibility.CanWrite)
        {
            StatusText = index.Compatibility.Guidance;
            return;
        }

        if (!Settings.CanRescan)
        {
            StatusText = Settings.IsDirty
                ? "Save settings before rescanning."
                : Settings.ValidationError ?? "Choose a valid library root.";
            return;
        }

        scanCancellation?.Dispose();
        scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(commandCancellation);
        var cancellationToken = scanCancellation.Token;
        IsScanning = true;
        StatusText = "Scanning library…";
        var attemptStopwatch = Stopwatch.StartNew();

        var progress = new Progress<ScanProgress>(scanProgress =>
        {
            StatusText = scanProgress.Phase switch
            {
                ScanPhase.Discovering =>
                    $"Discovering metadata… {scanProgress.DiscoveredFiles:N0} JSON files",
                ScanPhase.Parsing =>
                    $"Parsing metadata… {scanProgress.ProcessedFiles:N0}/{scanProgress.DiscoveredFiles:N0}",
                ScanPhase.Committing => "Updating SQLite index transaction…",
                ScanPhase.Completed => "Refreshing indexed assets…",
                _ => scanProgress.Phase.ToString()
            };
        });

        try
        {
            var result = await scanService.ScanAsync(Settings.Current, progress, cancellationToken);
            allAssets = await index.GetAssetsAsync(cancellationToken);
            SelectedFolderPath = null;
            RebuildNavigation();
            attemptStopwatch.Stop();
            lastScanStatus = ScanAttemptStatus.Succeeded;
            lastScanDuration = result.Elapsed;
            lastScanResult = PersistedScanSummary(result);
            StatusText =
                $"Scan complete: +{result.AddedAssets}, ~{result.UpdatedAssets}, -{result.RemovedAssets}; " +
                $"{result.SkippedMalformedFiles} malformed, {result.InaccessibleDirectories.Count} inaccessible, " +
                $"{result.DuplicateGroups.Count} duplicate ID groups; {result.Elapsed:g}.";
        }
        catch (OperationCanceledException)
        {
            attemptStopwatch.Stop();
            lastScanStatus = ScanAttemptStatus.Cancelled;
            lastScanDuration = attemptStopwatch.Elapsed;
            lastScanResult = "Cancelled; the previous index remains available";
            StatusText = "Scan cancelled. The previous index remains available.";
        }
        catch (Exception exception)
        {
            attemptStopwatch.Stop();
            lastScanStatus = ScanAttemptStatus.Failed;
            lastScanDuration = attemptStopwatch.Elapsed;
            lastScanResult = "Failed; see application logs for technical details";
            ApplicationLog.ScanCommandFailed(logger, exception);
            StatusText = $"Scan failed: {exception.Message}";
        }
        finally
        {
            IsScanning = false;
            NotifyCommandStates();
        }
    }

    private static string PersistedScanSummary(ScanResult result) =>
        $"+{result.AddedAssets}, ~{result.UpdatedAssets}, -{result.RemovedAssets}; " +
        $"{result.SkippedMalformedFiles + result.SkippedUnrelatedFiles + result.DuplicateGroups.Sum(group => group.SkippedCopyJsonPaths.Count)} skipped, " +
        $"{result.InaccessibleDirectories.Count} inaccessible";

    private void CancelScan() => scanCancellation?.Cancel();

    private void RebuildNavigation()
    {
        IndexedAssetCount = allAssets.Count;
        Folders.Clear();
        if (SettingsValidator.Validate(Settings.Current).IsValid)
        {
            foreach (var root in AssetFiltering.BuildFolderTree(
                         Settings.Current.LibraryRoot,
                         allAssets))
            {
                Folders.Add(root);
            }
        }

        RefreshVisibleAssets();
    }

    private void RefreshVisibleAssets()
    {
        // Preserve selection through filter/sort rebuilds using the immutable source identity.
        string? selectedId = SelectedCard?.Asset.Id;
        string? selectedJsonPath = SelectedCard?.Asset.JsonPath;

        foreach (var card in Assets)
        {
            card.Dispose();
        }

        SelectedCard = null;
        Assets.Clear();
        var query = allAssets.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(SelectedFolderPath))
        {
            query = query.Where(asset =>
                AssetFiltering.IsInFolder(asset, SelectedFolderPath));
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(asset => AssetFiltering.MatchesSearch(asset, SearchText));
        }

        foreach (var asset in AssetSorting.Apply(query, SortMode))
        {
            var card = new AssetCardViewModel(
                asset,
                imageLoader,
                interactions,
                Preview.OpenAsync,
                ReportStatus,
                cardLogger);
            Assets.Add(card);
            if (StringComparer.Ordinal.Equals(asset.Id, selectedId) &&
                StringComparer.OrdinalIgnoreCase.Equals(asset.JsonPath, selectedJsonPath))
            {
                SelectedCard = card;
            }
        }

        OnPropertyChanged(nameof(VisibleAssetCount));
    }

    private void CopySelectedFolder()
    {
        if (SelectedCard is not null)
        {
            SelectedCard.CopyFolderPathCommand.Execute(null);
        }
    }

    private void ReportStatus(string message) => StatusText = message;

    private void OnPreviewPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(PreviewViewModel.IsOpen))
        {
            ClosePreviewCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SettingsViewModel.IsDirty) or
            nameof(SettingsViewModel.CanRescan) or
            nameof(SettingsViewModel.ValidationError))
        {
            NotifyCommandStates();
        }
    }

    private void NotifyCommandStates()
    {
        SaveSettingsCommand.NotifyCanExecuteChanged();
        RescanCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        Preview.PropertyChanged -= OnPreviewPropertyChanged;
        scanCancellation?.Cancel();
        scanCancellation?.Dispose();
        foreach (var card in Assets)
        {
            card.Dispose();
        }

        Preview.Dispose();
    }
}
