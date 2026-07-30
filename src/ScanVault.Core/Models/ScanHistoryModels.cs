namespace ScanVault.Core.Models;

public enum ScanRunStatus
{
    Running,
    Completed,
    Cancelled,
    Failed
}

public enum AssetChangeKind
{
    Added,
    Changed,
    Removed,
    Unchanged
}

[Flags]
public enum AssetChangeReason
{
    None = 0,
    Metadata = 1 << 0,
    Path = 1 << 1,
    Resolution = 1 << 2,
    Inventory = 1 << 3,
    Files = 1 << 4,
    Completeness = 1 << 5
}

public sealed record ScanRunSummary(
    string Id,
    string LibraryIdentity,
    string LibraryRoot,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    ScanRunStatus Status,
    string ApplicationVersion,
    string CommitSha,
    int SchemaVersion,
    int NormalizationVersion,
    int FingerprintVersion,
    int TotalAssets,
    int AddedAssets,
    int ChangedAssets,
    int RemovedAssets,
    int UnchangedAssets,
    string? ErrorMessage,
    bool IsInitialBaseline)
{
    public TimeSpan? Duration => FinishedAtUtc is null ? null : FinishedAtUtc.Value - StartedAtUtc;
}

public sealed record ScanChangeSummary(
    string ScanRunId,
    string AssetIdentity,
    AssetChangeKind Kind,
    AssetChangeReason Flags,
    string AssetId,
    string Name,
    string AssetType,
    string? PreviousPath,
    string? CurrentPath,
    AssetCompletenessStatus Completeness,
    bool AssetExists);

