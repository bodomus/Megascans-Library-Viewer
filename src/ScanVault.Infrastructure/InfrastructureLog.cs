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

    [LoggerMessage(1009, LogLevel.Warning,
        "Ignoring malformed resolution {Resolution} in {JsonPath}")]
    public static partial void MalformedResolution(
        ILogger logger,
        string jsonPath,
        string resolution);

    [LoggerMessage(1010, LogLevel.Information, "Starting asset-content inventory for {AssetCount} assets")]
    public static partial void InventoryStarted(ILogger logger, int assetCount);

    [LoggerMessage(1011, LogLevel.Information,
        "Completed asset-content inventory: {AssetCount} assets, {MeshCount} meshes, {TextureCount} textures, {AmbiguousCount} ambiguous, {MissingCriticalCount} missing critical")]
    public static partial void InventoryCompleted(
        ILogger logger,
        int assetCount,
        int meshCount,
        int textureCount,
        int ambiguousCount,
        int missingCriticalCount);

    [LoggerMessage(1012, LogLevel.Warning, "Asset content issue for {AssetId}: {IssueCode} — {Message}; {Paths}")]
    public static partial void InventoryIssue(
        ILogger logger,
        string assetId,
        object issueCode,
        string message,
        string paths);
    [LoggerMessage(1101, LogLevel.Information,
        "SQLite index ready at {DatabasePath}, schema version {SchemaVersion}")]
    public static partial void IndexReady(
        ILogger logger,
        string databasePath,
        int schemaVersion);

    [LoggerMessage(1102, LogLevel.Error, "SQLite rollback failed")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);

    [LoggerMessage(1103, LogLevel.Information,
        "SQLite index {DatabasePath} compatibility {CompatibilityState}; schema {SchemaVersion}; normalization {NormalizationVersion}; Rescan required {RequiresRescan}")]
    public static partial void IndexCompatibilityEvaluated(
        ILogger logger,
        string databasePath,
        object compatibilityState,
        int? schemaVersion,
        int? normalizationVersion,
        bool requiresRescan);

    [LoggerMessage(1104, LogLevel.Information,
        "Migrating SQLite index {DatabasePath} from schema {FromVersion} to {ToVersion}")]
    public static partial void IndexMigrationStarted(
        ILogger logger,
        string databasePath,
        int fromVersion,
        int toVersion);

    [LoggerMessage(1105, LogLevel.Information,
        "Migrated SQLite index {DatabasePath} to schema {SchemaVersion}")]
    public static partial void IndexMigrationCompleted(
        ILogger logger,
        string databasePath,
        int schemaVersion);

    [LoggerMessage(1106, LogLevel.Warning,
        "Writes blocked for SQLite index {DatabasePath}: {CompatibilityState}. {Guidance}")]
    public static partial void IndexWritesBlocked(
        ILogger logger,
        string databasePath,
        object compatibilityState,
        string guidance);

    [LoggerMessage(1107, LogLevel.Error, "SQLite index {DatabasePath} is corrupted or unreadable")]
    public static partial void IndexCorrupted(
        ILogger logger,
        string databasePath,
        Exception exception);

    [LoggerMessage(1201, LogLevel.Error, "Duplicate analysis failed for {LibraryRoot}")]
    public static partial void DuplicateAnalysisFailed(
        ILogger logger,
        string libraryRoot,
        Exception exception);
}
