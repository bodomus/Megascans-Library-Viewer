using System.Diagnostics;
using ScanVault.Core.Models;
using ScanVault.Infrastructure.Reporting;
using Xunit.Abstractions;

namespace ScanVault.Infrastructure.Tests;

public sealed class ReportExportPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task LargeSyntheticProfilesStreamSuccessfully()
    {
        using var temporary = new TemporaryDirectory();
        var assets = CreateAssets(temporary.Path, 10_000);
        var service = new ReportExportService([new CsvReportWriter(), new JsonReportWriter(), new MarkdownReportWriter()]);
        var peakMemory = GC.GetTotalMemory(false);
        using var samplingCancellation = new CancellationTokenSource();
        var sampler = SampleMemoryAsync(value => peakMemory = Math.Max(peakMemory, value), samplingCancellation.Token);

        var catalogCsv = await ExportAsync(service, temporary.Path, assets, ReportProfile.AssetCatalog, ReportFormat.Csv, "catalog.csv");
        var inventoryCsv = await ExportAsync(service, temporary.Path, assets, ReportProfile.AssetInventory, ReportFormat.Csv, "inventory.csv");
        var catalogJson = await ExportAsync(service, temporary.Path, assets, ReportProfile.AssetCatalog, ReportFormat.Json, "catalog.json");
        var issuesMarkdown = await ExportAsync(service, temporary.Path, assets, ReportProfile.IssuesReport, ReportFormat.Markdown, "issues.md");

        samplingCancellation.Cancel();
        await sampler;
        Assert.Equal(10_000, catalogCsv.RowCount);
        Assert.Equal(100_000, inventoryCsv.RowCount);
        Assert.Equal(10_000, catalogJson.RowCount);
        Assert.Equal(5_000, issuesMarkdown.RowCount);

        var cancellation = new CancellationTokenSource();
        var cancelledAt = TimeSpan.Zero;
        var stopwatch = Stopwatch.StartNew();
        var progress = new InlineProgress<ReportProgress>(value =>
        {
            if (value.WrittenRows >= 256 && !cancellation.IsCancellationRequested)
            {
                cancelledAt = stopwatch.Elapsed;
                cancellation.Cancel();
            }
        });
        var cancellationRequest = CreateRequest(temporary.Path, assets, ReportProfile.AssetInventory, ReportFormat.Csv,
            Path.Combine(temporary.Path, "cancelled.csv"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExportAsync(cancellationRequest, progress, cancellation.Token));
        var cancellationLatency = stopwatch.Elapsed - cancelledAt;

        output.WriteLine("Asset Catalog CSV: {0} rows, {1} bytes, {2} ms", catalogCsv.RowCount, catalogCsv.OutputSizeBytes, catalogCsv.Duration.TotalMilliseconds);
        output.WriteLine("Asset Inventory CSV: {0} rows, {1} bytes, {2} ms", inventoryCsv.RowCount, inventoryCsv.OutputSizeBytes, inventoryCsv.Duration.TotalMilliseconds);
        output.WriteLine("Asset Catalog JSON: {0} rows, {1} bytes, {2} ms", catalogJson.RowCount, catalogJson.OutputSizeBytes, catalogJson.Duration.TotalMilliseconds);
        output.WriteLine("Issues Markdown: {0} rows, {1} bytes, {2} ms", issuesMarkdown.RowCount, issuesMarkdown.OutputSizeBytes, issuesMarkdown.Duration.TotalMilliseconds);
        output.WriteLine("Peak sampled managed memory: {0} bytes", peakMemory);
        output.WriteLine("Cancellation latency: {0} ms", cancellationLatency.TotalMilliseconds);

        Assert.False(File.Exists(cancellationRequest.DestinationPath));
        Assert.True(cancellationLatency < TimeSpan.FromSeconds(2), $"Cancellation latency was {cancellationLatency}.");
    }

    private static Task<ReportExportResult> ExportAsync(ReportExportService service, string root,
        IReadOnlyList<AssetSummary> assets, ReportProfile profile, ReportFormat format, string fileName) =>
        service.ExportAsync(CreateRequest(root, assets, profile, format, Path.Combine(root, fileName)), null, CancellationToken.None);

    private static ReportExportRequest CreateRequest(string root, IReadOnlyList<AssetSummary> assets,
        ReportProfile profile, ReportFormat format, string destination) => new(
        profile, format, ReportScope.EntireLibrary, destination, root, false, true, false, false,
        Enum.GetValues<AssetCompletenessStatus>(), assets, "None", "NameAscending", "1.0.0", "abcdef1", 4, 3);

    private static AssetSummary[] CreateAssets(string root, int count)
    {
        var assets = new AssetSummary[count];
        for (var index = 0; index < count; index++)
        {
            var id = $"asset-{index:D5}";
            var folder = Path.Combine(root, "assets", id);
            var components = Enumerable.Range(0, 10)
                .Select(component => new TextureComponentEntry(
                    Path.Combine(folder, $"texture-{component}.png"),
                    $"texture-{component}.png", "albedo", TextureMapType.Albedo, 2048, "png"))
                .ToArray();
            IReadOnlyList<AssetContentIssue> issues = index < 5_000
                ? [new(AssetContentIssueCode.MissingReference, "Missing | referenced file", [Path.Combine(folder, "missing.file")])]
                : [];
            var content = new AssetContentInventory([], [new(TextureSetKind.General, 2048, components)], [],
                AssetCompletenessStatus.Partial, issues);
            assets[index] = new AssetSummary(id, $"Synthetic asset {index}", "surface", folder,
                Path.Combine(folder, $"{id}.json"), null, null, "Forest", "Global", null,
                new(2048, 2048), 10.5, null, [], [], DateTimeOffset.UnixEpoch)
            { Content = content };
        }

        return assets;
    }

    private static async Task SampleMemoryAsync(Action<long> observe, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                observe(GC.GetTotalMemory(false));
                await Task.Delay(5, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Sampling completed.
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
