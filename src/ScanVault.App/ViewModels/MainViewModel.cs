using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ScanVault.App.Presentation;
using ScanVault.App.Services;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IAssetIndex index;
    private readonly ILibraryScanService scanService;
    private readonly IImageLoader imageLoader;
    private readonly ILogger<MainViewModel> logger;
    private IReadOnlyList<AssetSummary> allAssets = [];
    private CancellationTokenSource? scanCancellation;
    private string searchText = string.Empty;
    private string? selectedFolderPath;
    private string statusText = "Starting ScanVault…";
    private bool isScanning;
    private int indexedAssetCount;

    public MainViewModel(
        IAssetIndex index,
        ILibraryScanService scanService,
        ISettingsStore settingsStore,
        IImageLoader imageLoader,
        ILogger<MainViewModel> logger)
    {
        this.index = index;
        this.scanService = scanService;
        this.imageLoader = imageLoader;
        this.logger = logger;
        Settings = new(settingsStore);
        Preview = new(imageLoader);
        Settings.PropertyChanged += OnSettingsPropertyChanged;

        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, () => Settings.IsDirty);
        RescanCommand = new AsyncRelayCommand(RescanAsync, () => Settings.CanRescan && !IsScanning);
        CancelScanCommand = new RelayCommand(CancelScan, () => IsScanning);
        ClosePreviewCommand = new RelayCommand(Preview.Close, () => Preview.IsOpen);
        Preview.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PreviewViewModel.IsOpen))
            {
                ClosePreviewCommand.NotifyCanExecuteChanged();
            }
        };
    }

    public ObservableCollection<AssetCardViewModel> Assets { get; } = [];
    public ObservableCollection<FolderNode> Folders { get; } = [];
    public SettingsViewModel Settings { get; }
    public PreviewViewModel Preview { get; }
    public AsyncRelayCommand SaveSettingsCommand { get; }
    public AsyncRelayCommand RescanCommand { get; }
    public RelayCommand CancelScanCommand { get; }
    public RelayCommand ClosePreviewCommand { get; }

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
        allAssets = await index.GetAssetsAsync(cancellationToken);
        RebuildNavigation();
        StatusText = allAssets.Count == 0
            ? "No assets indexed. Configure a library root and run Rescan."
            : $"Loaded {allAssets.Count:N0} indexed assets.";
    }

    public void SelectFolder(FolderNode? folder) =>
        SelectedFolderPath = folder?.FullPath;

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
            StatusText =
                $"Scan complete: +{result.AddedAssets}, ~{result.UpdatedAssets}, -{result.RemovedAssets}; " +
                $"{result.SkippedMalformedFiles} malformed, {result.InaccessibleDirectories.Count} inaccessible, " +
                $"{result.DuplicateGroups.Count} duplicate ID groups; {result.Elapsed:g}.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled. The previous index remains available.";
        }
        catch (Exception exception)
        {
            ApplicationLog.ScanCommandFailed(logger, exception);
            StatusText = $"Scan failed: {exception.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

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
        foreach (var card in Assets)
        {
            card.Dispose();
        }

        Assets.Clear();
        var query = allAssets.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(SelectedFolderPath))
        {
            query = query.Where(asset =>
                AssetFiltering.IsInFolder(asset, SelectedFolderPath));
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            query = query.Where(asset =>
                asset.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                asset.Id.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                asset.AssetType.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var asset in query)
        {
            Assets.Add(new(asset, imageLoader, Preview.OpenAsync));
        }

        OnPropertyChanged(nameof(VisibleAssetCount));
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
        scanCancellation?.Cancel();
        scanCancellation?.Dispose();
        foreach (var card in Assets)
        {
            card.Dispose();
        }

        Preview.Dispose();
    }
}
