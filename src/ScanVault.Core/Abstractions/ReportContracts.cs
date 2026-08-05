using ScanVault.Core.Models;

namespace ScanVault.Core.Abstractions;

public interface IReportExportService
{
    Task<ReportExportResult> ExportAsync(
        ReportExportRequest request,
        IProgress<ReportProgress>? progress,
        CancellationToken cancellationToken);
}

public interface IReportWriter
{
    ReportFormat Format { get; }

    Task<long> WriteAsync(
        ReportDocument document,
        Stream destination,
        ReportExportRequest request,
        IProgress<ReportProgress>? progress,
        CancellationToken cancellationToken);
}

