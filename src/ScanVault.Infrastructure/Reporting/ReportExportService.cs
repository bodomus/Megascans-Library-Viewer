using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Infrastructure.Reporting;

public sealed class ReportExportService(IEnumerable<IReportWriter> writers) : IReportExportService
{
    private readonly Dictionary<ReportFormat, IReportWriter> writersByFormat =
        writers.ToDictionary(static writer => writer.Format);
    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };


    public async Task<ReportExportResult> ExportAsync(ReportExportRequest request, IProgress<ReportProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stopwatch = Stopwatch.StartNew();
        progress?.Report(new(ReportExportPhase.PreparingQuery, 0, 0, stopwatch.Elapsed));
        var destination = Path.GetFullPath(request.DestinationPath);
        var directory = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Destination directory does not exist: {directory}");
        }

        if (!writersByFormat.TryGetValue(request.Format, out var writer))
        {
            throw new NotSupportedException($"Report format {request.Format} is not supported.");
        }

        var document = ReportProfilePolicy.CreateDocument(request, DateTimeOffset.UtcNow);
        progress?.Report(new(ReportExportPhase.ReadingAssets, 0, 0, stopwatch.Elapsed));
        var temporaryPath = TemporaryPath(destination);
        var metadataPath = request.Format == ReportFormat.Csv && request.IncludeMetadata ? destination + ".metadata.json" : null;
        var temporaryMetadataPath = metadataPath is null ? null : TemporaryPath(metadataPath);

        try
        {
            long rowCount;
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                rowCount = await writer.WriteAsync(document, stream, request, progress, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (temporaryMetadataPath is not null)
            {
                var actualMetadata = document.Metadata with { RowCount = rowCount };
                await using var metadataStream = new FileStream(temporaryMetadataPath, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await JsonSerializer.SerializeAsync(metadataStream, actualMetadata, MetadataJsonOptions, cancellationToken).ConfigureAwait(false);
                await metadataStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(ReportExportPhase.Finalizing, document.Metadata.AssetCount, rowCount, stopwatch.Elapsed));
            ReportFilePublisher.Publish(temporaryPath, destination, temporaryMetadataPath, metadataPath);
            stopwatch.Stop();
            var result = new ReportExportResult(destination, metadataPath, document.Metadata.AssetCount, rowCount,
                new FileInfo(destination).Length, stopwatch.Elapsed);
            progress?.Report(new(ReportExportPhase.Completed, result.AssetCount, result.RowCount, result.Duration));
            return result;
        }
        finally
        {
            TryDelete(temporaryPath);
            if (temporaryMetadataPath is not null)
            {
                TryDelete(temporaryMetadataPath);
            }
        }
    }

    private static string TemporaryPath(string destination) =>
        Path.Combine(Path.GetDirectoryName(destination)!, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Preserve the original outcome; staging files are visibly temporary.
        }
        catch (UnauthorizedAccessException)
        {
            // Preserve the original outcome; staging files are visibly temporary.
        }
    }
}
