using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using ScanVault.App.Services;
using ScanVault.App.ViewModels;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;

namespace ScanVault.App.Tests;

public sealed class ExportReportViewModelTests
{
    [Fact]
    public async Task LoadsOnlyCompletedRunsAndExposesConditionalOptions()
    {
        var completed = CreateRun("completed", ScanRunStatus.Completed);
        var index = new ReportIndex([completed, CreateRun("failed", ScanRunStatus.Failed)]);
        using var viewModel = CreateViewModel(index, new RecordingExportService(), [CreateAsset("all")], []);

        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SelectedProfile = ReportProfile.ScanChanges;
        viewModel.SelectedFormat = ReportFormat.Json;

        Assert.Equal(completed, Assert.Single(viewModel.ScanRuns));
        Assert.True(viewModel.IsScanChanges);
        Assert.False(viewModel.IsScopeEnabled);
        Assert.True(viewModel.IsJson);
        Assert.EndsWith(".json", viewModel.DefaultFileName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CurrentViewExportUsesExistingOrderedSnapshot()
    {
        var all = new[] { CreateAsset("one"), CreateAsset("two") };
        var visible = new[] { all[1] };
        var service = new RecordingExportService();
        using var viewModel = CreateViewModel(new ReportIndex([]), service, all, visible);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SelectedScope = ReportScope.CurrentView;
        viewModel.DestinationPath = Path.Combine(Path.GetTempPath(), "report.csv");

        await viewModel.ExportAsync();

        var request = Assert.IsType<ReportExportRequest>(service.Request);
        Assert.Equal("two", Assert.Single(request.Assets).Id);
        Assert.Equal("HasFbx; search=rock", request.FilterSummary);
        Assert.Equal("NameDescending", request.SortSummary);
        Assert.Contains("Exported", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectedAssetScopeIsInvalidWhenNothingIsSelected()
    {
        using var viewModel = CreateViewModel(new ReportIndex([]), new RecordingExportService(), [CreateAsset("one")], [], null);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SelectedScope = ReportScope.SelectedAsset;
        viewModel.DestinationPath = Path.Combine(Path.GetTempPath(), "report.csv");

        Assert.False(viewModel.CanExport);
        Assert.Equal(0, viewModel.EstimatedAssetCount);
    }

    private static ExportReportViewModel CreateViewModel(
        IAssetIndex index,
        IReportExportService service,
        IReadOnlyList<AssetSummary> all,
        IReadOnlyList<AssetSummary> visible,
        AssetSummary? selected = null) => new(
            service,
            index,
            all,
            visible,
            selected,
            [],
            Path.GetTempPath(),
            "HasFbx; search=rock",
            "NameDescending",
            ApplicationBuildInfo.Create("1.0.0", "1.0.0+abcdef1", "abcdef1", "Test"),
            NullLogger<ExportReportViewModel>.Instance);

    private static AssetSummary CreateAsset(string id) => new(
        id, $"Asset {id}", "surface", Path.Combine(Path.GetTempPath(), id),
        Path.Combine(Path.GetTempPath(), id, $"{id}.json"), null, null, null, null, null, null, null, null,
        [], [], DateTimeOffset.UnixEpoch);

    private static ScanRunSummary CreateRun(string id, ScanRunStatus status) => new(
        id, "library", "library", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, status, "1.0.0", "abcdef1",
        4, 3, 1, 1, 1, 0, 0, 0, null, false);

    private sealed class RecordingExportService : IReportExportService
    {
        public ReportExportRequest? Request { get; private set; }

        public Task<ReportExportResult> ExportAsync(ReportExportRequest request, IProgress<ReportProgress>? progress,
            CancellationToken cancellationToken)
        {
            Request = request;
            progress?.Report(new(ReportExportPhase.Completed, request.Assets.Count, request.Assets.Count, TimeSpan.FromMilliseconds(5)));
            return Task.FromResult(new ReportExportResult(request.DestinationPath, null, request.Assets.Count,
                request.Assets.Count, 123, TimeSpan.FromMilliseconds(5)));
        }
    }

    private sealed class ReportIndex(IReadOnlyList<ScanRunSummary> runs) : IAssetIndex
    {
        public IndexCompatibilityInfo Compatibility { get; } = new(
            IndexCompatibilityState.Compatible, 4, 3, true, true, false, "Compatible");
        public bool RequiresNormalizationRescan => false;
        public Task<IndexCompatibilityInfo> InspectCompatibilityAsync(CancellationToken cancellationToken) => Task.FromResult(Compatibility);
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IndexDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new IndexDiagnostics(Compatibility, 0, null));
        public Task<IReadOnlyList<AssetSummary>> GetAssetsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AssetSummary>>([]);
        public Task<string> BeginScanRunAsync(string libraryRoot, string applicationVersion, string commitSha,
            CancellationToken cancellationToken) => Task.FromResult("run");
        public Task FinishScanRunAsync(string scanRunId, ScanRunStatus status, string? errorMessage,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<ScanRunSummary>> GetScanRunsAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult(runs);
        public Task<IReadOnlyList<ScanChangeSummary>> GetScanChangesAsync(string scanRunId, AssetChangeKind kind, int limit,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ScanChangeSummary>>([]);
        public Task<IndexUpdateResult> ReplaceLibraryAsync(string libraryRoot, IReadOnlyList<AssetSummary> assets,
            ScanResult draftResult, string scanRunId, CancellationToken cancellationToken) =>
            Task.FromResult(new IndexUpdateResult(0, 0, 0));
    }
}
