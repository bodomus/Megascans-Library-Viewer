using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using System.IO;
using ScanVault.App.Presentation;
using ScanVault.App.Services;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.App.ViewModels;

public sealed record ReportProfileOption(ReportProfile Profile, string Label);
public sealed record ReportFormatOption(ReportFormat Format, string Label);
public sealed record ReportScopeOption(ReportScope Scope, string Label);

public sealed class ExportReportViewModel : ObservableObject, IDisposable
{
    private readonly IReportExportService exportService;
    private readonly IAssetIndex index;
    private readonly IReadOnlyList<AssetSummary> allAssets;
    private readonly IReadOnlyList<AssetSummary> currentViewAssets;
    private readonly AssetSummary? selectedAsset;
    private readonly string libraryRoot;
    private readonly string filterSummary;
    private readonly string sortSummary;
    private readonly ApplicationBuildInfo buildInfo;
    private readonly ILogger<ExportReportViewModel> logger;
    private CancellationTokenSource? exportCancellation;
    private ReportProfile selectedProfile = ReportProfile.AssetCatalog;
    private ReportFormat selectedFormat = ReportFormat.Csv;
    private ReportScope selectedScope = ReportScope.EntireLibrary;
    private ScanRunSummary? selectedScanRun;
    private SmartCollectionRecord? selectedSmartCollection;
    private string destinationPath = string.Empty;
    private bool includeAbsolutePaths;
    private bool includeMetadata = true;
    private bool prettyJson = true;
    private bool includeUnchangedScanItems;
    private bool isExporting;
    private string statusText = "Choose a profile, scope, format, and destination file.";
    private int processedAssets;
    private long writtenRows;
    private TimeSpan elapsed;
    private bool completeSelected = true;
    private bool usableSelected = true;
    private bool partialSelected = true;
    private bool missingCriticalSelected = true;
    private bool ambiguousSelected = true;
    private bool unknownSelected = true;

    public ExportReportViewModel(
        IReportExportService exportService,
        IAssetIndex index,
        IReadOnlyList<AssetSummary> allAssets,
        IReadOnlyList<AssetSummary> currentViewAssets,
        AssetSummary? selectedAsset,
        IReadOnlyList<SmartCollectionRecord> smartCollections,
        string libraryRoot,
        string filterSummary,
        string sortSummary,
        ApplicationBuildInfo buildInfo,
        ILogger<ExportReportViewModel> logger)
    {
        this.exportService = exportService;
        this.index = index;
        this.allAssets = allAssets;
        this.currentViewAssets = currentViewAssets;
        this.selectedAsset = selectedAsset;
        this.libraryRoot = libraryRoot;
        this.filterSummary = filterSummary;
        this.sortSummary = sortSummary;
        this.buildInfo = buildInfo;
        this.logger = logger;
        foreach (var collection in smartCollections)
        {
            SmartCollections.Add(collection);
        }

        SelectedSmartCollection = SmartCollections.FirstOrDefault();
    }

    public IReadOnlyList<ReportProfileOption> Profiles { get; } =
    [
        new(ReportProfile.AssetCatalog, "Asset Catalog"),
        new(ReportProfile.AssetInventory, "Asset Inventory"),
        new(ReportProfile.IssuesReport, "Issues Report"),
        new(ReportProfile.CompletenessReport, "Completeness Report"),
        new(ReportProfile.ScanChanges, "Scan Changes"),
        new(ReportProfile.SmartCollectionResult, "Smart Collection Result")
    ];

    public IReadOnlyList<ReportFormatOption> Formats { get; } =
    [
        new(ReportFormat.Csv, "CSV"), new(ReportFormat.Json, "JSON"), new(ReportFormat.Markdown, "Markdown")
    ];

    public IReadOnlyList<ReportScopeOption> Scopes { get; } =
    [
        new(ReportScope.EntireLibrary, "Entire Library"),
        new(ReportScope.CurrentView, "Current View"),
        new(ReportScope.SelectedAsset, "Selected Asset")
    ];

    public ObservableCollection<ScanRunSummary> ScanRuns { get; } = [];
    public ObservableCollection<SmartCollectionRecord> SmartCollections { get; } = [];

    public ReportProfile SelectedProfile
    {
        get => selectedProfile;
        set
        {
            if (SetProperty(ref selectedProfile, value))
            {
                if (!IsScopeEnabled) SelectedScope = ReportScope.EntireLibrary;
                NotifyOptionsChanged();
            }
        }
    }

    public ReportFormat SelectedFormat
    {
        get => selectedFormat;
        set
        {
            if (SetProperty(ref selectedFormat, value)) NotifyOptionsChanged();
        }
    }

    public ReportScope SelectedScope
    {
        get => selectedScope;
        set
        {
            if (SetProperty(ref selectedScope, value)) NotifyEstimateChanged();
        }
    }

    public ScanRunSummary? SelectedScanRun
    {
        get => selectedScanRun;
        set
        {
            if (SetProperty(ref selectedScanRun, value)) NotifyEstimateChanged();
        }
    }

    public SmartCollectionRecord? SelectedSmartCollection
    {
        get => selectedSmartCollection;
        set
        {
            if (SetProperty(ref selectedSmartCollection, value)) NotifyEstimateChanged();
        }
    }

    public string DestinationPath
    {
        get => destinationPath;
        set
        {
            if (SetProperty(ref destinationPath, value)) OnPropertyChanged(nameof(CanExport));
        }
    }

    public bool IncludeAbsolutePaths { get => includeAbsolutePaths; set => SetProperty(ref includeAbsolutePaths, value); }
    public bool IncludeMetadata { get => includeMetadata; set => SetProperty(ref includeMetadata, value); }
    public bool PrettyJson { get => prettyJson; set => SetProperty(ref prettyJson, value); }
    public bool IncludeUnchangedScanItems
    {
        get => includeUnchangedScanItems;
        set
        {
            if (SetProperty(ref includeUnchangedScanItems, value)) NotifyEstimateChanged();
        }
    }

    public bool CompleteSelected { get => completeSelected; set { if (SetProperty(ref completeSelected, value)) NotifyEstimateChanged(); } }
    public bool UsableSelected { get => usableSelected; set { if (SetProperty(ref usableSelected, value)) NotifyEstimateChanged(); } }
    public bool PartialSelected { get => partialSelected; set { if (SetProperty(ref partialSelected, value)) NotifyEstimateChanged(); } }
    public bool MissingCriticalSelected { get => missingCriticalSelected; set { if (SetProperty(ref missingCriticalSelected, value)) NotifyEstimateChanged(); } }
    public bool AmbiguousSelected { get => ambiguousSelected; set { if (SetProperty(ref ambiguousSelected, value)) NotifyEstimateChanged(); } }
    public bool UnknownSelected { get => unknownSelected; set { if (SetProperty(ref unknownSelected, value)) NotifyEstimateChanged(); } }

    public bool IsExporting
    {
        get => isExporting;
        private set
        {
            if (SetProperty(ref isExporting, value))
            {
                OnPropertyChanged(nameof(CanExport));
                OnPropertyChanged(nameof(CanCancel));
            }
        }
    }

    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public int ProcessedAssets { get => processedAssets; private set => SetProperty(ref processedAssets, value); }
    public long WrittenRows { get => writtenRows; private set => SetProperty(ref writtenRows, value); }
    public TimeSpan Elapsed { get => elapsed; private set => SetProperty(ref elapsed, value); }
    public bool IsJson => SelectedFormat == ReportFormat.Json;
    public bool IsCsv => SelectedFormat == ReportFormat.Csv;
    public bool IsScanChanges => SelectedProfile == ReportProfile.ScanChanges;
    public bool IsSmartCollection => SelectedProfile == ReportProfile.SmartCollectionResult;
    public bool IsCompleteness => SelectedProfile == ReportProfile.CompletenessReport;
    public bool IsScopeEnabled => !IsScanChanges && !IsSmartCollection;
    public bool CanCancel => IsExporting;
    public bool CanExport => !IsExporting && !string.IsNullOrWhiteSpace(DestinationPath) &&
                             (SelectedScope != ReportScope.SelectedAsset || selectedAsset is not null) &&
                             (!IsScanChanges || SelectedScanRun is not null) &&
                             (!IsSmartCollection || SelectedSmartCollection is not null) &&
                             (!IsCompleteness || CompleteSelected || UsableSelected || PartialSelected || MissingCriticalSelected || AmbiguousSelected || UnknownSelected);
    public string DefaultFileName => ReportProfilePolicy.DefaultFileName(SelectedProfile, SelectedFormat, DateTimeOffset.Now);
    public string DestinationExtension => ReportProfilePolicy.Extension(SelectedFormat);
    public int EstimatedAssetCount => SelectAssets().Count;
    public long EstimatedRowCount
    {
        get
        {
            try { return ReportProfilePolicy.EstimateRowCount(CreateRequest("report." + DestinationExtension, [])); }
            catch (InvalidOperationException) { return 0; }
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        ScanRuns.Clear();
        foreach (var run in await index.GetScanRunsAsync(100, cancellationToken).ConfigureAwait(true))
        {
            if (run.Status == ScanRunStatus.Completed) ScanRuns.Add(run);
        }

        SelectedScanRun = ScanRuns.FirstOrDefault();
    }

    public async Task ExportAsync(CancellationToken cancellationToken = default)
    {
        if (!CanExport) return;
        CancelAndDisposeExportCancellation();
        exportCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = exportCancellation.Token;
        IsExporting = true;
        ProcessedAssets = 0;
        WrittenRows = 0;
        Elapsed = TimeSpan.Zero;
        ReportExportRequest? request = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var changes = IsScanChanges ? await LoadScanChangesAsync(token).ConfigureAwait(true) : [];
            request = CreateRequest(DestinationPath, changes);
            stopwatch.Restart();
            ApplicationLog.ReportExportStarted(logger, request.Profile, request.Format, request.Scope,
                Path.GetExtension(request.DestinationPath), request.Assets.Count, request.IncludeAbsolutePaths);
            var progress = new Progress<ReportProgress>(value =>
            {
                ProcessedAssets = value.ProcessedAssets;
                WrittenRows = value.WrittenRows;
                Elapsed = value.Elapsed;
                StatusText = value.Phase switch
                {
                    ReportExportPhase.PreparingQuery => "Preparing query...",
                    ReportExportPhase.ReadingAssets => "Reading assets...",
                    ReportExportPhase.WritingReport => $"Writing report... {value.WrittenRows:N0} rows",
                    ReportExportPhase.Finalizing => "Finalizing...",
                    ReportExportPhase.Completed => "Export completed.",
                    _ => value.Phase.ToString()
                };
            });

            var result = await Task.Run(() => exportService.ExportAsync(request, progress, token), token)
                .ConfigureAwait(true);
            StatusText = $"Exported {result.RowCount:N0} rows ({result.OutputSizeBytes:N0} bytes) in {result.Duration:g}.";
            ApplicationLog.ReportExportCompleted(logger, request.Profile, request.Format, request.Scope,
                result.AssetCount, result.RowCount, result.Duration.TotalMilliseconds, result.OutputSizeBytes,
                request.IncludeAbsolutePaths);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            stopwatch.Stop();
            StatusText = "Export cancelled. No partial destination file was published.";
            ApplicationLog.ReportExportCancelled(logger, SelectedProfile, SelectedFormat, SelectedScope, stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            StatusText = $"Export failed: {exception.Message}";
            ApplicationLog.ReportExportFailed(logger, SelectedProfile, SelectedFormat, SelectedScope, exception);
        }
        finally
        {
            IsExporting = false;
        }
    }

    public void Cancel() => exportCancellation?.Cancel();

    public void Dispose() => CancelAndDisposeExportCancellation();

    private ReportExportRequest CreateRequest(string destination, IReadOnlyList<ScanChangeSummary> changes)
    {
        var assets = SelectAssets();
        SmartCollectionRecord? collection = IsSmartCollection ? SelectedSmartCollection : null;
        var validation = collection is null ? new SmartCollectionValidationResult(SmartCollectionCompatibility.Compatible, null) :
            SmartCollectionPolicy.Validate(collection.Definition, libraryRoot);
        if (validation.Compatibility != SmartCollectionCompatibility.Compatible)
        {
            throw new InvalidOperationException(validation.Message ?? "The smart collection is not compatible.");
        }
        if (collection is not null)
        {
            assets = assets.Where(asset => SmartCollectionPolicy.Matches(asset, collection.Definition, libraryRoot, null)).ToArray();
        }

        return new(
            SelectedProfile, SelectedFormat, SelectedScope, destination, libraryRoot, IncludeAbsolutePaths,
            IncludeMetadata, PrettyJson, IncludeUnchangedScanItems, SelectedCompletenessStatuses(), assets,
            filterSummary, sortSummary, buildInfo.ApplicationVersion, buildInfo.CommitSha,
            index.Compatibility.DatabaseSchemaVersion, index.Compatibility.MetadataNormalizationVersion,
            IsScanChanges ? SelectedScanRun : null, changes, collection);
    }

    private IReadOnlyList<AssetSummary> SelectAssets() => SelectedScope switch
    {
        ReportScope.EntireLibrary => allAssets,
        ReportScope.CurrentView => currentViewAssets,
        ReportScope.SelectedAsset when selectedAsset is not null => [selectedAsset],
        ReportScope.SelectedAsset => [],
        _ => throw new ArgumentOutOfRangeException()
    };

    private List<AssetCompletenessStatus> SelectedCompletenessStatuses()
    {
        var values = new List<AssetCompletenessStatus>(6);
        if (CompleteSelected) values.Add(AssetCompletenessStatus.Complete);
        if (UsableSelected) values.Add(AssetCompletenessStatus.Usable);
        if (PartialSelected) values.Add(AssetCompletenessStatus.Partial);
        if (MissingCriticalSelected) values.Add(AssetCompletenessStatus.MissingCriticalFiles);
        if (AmbiguousSelected) values.Add(AssetCompletenessStatus.Ambiguous);
        if (UnknownSelected) values.Add(AssetCompletenessStatus.Unknown);
        return values;
    }

    private async Task<IReadOnlyList<ScanChangeSummary>> LoadScanChangesAsync(CancellationToken cancellationToken)
    {
        if (SelectedScanRun is not { Status: ScanRunStatus.Completed } run)
        {
            throw new InvalidOperationException("Select a completed scan run.");
        }

        var changes = new List<ScanChangeSummary>();
        foreach (var kind in Enum.GetValues<AssetChangeKind>())
        {
            if (kind == AssetChangeKind.Unchanged && !IncludeUnchangedScanItems) continue;
            changes.AddRange(await index.GetScanChangesAsync(run.Id, kind, int.MaxValue, cancellationToken).ConfigureAwait(true));
        }

        return changes;
    }

    private void NotifyOptionsChanged()
    {
        OnPropertyChanged(nameof(IsJson));
        OnPropertyChanged(nameof(IsCsv));
        OnPropertyChanged(nameof(IsScanChanges));
        OnPropertyChanged(nameof(IsSmartCollection));
        OnPropertyChanged(nameof(IsCompleteness));
        OnPropertyChanged(nameof(IsScopeEnabled));
        OnPropertyChanged(nameof(DefaultFileName));
        OnPropertyChanged(nameof(DestinationExtension));
        NotifyEstimateChanged();
    }

    private void NotifyEstimateChanged()
    {
        OnPropertyChanged(nameof(EstimatedAssetCount));
        OnPropertyChanged(nameof(EstimatedRowCount));
        OnPropertyChanged(nameof(CanExport));
    }

    private void CancelAndDisposeExportCancellation()
    {
        var cancellation = exportCancellation;
        exportCancellation = null;
        if (cancellation is null) return;
        if (!cancellation.IsCancellationRequested) cancellation.Cancel();
        cancellation.Dispose();
    }
}
