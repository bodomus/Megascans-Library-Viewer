using Microsoft.Extensions.Logging;
using ScanVault.Core.Models;

namespace ScanVault.App;

internal static partial class ApplicationLog
{
    [LoggerMessage(2013, LogLevel.Information,
        "Report export started: profile {ReportProfile}; format {ExportFormat}; scope {Scope}; extension {DestinationExtension}; assets {AssetCount}; absolute paths {IncludeAbsolutePaths}")]
    public static partial void ReportExportStarted(
        ILogger logger,
        ReportProfile reportProfile,
        ReportFormat exportFormat,
        ReportScope scope,
        string destinationExtension,
        int assetCount,
        bool includeAbsolutePaths);

    [LoggerMessage(2014, LogLevel.Information,
        "Report export completed: profile {ReportProfile}; format {ExportFormat}; scope {Scope}; assets {AssetCount}; rows {RowCount}; duration {DurationMs} ms; bytes {OutputSizeBytes}; absolute paths {IncludeAbsolutePaths}")]
    public static partial void ReportExportCompleted(
        ILogger logger,
        ReportProfile reportProfile,
        ReportFormat exportFormat,
        ReportScope scope,
        int assetCount,
        long rowCount,
        double durationMs,
        long outputSizeBytes,
        bool includeAbsolutePaths);

    [LoggerMessage(2015, LogLevel.Information,
        "Report export cancelled: profile {ReportProfile}; format {ExportFormat}; scope {Scope}; duration {DurationMs} ms")]
    public static partial void ReportExportCancelled(
        ILogger logger,
        ReportProfile reportProfile,
        ReportFormat exportFormat,
        ReportScope scope,
        double durationMs);

    [LoggerMessage(2016, LogLevel.Error,
        "Report export failed: profile {ReportProfile}; format {ExportFormat}; scope {Scope}")]
    public static partial void ReportExportFailed(
        ILogger logger,
        ReportProfile reportProfile,
        ReportFormat exportFormat,
        ReportScope scope,
        Exception exception);
}
