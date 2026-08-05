using System.Text;
using System.Text.Json;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Infrastructure.Reporting;

namespace ScanVault.Infrastructure.Tests;

public sealed class ReportExportTests
{
    [Fact]
    public async Task CsvUsesBomEscapingAndCompanionMetadata()
    {
        using var temporary = new TemporaryDirectory();
        var destination = Path.Combine(temporary.Path, "catalog.csv");
        var service = CreateService();
        var request = CreateRequest(temporary.Path, destination, ReportFormat.Csv, CreateAsset("comma,quote\"", "line\none"));

        var result = await service.ExportAsync(request, null, CancellationToken.None);
        var bytes = await File.ReadAllBytesAsync(destination);
        var text = Encoding.UTF8.GetString(bytes);

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Contains("\"comma,quote\"\"\"", text, StringComparison.Ordinal);
        Assert.Contains("\"line\none\"", text, StringComparison.Ordinal);
        Assert.Equal(1, result.RowCount);
        Assert.True(File.Exists(destination + ".metadata.json"));
        using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(destination + ".metadata.json"));
        Assert.Equal(ReportContract.SchemaVersion, metadata.RootElement.GetProperty("reportSchemaVersion").GetInt32());
    }

    [Fact]
    public async Task JsonUsesExplicitEnvelopeAndPrettyPrint()
    {
        using var temporary = new TemporaryDirectory();
        var destination = Path.Combine(temporary.Path, "catalog.json");
        var service = CreateService();
        var request = CreateRequest(temporary.Path, destination, ReportFormat.Json, CreateAsset("unicode-лес", "asset"));

        await service.ExportAsync(request, null, CancellationToken.None);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(destination));

        Assert.Equal(ReportContract.SchemaVersion, document.RootElement.GetProperty("reportSchemaVersion").GetInt32());
        Assert.Equal("unicode-лес", document.RootElement.GetProperty("rows")[0].GetProperty("assetId").GetString());
        Assert.Contains(Environment.NewLine, await File.ReadAllTextAsync(destination), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MarkdownEscapesPipesBackticksAndNewlines()
    {
        using var temporary = new TemporaryDirectory();
        var destination = Path.Combine(temporary.Path, "catalog.md");
        var service = CreateService();
        var request = CreateRequest(temporary.Path, destination, ReportFormat.Markdown, CreateAsset("a|b`c", "line\nbreak"));

        await service.ExportAsync(request, null, CancellationToken.None);
        var text = await File.ReadAllTextAsync(destination);

        Assert.Contains("a\\|b\\`c", text, StringComparison.Ordinal);
        Assert.Contains("line<br>break", text, StringComparison.Ordinal);
        Assert.Contains("## Metadata", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationPreservesExistingDestinationAndCleansStagingFile()
    {
        using var temporary = new TemporaryDirectory();
        var destination = temporary.WriteFile("catalog.csv", "original");
        var service = new ReportExportService([new CancellingWriter()]);
        var request = CreateRequest(temporary.Path, destination, ReportFormat.Csv, CreateAsset("asset", "asset")) with
        {
            IncludeMetadata = false
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ExportAsync(request, null, CancellationToken.None));

        Assert.Equal("original", await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.GetFiles(temporary.Path, "*.tmp"));
    }


    [Fact]
    public async Task FinalizationFailureRestoresExistingMetadataCompanion()
    {
        using var temporary = new TemporaryDirectory();
        var destination = temporary.CreateDirectory("occupied.csv");
        var metadataPath = temporary.WriteFile("occupied.csv.metadata.json", "original metadata");
        var service = CreateService();
        var request = CreateRequest(temporary.Path, destination, ReportFormat.Csv, CreateAsset("asset", "asset"));

        var exception = await Record.ExceptionAsync(() =>
            service.ExportAsync(request, null, CancellationToken.None));
        Assert.True(exception is IOException or UnauthorizedAccessException, exception?.ToString());

        Assert.True(Directory.Exists(destination));
        Assert.Equal("original metadata", await File.ReadAllTextAsync(metadataPath));
        Assert.Empty(Directory.GetFiles(temporary.Path, "*.backup"));
    }

    [Fact]
    public async Task EmptyCsvStillContainsStableProfileHeaders()
    {
        using var temporary = new TemporaryDirectory();
        var destination = Path.Combine(temporary.Path, "issues.csv");
        var service = CreateService();
        var request = CreateRequest(temporary.Path, destination, ReportFormat.Csv, CreateAsset("asset", "asset")) with
        {
            Profile = ReportProfile.IssuesReport
        };

        await service.ExportAsync(request, null, CancellationToken.None);

        Assert.Contains("IssueCode", await File.ReadAllTextAsync(destination), StringComparison.Ordinal);
    }

    private static ReportExportService CreateService() =>
        new([new CsvReportWriter(), new JsonReportWriter(), new MarkdownReportWriter()]);

    private static ReportExportRequest CreateRequest(string root, string destination, ReportFormat format, AssetSummary asset) => new(
        ReportProfile.AssetCatalog, format, ReportScope.EntireLibrary, destination, root, false, true, true,
        false, [], [asset], "No filters", "NameAscending", "1.0.0", "abcdef1", 4, 3);

    private static AssetSummary CreateAsset(string id, string name)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ScanVault.Report.Tests"));
        return new(id, name, "surface", Path.Combine(root, id), Path.Combine(root, id, $"{id}.json"), null, null,
            "Biome", "Region", null, new(2048, 1024), 12.5, null, [], [], DateTimeOffset.UnixEpoch);
    }

    private sealed class CancellingWriter : IReportWriter
    {
        public ReportFormat Format => ReportFormat.Csv;

        public async Task<long> WriteAsync(ReportDocument document, Stream destination, ReportExportRequest request,
            IProgress<ReportProgress>? progress, CancellationToken cancellationToken)
        {
            await destination.WriteAsync("partial"u8.ToArray(), cancellationToken);
            throw new OperationCanceledException();
        }
    }
}
