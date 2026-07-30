using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using ScanVault.App.Presentation;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;

namespace ScanVault.App.ViewModels;

public sealed record AssetChangeKindOption(AssetChangeKind Kind, string Label);

public sealed class ScanHistoryViewModel : ObservableObject
{
    private const int RunLimit = 100;
    private const int ChangeLimit = 10000;
    private readonly IAssetIndex index;
    private readonly Action<string> navigateToAsset;
    private readonly ILogger<ScanHistoryViewModel> logger;
    private ScanRunSummary? selectedRun;
    private ScanChangeSummary? selectedChange;
    private AssetChangeKind selectedKind = AssetChangeKind.Added;
    private string statusText = "Loading scan history...";
    private bool isLoading;

    public ScanHistoryViewModel(IAssetIndex index, Action<string> navigateToAsset, ILogger<ScanHistoryViewModel> logger)
    {
        this.index = index;
        this.navigateToAsset = navigateToAsset;
        this.logger = logger;
        OpenAssetCommand = new RelayCommand(OpenSelectedAsset, () => SelectedChange is { AssetExists: true, Kind: not AssetChangeKind.Removed });
    }

    public ObservableCollection<ScanRunSummary> Runs { get; } = [];
    public ObservableCollection<ScanChangeSummary> Changes { get; } = [];
    public IReadOnlyList<AssetChangeKindOption> ChangeKinds { get; } =
    [
        new(AssetChangeKind.Added, "Added"),
        new(AssetChangeKind.Changed, "Changed"),
        new(AssetChangeKind.Removed, "Removed"),
        new(AssetChangeKind.Unchanged, "Unchanged")
    ];
    public RelayCommand OpenAssetCommand { get; }

    public ScanRunSummary? SelectedRun
    {
        get => selectedRun;
        set
        {
            if (SetProperty(ref selectedRun, value))
            {
                _ = LoadChangesAsync(CancellationToken.None);
            }
        }
    }

    public ScanChangeSummary? SelectedChange
    {
        get => selectedChange;
        set
        {
            if (SetProperty(ref selectedChange, value)) OpenAssetCommand.NotifyCanExecuteChanged();
        }
    }

    public AssetChangeKind SelectedKind
    {
        get => selectedKind;
        set
        {
            if (SetProperty(ref selectedKind, value)) _ = LoadChangesAsync(CancellationToken.None);
        }
    }

    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public bool IsLoading { get => isLoading; private set => SetProperty(ref isLoading, value); }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            Runs.Clear();
            foreach (var run in await index.GetScanRunsAsync(RunLimit, cancellationToken).ConfigureAwait(true)) Runs.Add(run);
            SelectedRun = Runs.FirstOrDefault();
            StatusText = Runs.Count == 0 ? "No scan history yet. Run Rescan to create the initial baseline." : $"Loaded {Runs.Count} scan runs.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ApplicationLog.ScanHistoryLoadFailed(logger, exception);
            StatusText = $"Scan history could not be loaded: {exception.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadChangesAsync(CancellationToken cancellationToken)
    {
        Changes.Clear();
        SelectedChange = null;
        if (SelectedRun is null)
        {
            StatusText = "No scan history yet. Run Rescan to create the initial baseline.";
            return;
        }
        if (SelectedRun.Status != ScanRunStatus.Completed)
        {
            StatusText = SelectedRun.ErrorMessage ?? $"Scan run is {SelectedRun.Status}.";
            return;
        }
        IsLoading = true;
        try
        {
            foreach (var change in await index.GetScanChangesAsync(SelectedRun.Id, SelectedKind, ChangeLimit, cancellationToken).ConfigureAwait(true)) Changes.Add(change);
            StatusText = Changes.Count == 0 ? $"No {SelectedKind.ToString().ToLowerInvariant()} assets." : $"{Changes.Count} {SelectedKind.ToString().ToLowerInvariant()} assets.";
            if (SelectedRun.IsInitialBaseline && SelectedKind != AssetChangeKind.Added)
            {
                StatusText = "This was the initial baseline. No previous scan is available for comparison.";
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ApplicationLog.ScanChangesLoadFailed(logger, exception);
            StatusText = $"Scan changes could not be loaded: {exception.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OpenSelectedAsset()
    {
        if (SelectedChange is { AssetExists: true, Kind: not AssetChangeKind.Removed } change)
        {
            navigateToAsset(change.AssetId);
        }
    }
}
