using Microsoft.Extensions.Logging;

namespace ScanVault.Infrastructure;

internal static partial class InfrastructureLog
{
    [LoggerMessage(1001, LogLevel.Warning, "Cannot enumerate Megascans directory {Directory}")]
    public static partial void CannotEnumerate(
        ILogger logger,
        string directory,
        Exception exception);

    [LoggerMessage(1002, LogLevel.Warning, "Malformed Megascans JSON {JsonPath}")]
    public static partial void MalformedJson(
        ILogger logger,
        string jsonPath,
        Exception exception);

    [LoggerMessage(1003, LogLevel.Warning, "Cannot read Megascans JSON {JsonPath}")]
    public static partial void CannotReadJson(
        ILogger logger,
        string jsonPath,
        Exception exception);

    [LoggerMessage(1004, LogLevel.Information, "Starting Megascans scan for {LibraryRoot}")]
    public static partial void ScanStarted(ILogger logger, string libraryRoot);

    [LoggerMessage(1005, LogLevel.Warning,
        "Duplicate Megascans ID {AssetId}. Winner: {WinnerPath}; skipped copies: {CopyPaths}")]
    public static partial void DuplicateId(
        ILogger logger,
        string assetId,
        string winnerPath,
        string copyPaths);

    [LoggerMessage(1006, LogLevel.Information,
        "Completed scan for {LibraryRoot}: {Added} added, {Updated} updated, {Removed} removed in {Elapsed}")]
    public static partial void ScanCompleted(
        ILogger logger,
        string libraryRoot,
        int added,
        int updated,
        int removed,
        TimeSpan elapsed);

    [LoggerMessage(1007, LogLevel.Information, "Megascans scan cancelled for {LibraryRoot}")]
    public static partial void ScanCancelled(ILogger logger, string libraryRoot);

    [LoggerMessage(1008, LogLevel.Error, "Megascans scan failed for {LibraryRoot}")]
    public static partial void ScanFailed(
        ILogger logger,
        string libraryRoot,
        Exception exception);

    [LoggerMessage(1101, LogLevel.Information,
        "SQLite index ready at {DatabasePath}, schema version {SchemaVersion}")]
    public static partial void IndexReady(
        ILogger logger,
        string databasePath,
        int schemaVersion);

    [LoggerMessage(1102, LogLevel.Error, "SQLite rollback failed")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
