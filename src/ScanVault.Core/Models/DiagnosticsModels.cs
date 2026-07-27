namespace ScanVault.Core.Models;

public enum IndexCompatibilityState
{
    Compatible,
    RequiresMigration,
    RequiresRescan,
    NewerVersionUnsupported,
    Missing,
    Corrupted
}

public sealed record IndexCompatibilityInfo(
    IndexCompatibilityState State,
    int? DatabaseSchemaVersion,
    int? MetadataNormalizationVersion,
    bool IsReadable,
    bool CanWrite,
    bool RequiresRescan,
    string Guidance);

public enum ScanAttemptStatus
{
    NotRun,
    Succeeded,
    Cancelled,
    Failed
}

public sealed record PersistedScanMetadata(
    DateTimeOffset LastSuccessfulScanUtc,
    TimeSpan LastScanDuration,
    ScanAttemptStatus LastScanStatus,
    int AddedCount,
    int UpdatedCount,
    int RemovedCount,
    int SkippedCount,
    int InaccessibleFolderCount)
{
    public string ResultSummary =>
        $"+{AddedCount}, ~{UpdatedCount}, -{RemovedCount}; " +
        $"{SkippedCount} skipped, {InaccessibleFolderCount} inaccessible";
}

public sealed record IndexDiagnostics(
    IndexCompatibilityInfo Compatibility,
    int IndexedAssetCount,
    PersistedScanMetadata? LastSuccessfulScan);

public sealed record DiagnosticsSnapshot(
    string ApplicationVersion,
    string InformationalVersion,
    string CommitSha,
    string BuildConfiguration,
    string RuntimeVersion,
    string OperatingSystem,
    string ProcessArchitecture,
    string? LibraryRoot,
    int IndexedAssetCount,
    DateTimeOffset? LastSuccessfulScan,
    TimeSpan? LastScanDuration,
    ScanAttemptStatus LastScanStatus,
    string? LastScanResult,
    string DatabasePath,
    string ThumbnailCachePath,
    int? DatabaseSchemaVersion,
    int? MetadataNormalizationVersion,
    IndexCompatibilityState IndexCompatibilityState,
    bool RequiresRescan,
    string CompatibilityGuidance,
    string? SettingsPath = null,
    string? CurrentSortMode = null,
    string? CurrentSelectedFolder = null);

public sealed record DiagnosticField(string Label, string Value);
