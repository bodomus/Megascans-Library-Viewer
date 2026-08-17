using Microsoft.Extensions.Logging;

namespace ScanVault.App;

internal static partial class ApplicationLog
{
    [LoggerMessage(
        2001,
        LogLevel.Information,
        "Starting ScanVault {ApplicationVersion}; informational {InformationalVersion}; commit {CommitSha}; configuration {BuildConfiguration}; runtime {RuntimeVersion}; OS {OperatingSystem}; architecture {ProcessArchitecture}")]
    public static partial void Starting(
        ILogger logger,
        string applicationVersion,
        string informationalVersion,
        string commitSha,
        string buildConfiguration,
        string runtimeVersion,
        string operatingSystem,
        string processArchitecture);

    [LoggerMessage(2002, LogLevel.Warning, "Cannot load image {ImagePath}")]
    public static partial void ImageLoadFailed(
        ILogger logger,
        string imagePath,
        Exception exception);

    [LoggerMessage(2003, LogLevel.Error, "Scan command failed")]
    public static partial void ScanCommandFailed(ILogger logger, Exception exception);

    [LoggerMessage(2004, LogLevel.Warning, "Asset action {Action} failed for {AssetId}")]
    public static partial void AssetActionFailed(
        ILogger logger,
        string action,
        string assetId,
        Exception exception);

    [LoggerMessage(2005, LogLevel.Warning, "Copy diagnostics failed")]
    public static partial void DiagnosticsCopyFailed(
        ILogger logger,
        Exception exception);

    [LoggerMessage(2006, LogLevel.Warning, "Saving inventory filter failed")]
    public static partial void InventoryFilterSaveFailed(ILogger logger, Exception exception);

    [LoggerMessage(2007, LogLevel.Warning, "Loading scan history failed")]
    public static partial void ScanHistoryLoadFailed(ILogger logger, Exception exception);

    [LoggerMessage(2008, LogLevel.Warning, "Loading scan changes failed")]
    public static partial void ScanChangesLoadFailed(ILogger logger, Exception exception);

    [LoggerMessage(2009, LogLevel.Warning, "Loading smart collections failed")]
    public static partial void SmartCollectionsLoadFailed(ILogger logger, Exception exception);

    [LoggerMessage(2010, LogLevel.Information, "Smart collection {Action} {CollectionId} {CollectionName}")]
    public static partial void SmartCollectionChanged(ILogger logger, string action, string collectionId, string collectionName);

    [LoggerMessage(2011, LogLevel.Information, "Smart collection applied {CollectionId} {CollectionName}")]
    public static partial void SmartCollectionApplied(ILogger logger, string collectionId, string collectionName);

    [LoggerMessage(2012, LogLevel.Warning, "Smart collection count refresh failed")]
    public static partial void SmartCollectionCountRefreshFailed(ILogger logger, Exception exception);

    [LoggerMessage(2013, LogLevel.Error, "Duplicate analysis command failed")]
    public static partial void DuplicateAnalysisCommandFailed(ILogger logger, Exception exception);
}
