using System.IO;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using ScanVault.App.Presentation;
using ScanVault.App.Services;
using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.App.ViewModels;

public enum ComparisonSide
{
    Left,
    Right
}

public sealed class AssetComparisonViewModel : ObservableObject, IDisposable
{
    private readonly IImageLoader imageLoader;
    private readonly IAssetInteractionService interactions;
    private readonly Func<AssetSummary, Task> openAsset;
    private readonly Action<AssetSummary> openInventory;
    private readonly Func<string, AssetSummary?> resolveCurrentAsset;
    private readonly Action<string> replaceAsset;
    private readonly ILogger<AssetComparisonViewModel> logger;
    private AssetSummary leftAsset;
    private AssetSummary rightAsset;
    private CancellationTokenSource? loadCancellation;
    private AssetComparisonSnapshot? snapshot;
    private ImageSource? leftPreview;
    private ImageSource? rightPreview;
    private bool showDifferencesOnly;
    private bool isLoading;
    private bool isStale;
    private bool disposed;
    private string statusText = "Select two assets to compare.";
    private string? leftError;
    private string? rightError;

    public AssetComparisonViewModel(
        AssetSummary leftAsset,
        AssetSummary rightAsset,
        IImageLoader imageLoader,
        IAssetInteractionService interactions,
        Func<AssetSummary, Task> openAsset,
        Action<AssetSummary> openInventory,
        Func<string, AssetSummary?> resolveCurrentAsset,
        Action<string> replaceAsset,
        ILogger<AssetComparisonViewModel> logger)
    {
        this.leftAsset = leftAsset;
        this.rightAsset = rightAsset;
        this.imageLoader = imageLoader;
        this.interactions = interactions;
        this.openAsset = openAsset;
        this.openInventory = openInventory;
        this.resolveCurrentAsset = resolveCurrentAsset;
        this.replaceAsset = replaceAsset;
        this.logger = logger;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => IsStale && !IsLoading);
        SwapCommand = new AsyncRelayCommand(SwapAsync, () => !IsLoading);
        ReplaceLeftCommand = new RelayCommand(() => RequestReplacement(ComparisonSide.Left));
        ReplaceRightCommand = new RelayCommand(() => RequestReplacement(ComparisonSide.Right));
        OpenLeftAssetCommand = new AsyncRelayCommand(_ => openAsset(this.leftAsset));
        OpenRightAssetCommand = new AsyncRelayCommand(_ => openAsset(this.rightAsset));
        OpenLeftFolderCommand = new RelayCommand(() => interactions.OpenFolder(this.leftAsset.AssetFolderPath));
        OpenRightFolderCommand = new RelayCommand(() => interactions.OpenFolder(this.rightAsset.AssetFolderPath));
        OpenLeftInventoryCommand = new RelayCommand(() => openInventory(this.leftAsset));
        OpenRightInventoryCommand = new RelayCommand(() => openInventory(this.rightAsset));
    }

    public event Action? CloseRequested;
    public event Action<AssetComparisonViewModel>? Disposed;
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SwapCommand { get; }
    public RelayCommand ReplaceLeftCommand { get; }
    public RelayCommand ReplaceRightCommand { get; }
    public AsyncRelayCommand OpenLeftAssetCommand { get; }
    public AsyncRelayCommand OpenRightAssetCommand { get; }
    public RelayCommand OpenLeftFolderCommand { get; }
    public RelayCommand OpenRightFolderCommand { get; }
    public RelayCommand OpenLeftInventoryCommand { get; }
    public RelayCommand OpenRightInventoryCommand { get; }

    public AssetComparisonSnapshot? Snapshot
    {
        get => snapshot;
        private set
        {
            if (SetProperty(ref snapshot, value))
            {
                NotifySnapshotProperties();
            }
        }
    }

    public AssetComparisonHeader LeftHeader => Snapshot?.Left ?? Header(leftAsset);
    public AssetComparisonHeader RightHeader => Snapshot?.Right ?? Header(rightAsset);
    public ImageSource? LeftPreview { get => leftPreview; private set => SetProperty(ref leftPreview, value); }
    public ImageSource? RightPreview { get => rightPreview; private set => SetProperty(ref rightPreview, value); }
    public bool IsLoading { get => isLoading; private set { if (SetProperty(ref isLoading, value)) NotifyCommandStates(); } }
    public bool IsStale { get => isStale; private set { if (SetProperty(ref isStale, value)) NotifyCommandStates(); } }
    public bool HasLeftError => LeftError is not null;
    public bool HasRightError => RightError is not null;
    public string? LeftError { get => leftError; private set { if (SetProperty(ref leftError, value)) OnPropertyChanged(nameof(HasLeftError)); } }
    public string? RightError { get => rightError; private set { if (SetProperty(ref rightError, value)) OnPropertyChanged(nameof(HasRightError)); } }
    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public bool ShowDifferencesOnly
    {
        get => showDifferencesOnly;
        set
        {
            if (SetProperty(ref showDifferencesOnly, value))
            {
                NotifyRowProperties();
                StatusText = value && VisibleRowCount == 0 ? "No differences found." : SummaryText;
            }
        }
    }

    public IReadOnlyList<ComparisonRow> OverviewRows => Filter(Snapshot?.Overview);
    public IReadOnlyList<ComparisonRow> VariantAndLodRows => Filter(Snapshot?.VariantsAndLods);
    public IReadOnlyList<ComparisonRow> TextureRows => Filter(Snapshot?.TextureSets);
    public IReadOnlyList<ComparisonRow> FileRows => Filter(Snapshot?.Files);
    public IReadOnlyList<ComparisonRow> IssueRows => Filter(Snapshot?.Issues);
    public int VisibleRowCount => OverviewRows.Count + VariantAndLodRows.Count + TextureRows.Count + FileRows.Count + IssueRows.Count;
    public string SummaryText => Snapshot is null
        ? StatusText
        : $"Equal: {Snapshot.Summary.Equal}  Different: {Snapshot.Summary.Different}  Only left: {Snapshot.Summary.OnlyLeft}  Only right: {Snapshot.Summary.OnlyRight}  Unknown: {Snapshot.Summary.Unknown}  Ambiguous: {Snapshot.Summary.Ambiguous}";
    public int LeftFileCount { get; private set; }
    public int RightFileCount { get; private set; }
    public long LoadDurationMs { get; private set; }
    public long ComparisonDurationMs { get; private set; }
    public long TotalReadyDurationMs { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken) =>
        await LoadAsync(isRefresh: false, cancellationToken);

    public void MarkStale()
    {
        IsStale = true;
        StatusText = "The index changed after this snapshot was opened. Refresh to compare current data.";
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        CancelAndDisposeLoad();
        Disposed?.Invoke(this);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var refreshedLeft = resolveCurrentAsset(leftAsset.Id);
        var refreshedRight = resolveCurrentAsset(rightAsset.Id);
        LeftError = refreshedLeft is null ? "This asset is no longer present in the current index." : null;
        RightError = refreshedRight is null ? "This asset is no longer present in the current index." : null;
        if (refreshedLeft is null || refreshedRight is null)
        {
            StatusText = "Comparison data could not be loaded.";
            Snapshot = null;
            return;
        }

        leftAsset = refreshedLeft;
        rightAsset = refreshedRight;
        await LoadAsync(isRefresh: true, cancellationToken);
        stopwatch.Stop();
        ComparisonApplicationLog.Refreshed(logger, leftAsset.Id, rightAsset.Id, stopwatch.ElapsedMilliseconds);
    }

    private async Task SwapAsync(CancellationToken cancellationToken)
    {
        (leftAsset, rightAsset) = (rightAsset, leftAsset);
        (LeftPreview, RightPreview) = (RightPreview, LeftPreview);
        (LeftError, RightError) = (RightError, LeftError);
        ComparisonApplicationLog.Swapped(logger, leftAsset.Id, rightAsset.Id);
        await LoadAsync(isRefresh: false, cancellationToken, loadPreviews: false);
    }

    private async Task LoadAsync(bool isRefresh, CancellationToken cancellationToken, bool loadPreviews = true)
    {
        CancelAndDisposeLoad();
        loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = loadCancellation.Token;
        IsLoading = true;
        LeftError = null;
        RightError = null;
        StatusText = isRefresh ? "Refreshing comparison…" : "Loading comparison…";
        var total = Stopwatch.StartNew();
        ComparisonApplicationLog.Opened(logger, leftAsset.Id, rightAsset.Id);
        try
        {
            var load = Stopwatch.StartNew();
            var left = leftAsset;
            var right = rightAsset;
            load.Stop();
            LoadDurationMs = load.ElapsedMilliseconds;

            var comparison = Stopwatch.StartNew();
            var result = await Task.Run(() => AssetComparisonPolicy.Compare(left, right), token);
            comparison.Stop();
            ComparisonDurationMs = comparison.ElapsedMilliseconds;
            token.ThrowIfCancellationRequested();

            Snapshot = result;
            LeftFileCount = result.Files.Count(static row => row.Left.Kind != ComparisonValueKind.Missing);
            RightFileCount = result.Files.Count(static row => row.Right.Kind != ComparisonValueKind.Missing);
            IsStale = false;
            total.Stop();
            TotalReadyDurationMs = total.ElapsedMilliseconds;
            OnPropertyChanged(nameof(LeftFileCount));
            OnPropertyChanged(nameof(RightFileCount));
            OnPropertyChanged(nameof(LoadDurationMs));
            OnPropertyChanged(nameof(ComparisonDurationMs));
            OnPropertyChanged(nameof(TotalReadyDurationMs));
            StatusText = ShowDifferencesOnly && VisibleRowCount == 0 ? "No differences found." : SummaryText;
            ComparisonApplicationLog.Loaded(logger, left.Id, right.Id, LeftFileCount, RightFileCount, result.Summary.DifferenceCount, TotalReadyDurationMs);

            if (loadPreviews)
            {
                await LoadPreviewsAsync(left, right, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Snapshot = null;
            StatusText = "Comparison data could not be loaded.";
            ComparisonApplicationLog.Failed(logger, leftAsset.Id, rightAsset.Id, exception);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadPreviewsAsync(AssetSummary left, AssetSummary right, CancellationToken cancellationToken)
    {
        var leftTask = imageLoader.LoadAsync(left.PreviewPath ?? left.ThumbnailPath, 420, cancellationToken);
        var rightTask = imageLoader.LoadAsync(right.PreviewPath ?? right.ThumbnailPath, 420, cancellationToken);
        var previews = await Task.WhenAll(leftTask, rightTask);
        cancellationToken.ThrowIfCancellationRequested();
        LeftPreview = previews[0];
        RightPreview = previews[1];
    }

    private IReadOnlyList<ComparisonRow> Filter(IReadOnlyList<ComparisonRow>? rows) =>
        rows is null ? [] : AssetComparisonPolicy.FilterDifferences(rows, ShowDifferencesOnly);

    private void RequestReplacement(ComparisonSide side)
    {
        replaceAsset(side == ComparisonSide.Left ? leftAsset.Id : rightAsset.Id);
        CloseRequested?.Invoke();
    }

    private void NotifySnapshotProperties()
    {
        OnPropertyChanged(nameof(LeftHeader));
        OnPropertyChanged(nameof(RightHeader));
        OnPropertyChanged(nameof(SummaryText));
        NotifyRowProperties();
    }

    private void NotifyRowProperties()
    {
        OnPropertyChanged(nameof(OverviewRows));
        OnPropertyChanged(nameof(VariantAndLodRows));
        OnPropertyChanged(nameof(TextureRows));
        OnPropertyChanged(nameof(FileRows));
        OnPropertyChanged(nameof(IssueRows));
        OnPropertyChanged(nameof(VisibleRowCount));
    }

    private void NotifyCommandStates()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        SwapCommand.NotifyCanExecuteChanged();
    }

    private void CancelAndDisposeLoad()
    {
        var cancellation = loadCancellation;
        loadCancellation = null;
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

    private static AssetComparisonHeader Header(AssetSummary asset)
    {
        var path = Path.GetFileName(asset.AssetFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return new(asset.Id, asset.Name, asset.AssetType, path, asset.Content.Completeness, asset.PreviewPath ?? asset.ThumbnailPath);
    }
}
