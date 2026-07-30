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
    private const AssetInventoryFilter AllInventoryFilters = AssetInventoryFilter.HasFbx | AssetInventoryFilter.HasLods |
        AssetInventoryFilter.HasBillboard | AssetInventoryFilter.HasAtlas | AssetInventoryFilter.Complete |
        AssetInventoryFilter.Incomplete | AssetInventoryFilter.Ambiguous;

    private readonly IAssetIndex index;
    private readonly ILibraryScanService scanService;
    private readonly IImageLoader imageLoader;
    private readonly IAssetInteractionService interactions;
    private readonly DiagnosticsService diagnosticsService;
    private readonly ISmartCollectionStore smartCollectionStore;
    private readonly ILogger<MainViewModel> logger;
    private readonly ILogger<AssetCardViewModel> cardLogger;
    private readonly ILogger<DiagnosticsViewModel> diagnosticsLogger;
    private readonly ILogger<ScanHistoryViewModel> scanHistoryLogger;
    private IReadOnlyList<AssetSummary> allAssets = [];
    private CancellationTokenSource? scanCancellation;
    private CancellationTokenSource? smartCollectionCountsCancellation;
    private bool suppressCollectionChangeTracking;
    private SmartCollectionItemViewModel? selectedSmartCollection;
    private SmartCollectionRecord? activeSmartCollectionRecord;
    private bool isSmartCollectionModified;
    private bool disposed;
    private string searchText = string.Empty;
    private string? selectedFolderPath;
    private string statusText = "Starting ScanVault…";
    private bool isScanning;
    private int indexedAssetCount;
    private AssetSortMode sortMode = AssetSortMode.NameAscending;
    private AssetInventoryFilter inventoryFilter;
    private AssetCardViewModel? selectedCard;
    private ScanAttemptStatus lastScanStatus;
    private TimeSpan? lastScanDuration;
    private string? lastScanResult;

    public MainViewModel(
        IAssetIndex index,
        ILibraryScanService scanService,
        ISettingsStore settingsStore,
        ISmartCollectionStore smartCollectionStore,
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
        this.smartCollectionStore = smartCollectionStore;
        this.logger = logger;
        cardLogger = loggerFactory.CreateLogger<AssetCardViewModel>();
        diagnosticsLogger = loggerFactory.CreateLogger<DiagnosticsViewModel>();
        scanHistoryLogger = loggerFactory.CreateLogger<ScanHistoryViewModel>();
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
        ToggleHasFbxCommand = new AsyncRelayCommand(token => ToggleFilterAsync(AssetInventoryFilter.HasFbx, token));
        ToggleHasLodsCommand = new AsyncRelayCommand(token => ToggleFilterAsync(AssetInventoryFilter.HasLods, token));
        ToggleHasBillboardCommand = new AsyncRelayCommand(token => ToggleFilterAsync(AssetInventoryFilter.HasBillboard, token));
        ToggleHasAtlasCommand = new AsyncRelayCommand(token => ToggleFilterAsync(AssetInventoryFilter.HasAtlas, token));
        ToggleAllInventoryFiltersCommand = new AsyncRelayCommand(ToggleAllInventoryFiltersAsync);
        ToggleCompleteCommand = new AsyncRelayCommand(token => ToggleFilterAsync(AssetInventoryFilter.Complete, token));
        ToggleIncompleteCommand = new AsyncRelayCommand(token => ToggleFilterAsync(AssetInventoryFilter.Incomplete, token));
        ToggleAmbiguousCommand = new AsyncRelayCommand(token => ToggleFilterAsync(AssetInventoryFilter.Ambiguous, token));
        ApplySmartCollectionCommand = new AsyncRelayCommand(ApplySelectedSmartCollectionAsync, () => SelectedSmartCollection is not null);
        UpdateActiveSmartCollectionCommand = new AsyncRelayCommand(UpdateActiveSmartCollectionAsync, () => CanUpdateActiveSmartCollection);
        DuplicateSmartCollectionCommand = new AsyncRelayCommand(DuplicateSelectedSmartCollectionAsync, () => SelectedSmartCollection is not null);
        DeleteSmartCollectionCommand = new AsyncRelayCommand(DeleteSelectedSmartCollectionAsync, () => SelectedSmartCollection?.IsUser == true);
        MoveSmartCollectionUpCommand = new AsyncRelayCommand(token => MoveSelectedSmartCollectionAsync(-1, token), () => SelectedSmartCollection?.IsUser == true);
        MoveSmartCollectionDownCommand = new AsyncRelayCommand(token => MoveSelectedSmartCollectionAsync(1, token), () => SelectedSmartCollection?.IsUser == true);
        DeactivateSmartCollectionCommand = new RelayCommand(DeactivateSmartCollection, () => IsSmartCollectionActive);
        ResetSmartCollectionCommand = new RelayCommand(ResetActiveSmartCollection, () => IsSmartCollectionModified && IsSmartCollectionActive);
        Preview.PropertyChanged += OnPreviewPropertyChanged;
    }

    public ObservableCollection<AssetCardViewModel> Assets { get; } = [];
    public ObservableCollection<FolderNode> Folders { get; } = [];
    public ObservableCollection<SmartCollectionItemViewModel> BuiltInSmartCollections { get; } = [];
    public ObservableCollection<SmartCollectionItemViewModel> UserSmartCollections { get; } = [];
    public IReadOnlyList<AssetSortOption> SortOptions { get; } =
    [
        new(AssetSortMode.NameAscending, "Name A–Z"),
        new(AssetSortMode.NameDescending, "Name Z–A"),
        new(AssetSortMode.TypeAscending, "Type A–Z"),
        new(AssetSortMode.ResolutionDescending, "Resolution high to low"),
        new(AssetSortMode.ResolutionAscending, "Resolution low to high"),
        new(AssetSortMode.RecentlyModified, "Recently modified"),
        new(AssetSortMode.OldestModified, "Oldest modified"),
        new(AssetSortMode.AssetIdAscending, "Asset ID A–Z"),
        new(AssetSortMode.Completeness, "Completeness"),
        new(AssetSortMode.VariantCountDescending, "Variant count"),
        new(AssetSortMode.LodCountDescending, "LOD count"),
        new(AssetSortMode.TextureSetCountDescending, "Texture-set count")
    ];
    public SettingsViewModel Settings { get; }
    public PreviewViewModel Preview { get; }
    public AsyncRelayCommand SaveSettingsCommand { get; }
    public AsyncRelayCommand RescanCommand { get; }
    public RelayCommand CancelScanCommand { get; }
    public RelayCommand ClosePreviewCommand { get; }
    public AsyncRelayCommand OpenSelectedPreviewCommand { get; }
    public RelayCommand CopySelectedFolderCommand { get; }
    public AsyncRelayCommand ToggleHasFbxCommand { get; }
    public AsyncRelayCommand ToggleHasLodsCommand { get; }
    public AsyncRelayCommand ToggleHasBillboardCommand { get; }
    public AsyncRelayCommand ToggleHasAtlasCommand { get; }
    public AsyncRelayCommand ToggleAllInventoryFiltersCommand { get; }
    public AsyncRelayCommand ToggleCompleteCommand { get; }
    public AsyncRelayCommand ToggleIncompleteCommand { get; }
    public AsyncRelayCommand ToggleAmbiguousCommand { get; }
    public AsyncRelayCommand ApplySmartCollectionCommand { get; }
    public AsyncRelayCommand UpdateActiveSmartCollectionCommand { get; }
    public AsyncRelayCommand DuplicateSmartCollectionCommand { get; }
    public AsyncRelayCommand DeleteSmartCollectionCommand { get; }
    public AsyncRelayCommand MoveSmartCollectionUpCommand { get; }
    public AsyncRelayCommand MoveSmartCollectionDownCommand { get; }
    public RelayCommand DeactivateSmartCollectionCommand { get; }
    public RelayCommand ResetSmartCollectionCommand { get; }
    public event Action<ContentInventoryViewModel>? ContentInventoryRequested;
    public AssetInventoryFilter InventoryFilter
    {
        get => inventoryFilter;
        private set
        {
            if (SetProperty(ref inventoryFilter, value))
            {
                OnPropertyChanged(nameof(FilterHasFbx));
                OnPropertyChanged(nameof(FilterHasLods));
                OnPropertyChanged(nameof(FilterHasBillboard));
                OnPropertyChanged(nameof(FilterHasAtlas));
                OnPropertyChanged(nameof(FilterAll));
                OnPropertyChanged(nameof(FilterComplete));
                OnPropertyChanged(nameof(FilterIncomplete));
                OnPropertyChanged(nameof(FilterAmbiguous));
            }
        }
    }
    public bool FilterHasFbx => InventoryFilter.HasFlag(AssetInventoryFilter.HasFbx);
    public bool FilterHasLods => InventoryFilter.HasFlag(AssetInventoryFilter.HasLods);
    public bool FilterHasBillboard => InventoryFilter.HasFlag(AssetInventoryFilter.HasBillboard);
    public bool FilterHasAtlas => InventoryFilter.HasFlag(AssetInventoryFilter.HasAtlas);
    public bool FilterAll => (InventoryFilter & AllInventoryFilters) == AllInventoryFilters;
    public bool FilterComplete => InventoryFilter.HasFlag(AssetInventoryFilter.Complete);
    public bool FilterIncomplete => InventoryFilter.HasFlag(AssetInventoryFilter.Incomplete);
    public bool FilterAmbiguous => InventoryFilter.HasFlag(AssetInventoryFilter.Ambiguous);
    public string WindowTitle { get; }
    public string ProductVersion { get; }

    public SmartCollectionItemViewModel? SelectedSmartCollection
    {
        get => selectedSmartCollection;
        set
        {
            if (SetProperty(ref selectedSmartCollection, value))
            {
                NotifySmartCollectionCommandStates();
            }
        }
    }

    public bool IsSmartCollectionActive => activeSmartCollectionRecord is not null;
    public bool IsSmartCollectionModified
    {
        get => isSmartCollectionModified;
        private set
        {
            if (SetProperty(ref isSmartCollectionModified, value))
            {
                OnPropertyChanged(nameof(ActiveSmartCollectionStateText));
                ResetSmartCollectionCommand.NotifyCanExecuteChanged();
                UpdateSmartCollectionActiveFlags();
            }
        }
    }

    public bool CanUpdateActiveSmartCollection => IsSmartCollectionActive && IsSmartCollectionModified && activeSmartCollectionRecord?.Kind == SmartCollectionKind.User;
    public string ActiveSmartCollectionName => activeSmartCollectionRecord?.Name ?? "Manual filters";
    public string ActiveSmartCollectionStateText => activeSmartCollectionRecord is null
        ? "Manual filters"
        : IsSmartCollectionModified
            ? $"{activeSmartCollectionRecord.Name} (modified)"
            : activeSmartCollectionRecord.Name;
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
                MarkActiveSmartCollectionModified();
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
                MarkActiveSmartCollectionModified();
                _ = RefreshSmartCollectionCountsAsync(CancellationToken.None);
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
        InventoryFilter = Settings.InventoryFilter;
        allAssets = await index.GetAssetsAsync(cancellationToken);
        await LoadSmartCollectionsAsync(cancellationToken);
        RebuildNavigation();
        await RefreshSmartCollectionCountsAsync(cancellationToken);
        StatusText = DescribeStartupStatus(index.Compatibility, allAssets.Count);
        NotifyCommandStates();
    }

    public SmartCollectionEditorViewModel CreateSmartCollectionEditorForCurrentFilters() =>
        new(
            "Save Smart Collection",
            SuggestedCollectionName(),
            string.Empty,
            string.IsNullOrWhiteSpace(SelectedFolderPath)
                ? SmartCollectionFolderScope.EntireLibrary
                : SmartCollectionFolderScope.SpecificFolder,
            true,
            !string.IsNullOrWhiteSpace(SelectedFolderPath))
        {
            CriteriaSummary = DescribeCurrentCriteria()
        };

    public SmartCollectionEditorViewModel? CreateSmartCollectionEditorForSelected()
    {
        if (SelectedSmartCollection is not { IsUser: true } selected)
        {
            return null;
        }

        return new(
            "Edit Smart Collection",
            selected.Name,
            selected.Description,
            selected.Record.Definition.FolderScope,
            selected.Record.Definition.SortMode is not null,
            !string.IsNullOrWhiteSpace(SelectedFolderPath) || selected.Record.Definition.FolderScope != SmartCollectionFolderScope.SpecificFolder)
        {
            CriteriaSummary = DescribeDefinition(selected.Record.Definition)
        };
    }

    public async Task CreateSmartCollectionAsync(SmartCollectionEditorViewModel editor, CancellationToken cancellationToken = default)
    {
        var name = NormalizeCollectionName(editor.Name);
        if (!ValidateCollectionName(name, null))
        {
            return;
        }

        if (editor.FolderScope == SmartCollectionFolderScope.SpecificFolder && string.IsNullOrWhiteSpace(SelectedFolderPath))
        {
            StatusText = "Select a folder before saving a specific-folder collection.";
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var record = new SmartCollectionRecord(
            $"user-{Guid.NewGuid():N}",
            SmartCollectionKind.User,
            name,
            editor.Description.Trim(),
            SmartCollectionPolicy.FromUiState(
                SearchText,
                InventoryFilter,
                editor.FolderScope,
                Settings.LibraryRoot,
                SelectedFolderPath,
                SortMode,
                editor.SaveSort),
            UserSmartCollections.Count,
            now,
            now);

        UserSmartCollections.Add(new(record));
        await SaveUserCollectionsAsync(cancellationToken);
        SelectedSmartCollection = UserSmartCollections.Last();
        await RefreshSmartCollectionCountsAsync(cancellationToken);
        ApplicationLog.SmartCollectionChanged(logger, "created", record.Id, record.Name);
        StatusText = $"Smart collection saved: {record.Name}.";
    }

    public async Task EditSelectedSmartCollectionAsync(SmartCollectionEditorViewModel editor, CancellationToken cancellationToken = default)
    {
        if (SelectedSmartCollection is not { IsUser: true } selected)
        {
            return;
        }

        var name = NormalizeCollectionName(editor.Name);
        if (!ValidateCollectionName(name, selected.Id))
        {
            return;
        }

        var editedDefinition = selected.Record.Definition with
        {
            FolderScope = editor.FolderScope,
            RelativeFolderPath = editor.FolderScope == SmartCollectionFolderScope.SpecificFolder
                ? selected.Record.Definition.RelativeFolderPath
                : null,
            SortMode = editor.SaveSort ? SortMode : null
        };
        if (editor.FolderScope == SmartCollectionFolderScope.SpecificFolder && string.IsNullOrWhiteSpace(editedDefinition.RelativeFolderPath))
        {
            if (string.IsNullOrWhiteSpace(SelectedFolderPath))
            {
                StatusText = "Select a folder before switching a collection to specific-folder scope.";
                return;
            }

            editedDefinition = SmartCollectionPolicy.FromUiState(
                SearchText,
                InventoryFilter,
                SmartCollectionFolderScope.SpecificFolder,
                Settings.LibraryRoot,
                SelectedFolderPath,
                SortMode,
                editor.SaveSort) with
            {
                SearchText = selected.Record.Definition.SearchText,
                AssetTypes = selected.Record.Definition.AssetTypes,
                HasFbx = selected.Record.Definition.HasFbx,
                HasAbc = selected.Record.Definition.HasAbc,
                HasLods = selected.Record.Definition.HasLods,
                HasVariants = selected.Record.Definition.HasVariants,
                HasAtlas = selected.Record.Definition.HasAtlas,
                HasBillboard = selected.Record.Definition.HasBillboard,
                HasTextureSets = selected.Record.Definition.HasTextureSets,
                HasIssues = selected.Record.Definition.HasIssues,
                CompletenessStatuses = selected.Record.Definition.CompletenessStatuses,
                MinimumResolution = selected.Record.Definition.MinimumResolution,
                MaximumResolution = selected.Record.Definition.MaximumResolution
            };
        }

        var updated = selected.Record with
        {
            Name = name,
            Description = editor.Description.Trim(),
            Definition = editedDefinition,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        selected.UpdateRecord(updated);
        if (activeSmartCollectionRecord?.Id == updated.Id)
        {
            activeSmartCollectionRecord = updated;
            OnPropertyChanged(nameof(ActiveSmartCollectionName));
            OnPropertyChanged(nameof(ActiveSmartCollectionStateText));
        }

        await SaveUserCollectionsAsync(cancellationToken);
        await RefreshSmartCollectionCountsAsync(cancellationToken);
        ApplicationLog.SmartCollectionChanged(logger, "updated", updated.Id, updated.Name);
        StatusText = $"Smart collection updated: {updated.Name}.";
    }

    public async Task DeleteSelectedSmartCollectionAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedSmartCollection is not { IsUser: true } selected)
        {
            return;
        }

        UserSmartCollections.Remove(selected);
        ReorderUserCollections();
        if (activeSmartCollectionRecord?.Id == selected.Id)
        {
            DeactivateSmartCollection();
        }

        await SaveUserCollectionsAsync(cancellationToken);
        await RefreshSmartCollectionCountsAsync(cancellationToken);
        ApplicationLog.SmartCollectionChanged(logger, "deleted", selected.Id, selected.Name);
        StatusText = $"Smart collection deleted: {selected.Name}.";
    }

    private async Task LoadSmartCollectionsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SmartCollectionRecord> userCollections;
        try
        {
            userCollections = await smartCollectionStore.LoadAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ApplicationLog.SmartCollectionsLoadFailed(logger, exception);
            userCollections = [];
            StatusText = $"Smart collections could not be loaded: {exception.Message}";
        }

        BuiltInSmartCollections.Clear();
        foreach (var builtIn in SmartCollectionPolicy.BuiltIns)
        {
            BuiltInSmartCollections.Add(new(builtIn));
        }

        UserSmartCollections.Clear();
        foreach (var collection in userCollections.OrderBy(static collection => collection.Order))
        {
            UserSmartCollections.Add(new(collection));
        }

        UpdateSmartCollectionCompatibility();
    }

    private async Task ApplySelectedSmartCollectionAsync(CancellationToken cancellationToken)
    {
        if (SelectedSmartCollection is null)
        {
            return;
        }

        await ApplySmartCollectionAsync(SelectedSmartCollection, cancellationToken);
    }

    private async Task ApplySmartCollectionAsync(SmartCollectionItemViewModel collection, CancellationToken cancellationToken)
    {
        var validation = SmartCollectionPolicy.Validate(collection.Record.Definition, Settings.LibraryRoot);
        collection.Compatibility = validation.Compatibility;
        collection.CompatibilityMessage = validation.Message;
        if (validation.Compatibility == SmartCollectionCompatibility.UnsupportedDefinition)
        {
            StatusText = $"Cannot apply {collection.Name}: {validation.Message}";
            return;
        }

        suppressCollectionChangeTracking = true;
        try
        {
            activeSmartCollectionRecord = collection.Record;
            SearchText = collection.Record.Definition.SearchText;
            InventoryFilter = ToUiInventoryFilter(collection.Record.Definition);
            if (collection.Record.Definition.SortMode is { } collectionSort)
            {
                SortMode = collectionSort;
            }

            SelectedFolderPath = SmartCollectionPolicy.ResolveFolderPath(
                collection.Record.Definition,
                Settings.LibraryRoot,
                SelectedFolderPath);
            IsSmartCollectionModified = false;
        }
        finally
        {
            suppressCollectionChangeTracking = false;
        }

        RefreshVisibleAssets();
        NotifySmartCollectionCommandStates();
        ApplicationLog.SmartCollectionApplied(logger, collection.Id, collection.Name);
        StatusText = validation.Compatibility == SmartCollectionCompatibility.MissingFolder
            ? $"Applied {collection.Name}; saved folder is missing, so the result is empty until it returns."
            : $"Applied smart collection: {collection.Name}.";
        await RefreshSmartCollectionCountsAsync(cancellationToken);
    }

    private async Task UpdateActiveSmartCollectionAsync(CancellationToken cancellationToken)
    {
        if (activeSmartCollectionRecord is not { Kind: SmartCollectionKind.User } active)
        {
            return;
        }

        var item = UserSmartCollections.FirstOrDefault(collection => collection.Id == active.Id);
        if (item is null)
        {
            return;
        }

        var updatedDefinition = SmartCollectionPolicy.FromUiState(
            SearchText,
            InventoryFilter,
            active.Definition.FolderScope,
            Settings.LibraryRoot,
            SelectedFolderPath,
            SortMode,
            active.Definition.SortMode is not null);
        if (active.Definition.FolderScope == SmartCollectionFolderScope.SpecificFolder &&
            string.IsNullOrWhiteSpace(updatedDefinition.RelativeFolderPath))
        {
            updatedDefinition = updatedDefinition with { RelativeFolderPath = active.Definition.RelativeFolderPath };
        }

        var updated = active with
        {
            Definition = updatedDefinition,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        item.UpdateRecord(updated);
        activeSmartCollectionRecord = updated;
        IsSmartCollectionModified = false;
        await SaveUserCollectionsAsync(cancellationToken);
        await RefreshSmartCollectionCountsAsync(cancellationToken);
        ApplicationLog.SmartCollectionChanged(logger, "conditions-updated", updated.Id, updated.Name);
        StatusText = $"Smart collection conditions updated: {updated.Name}.";
    }

    private async Task DuplicateSelectedSmartCollectionAsync(CancellationToken cancellationToken)
    {
        if (SelectedSmartCollection is null)
        {
            return;
        }

        var source = SelectedSmartCollection.Record;
        var now = DateTimeOffset.UtcNow;
        var copyName = NextCopyName(source.Name);
        var duplicate = source with
        {
            Id = $"user-{Guid.NewGuid():N}",
            Kind = SmartCollectionKind.User,
            Name = copyName,
            Order = UserSmartCollections.Count,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        UserSmartCollections.Add(new(duplicate));
        SelectedSmartCollection = UserSmartCollections.Last();
        await SaveUserCollectionsAsync(cancellationToken);
        await RefreshSmartCollectionCountsAsync(cancellationToken);
        ApplicationLog.SmartCollectionChanged(logger, "duplicated", duplicate.Id, duplicate.Name);
        StatusText = $"Smart collection duplicated: {duplicate.Name}.";
    }

    private async Task MoveSelectedSmartCollectionAsync(int direction, CancellationToken cancellationToken)
    {
        if (SelectedSmartCollection is not { IsUser: true } selected)
        {
            return;
        }

        var oldIndex = UserSmartCollections.IndexOf(selected);
        var newIndex = oldIndex + direction;
        if (newIndex < 0 || newIndex >= UserSmartCollections.Count)
        {
            return;
        }

        UserSmartCollections.Move(oldIndex, newIndex);
        ReorderUserCollections();
        SelectedSmartCollection = selected;
        await SaveUserCollectionsAsync(cancellationToken);
        NotifySmartCollectionCommandStates();
    }

    private void ResetActiveSmartCollection()
    {
        var item = SmartCollectionItems().FirstOrDefault(collection => collection.Id == activeSmartCollectionRecord?.Id);
        if (item is not null)
        {
            _ = ApplySmartCollectionAsync(item, CancellationToken.None);
        }
    }

    private void DeactivateSmartCollection()
    {
        activeSmartCollectionRecord = null;
        IsSmartCollectionModified = false;
        UpdateSmartCollectionActiveFlags();
        OnPropertyChanged(nameof(IsSmartCollectionActive));
        OnPropertyChanged(nameof(ActiveSmartCollectionName));
        OnPropertyChanged(nameof(ActiveSmartCollectionStateText));
        NotifySmartCollectionCommandStates();
        RefreshVisibleAssets();
        StatusText = "Smart collection deactivated.";
    }

    private async Task SaveUserCollectionsAsync(CancellationToken cancellationToken)
    {
        ReorderUserCollections();
        await smartCollectionStore.SaveAsync(UserSmartCollections.Select(static item => item.Record).ToArray(), cancellationToken);
    }

    private void ReorderUserCollections()
    {
        for (var index = 0; index < UserSmartCollections.Count; index++)
        {
            var item = UserSmartCollections[index];
            item.UpdateRecord(item.Record with { Order = index });
        }
    }

    private async Task RefreshSmartCollectionCountsAsync(CancellationToken cancellationToken)
    {
        CancelAndDisposeSmartCollectionCountsCancellation();
        smartCollectionCountsCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var countCancellation = smartCollectionCountsCancellation;
        var token = countCancellation.Token;
        var collections = SmartCollectionItems().Select(static item => item.Record).ToArray();
        var assetsSnapshot = allAssets.ToArray();
        var libraryRoot = Settings.LibraryRoot;
        var currentFolder = SelectedFolderPath;

        foreach (var item in SmartCollectionItems())
        {
            item.Count = -1;
        }

        try
        {
            var counts = await Task.Run(
                () => SmartCollectionPolicy.CountMatches(collections, assetsSnapshot, libraryRoot, currentFolder),
                token);
            if (!ReferenceEquals(smartCollectionCountsCancellation, countCancellation))
            {
                return;
            }

            foreach (var item in SmartCollectionItems())
            {
                if (counts.TryGetValue(item.Id, out var count))
                {
                    item.Count = count;
                }
            }

            UpdateSmartCollectionCompatibility();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ApplicationLog.SmartCollectionCountRefreshFailed(logger, exception);
            StatusText = $"Smart collection counts could not be refreshed: {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(smartCollectionCountsCancellation, countCancellation))
            {
                smartCollectionCountsCancellation = null;
                countCancellation.Dispose();
            }
        }
    }

    private void CancelAndDisposeSmartCollectionCountsCancellation()
    {
        var cancellation = smartCollectionCountsCancellation;
        smartCollectionCountsCancellation = null;
        if (cancellation is null)
        {
            return;
        }

        if (!cancellation.IsCancellationRequested)
        {
            cancellation.Cancel();
        }

        cancellation.Dispose();
    }

    private void UpdateSmartCollectionCompatibility()
    {
        foreach (var item in SmartCollectionItems())
        {
            var validation = SmartCollectionPolicy.Validate(item.Record.Definition, Settings.LibraryRoot);
            item.Compatibility = validation.Compatibility;
            item.CompatibilityMessage = validation.Message;
        }
    }

    private void MarkActiveSmartCollectionModified()
    {
        if (suppressCollectionChangeTracking || activeSmartCollectionRecord is null)
        {
            return;
        }

        IsSmartCollectionModified = true;
        NotifySmartCollectionCommandStates();
    }

    private void UpdateSmartCollectionActiveFlags()
    {
        foreach (var item in SmartCollectionItems())
        {
            item.IsActive = activeSmartCollectionRecord?.Id == item.Id;
            item.IsModified = item.IsActive && IsSmartCollectionModified;
        }
    }

    private void NotifySmartCollectionCommandStates()
    {
        ApplySmartCollectionCommand.NotifyCanExecuteChanged();
        UpdateActiveSmartCollectionCommand.NotifyCanExecuteChanged();
        DuplicateSmartCollectionCommand.NotifyCanExecuteChanged();
        DeleteSmartCollectionCommand.NotifyCanExecuteChanged();
        MoveSmartCollectionUpCommand.NotifyCanExecuteChanged();
        MoveSmartCollectionDownCommand.NotifyCanExecuteChanged();
        DeactivateSmartCollectionCommand.NotifyCanExecuteChanged();
        ResetSmartCollectionCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsSmartCollectionActive));
        OnPropertyChanged(nameof(CanUpdateActiveSmartCollection));
        OnPropertyChanged(nameof(ActiveSmartCollectionName));
        OnPropertyChanged(nameof(ActiveSmartCollectionStateText));
        UpdateSmartCollectionActiveFlags();
    }

    private IEnumerable<SmartCollectionItemViewModel> SmartCollectionItems() =>
        BuiltInSmartCollections.Concat(UserSmartCollections);

    private bool ValidateCollectionName(string name, string? currentId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusText = "Smart collection name is required.";
            return false;
        }

        if (name.Length > 80)
        {
            StatusText = "Smart collection name must be 80 characters or fewer.";
            return false;
        }

        if (UserSmartCollections.Any(collection => collection.Id != currentId &&
            string.Equals(collection.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText = $"Smart collection name already exists: {name}.";
            return false;
        }

        return true;
    }

    private static string NormalizeCollectionName(string value) => value.Trim();

    private string NextCopyName(string baseName)
    {
        for (var index = 1; index < 1000; index++)
        {
            var candidate = index == 1 ? $"{baseName} Copy" : $"{baseName} Copy {index}";
            if (candidate.Length <= 80 && !UserSmartCollections.Any(collection =>
                    string.Equals(collection.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }

        return $"Collection {DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    private string SuggestedCollectionName()
    {
        var baseName = string.IsNullOrWhiteSpace(SearchText) ? "Smart Collection" : SearchText.Trim();
        if (baseName.Length > 64)
        {
            baseName = baseName[..64].Trim();
        }

        return UserSmartCollections.Any(collection => string.Equals(collection.Name, baseName, StringComparison.OrdinalIgnoreCase))
            ? NextCopyName(baseName)
            : baseName;
    }

    private string DescribeCurrentCriteria() => DescribeDefinition(SmartCollectionPolicy.FromUiState(
        SearchText,
        InventoryFilter,
        string.IsNullOrWhiteSpace(SelectedFolderPath) ? SmartCollectionFolderScope.EntireLibrary : SmartCollectionFolderScope.SpecificFolder,
        Settings.LibraryRoot,
        SelectedFolderPath,
        SortMode,
        true));

    private static string DescribeDefinition(SmartCollectionDefinition definition)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(definition.SearchText)) parts.Add($"Search: {definition.SearchText}");
        if (definition.FolderScope != SmartCollectionFolderScope.EntireLibrary) parts.Add($"Folder: {definition.FolderScope}");
        if (definition.HasFbx is true) parts.Add("Has FBX");
        if (definition.HasAbc is true) parts.Add("Has ABC");
        if (definition.HasLods is true) parts.Add("Has LODs");
        if (definition.HasAtlas is true) parts.Add("Atlas");
        if (definition.HasBillboard is true) parts.Add("Billboard");
        if (definition.HasIssues is true) parts.Add("Has issues");
        if (definition.CompletenessStatuses.Count > 0) parts.Add($"Completeness: {string.Join(", ", definition.CompletenessStatuses)}");
        if (definition.SortMode is not null) parts.Add($"Sort: {definition.SortMode}");
        return parts.Count == 0 ? "All indexed assets." : string.Join("; ", parts);
    }

    private static AssetInventoryFilter ToUiInventoryFilter(SmartCollectionDefinition definition)
    {
        var filter = AssetInventoryFilter.None;
        if (definition.HasFbx is true) filter |= AssetInventoryFilter.HasFbx;
        if (definition.HasLods is true) filter |= AssetInventoryFilter.HasLods;
        if (definition.HasBillboard is true) filter |= AssetInventoryFilter.HasBillboard;
        if (definition.HasAtlas is true) filter |= AssetInventoryFilter.HasAtlas;
        if (definition.CompletenessStatuses.Count == 1 && definition.CompletenessStatuses.Contains(AssetCompletenessStatus.Complete)) filter |= AssetInventoryFilter.Complete;
        if (definition.CompletenessStatuses.Count == 1 && definition.CompletenessStatuses.Contains(AssetCompletenessStatus.Ambiguous)) filter |= AssetInventoryFilter.Ambiguous;
        if (definition.CompletenessStatuses.Any(status => status is AssetCompletenessStatus.Usable or AssetCompletenessStatus.Partial or AssetCompletenessStatus.MissingCriticalFiles)) filter |= AssetInventoryFilter.Incomplete;
        return filter;
    }

    public async Task<ScanHistoryViewModel> CreateScanHistoryViewModelAsync(CancellationToken cancellationToken)
    {
        var viewModel = new ScanHistoryViewModel(index, NavigateToAssetFromHistory, scanHistoryLogger);
        await viewModel.LoadAsync(cancellationToken);
        return viewModel;
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

    public Task ToggleAllInventoryFiltersAsync(CancellationToken cancellationToken = default) =>
        SetInventoryFilterAsync(FilterAll ? AssetInventoryFilter.None : AllInventoryFilters, cancellationToken);

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
        MarkActiveSmartCollectionModified();
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

        CancelAndDisposeScanCancellation();
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
                ScanPhase.Inventory =>
                    $"Inventorying asset content\u2026 {scanProgress.ProcessedFiles:N0}/{scanProgress.DiscoveredFiles:N0}",
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
            await RefreshSmartCollectionCountsAsync(cancellationToken);
            attemptStopwatch.Stop();
            lastScanStatus = ScanAttemptStatus.Succeeded;
            lastScanDuration = result.Elapsed;
            lastScanResult = PersistedScanSummary(result);
            StatusText =
                $"Scan complete: +{result.AddedAssets}, ~{result.UpdatedAssets}, -{result.RemovedAssets}; " +
                $"{result.SkippedMalformedFiles} malformed, {result.InaccessibleDirectories.Count} inaccessible, " +
                $"{result.DuplicateGroups.Count} duplicate ID groups; " +
                $"{result.AssetsInventoried} inventoried, {result.MeshFilesFound} meshes, {result.TextureFilesFound} textures, " +
                $"{result.AmbiguousAssets} ambiguous, {result.AssetsMissingCriticalFiles} missing critical; {result.Elapsed:g}.";
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

    private async Task ToggleFilterAsync(AssetInventoryFilter flag, CancellationToken cancellationToken)
    {
        var updated = InventoryFilter ^ flag;
        if ((flag is AssetInventoryFilter.Complete or AssetInventoryFilter.Incomplete or AssetInventoryFilter.Ambiguous) && updated.HasFlag(flag))
        {
            updated &= ~(AssetInventoryFilter.Complete | AssetInventoryFilter.Incomplete | AssetInventoryFilter.Ambiguous);
            updated |= flag;
        }
        await SetInventoryFilterAsync(updated, cancellationToken);
    }

    private async Task SetInventoryFilterAsync(AssetInventoryFilter updated, CancellationToken cancellationToken)
    {
        try
        {
            await Settings.SaveInventoryFilterAsync(updated, cancellationToken);
            InventoryFilter = updated;
            RefreshVisibleAssets();
            MarkActiveSmartCollectionModified();
            StatusText = updated == AssetInventoryFilter.None ? "Inventory filters cleared." : $"Inventory filters: {updated}.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Command cancellation leaves the persisted and visible filters unchanged.
        }
        catch (Exception exception)
        {
            ApplicationLog.InventoryFilterSaveFailed(logger, exception);
            StatusText = $"Inventory filter could not be saved: {exception.Message}";
        }
    }

    private void NavigateToAssetFromHistory(string assetId)
    {
        var asset = allAssets.FirstOrDefault(item => StringComparer.OrdinalIgnoreCase.Equals(item.Id, assetId));
        if (asset is null)
        {
            StatusText = $"Asset {assetId} is no longer in the current index.";
            return;
        }

        SearchText = string.Empty;
        SelectedFolderPath = null;
        RefreshVisibleAssets();
        SelectedCard = Assets.FirstOrDefault(card => StringComparer.OrdinalIgnoreCase.Equals(card.Asset.Id, assetId));
        StatusText = SelectedCard is null
            ? $"Asset {assetId} is hidden by current filters."
            : $"Selected {asset.Name} from scan history.";
    }
    private void RequestContentInventory(AssetSummary asset) =>
        ContentInventoryRequested?.Invoke(new ContentInventoryViewModel(asset, interactions, logger));
    private void CancelScan()
    {
        if (scanCancellation is { IsCancellationRequested: false } cancellation)
        {
            cancellation.Cancel();
        }
    }

    private void CancelAndDisposeScanCancellation()
    {
        var cancellation = scanCancellation;
        scanCancellation = null;
        if (cancellation is null)
        {
            return;
        }

        if (!cancellation.IsCancellationRequested)
        {
            cancellation.Cancel();
        }

        cancellation.Dispose();
    }

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

        if (InventoryFilter != AssetInventoryFilter.None)
        {
            query = query.Where(asset => AssetFiltering.MatchesInventoryFilter(asset, InventoryFilter));
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
                cardLogger,
                RequestContentInventory);
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
        if (disposed)
        {
            return;
        }

        disposed = true;
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        Preview.PropertyChanged -= OnPreviewPropertyChanged;
        CancelAndDisposeScanCancellation();
        CancelAndDisposeSmartCollectionCountsCancellation();
        foreach (var card in Assets)
        {
            card.Dispose();
        }

        Preview.Dispose();
    }
}
