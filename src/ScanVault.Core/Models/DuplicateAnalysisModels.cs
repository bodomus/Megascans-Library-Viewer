namespace ScanVault.Core.Models;

public enum DuplicateCategory
{
    ExactIdDuplicate,
    ConflictingIdDuplicate,
    ExactContentDuplicate,
    ProbableDuplicate,
    PartialDuplicate
}

public enum DuplicateConfidence
{
    Exact,
    High,
    Medium,
    Low
}

public enum DuplicateAnalysisStatus
{
    Running,
    Completed,
    Cancelled,
    Failed
}

public enum DuplicateHashStatus
{
    NotRequired,
    CacheHit,
    Computed,
    Missing,
    Failed
}

public enum DuplicateAnalysisPhase
{
    LoadingAssets,
    GeneratingCandidates,
    Hashing,
    Classifying,
    Persisting,
    Completed
}

public sealed record DuplicateAnalysisProgress(
    DuplicateAnalysisPhase Phase,
    int ProcessedFiles,
    int TotalFiles,
    long ProcessedBytes,
    long TotalBytes,
    int GroupsFound,
    string? CurrentPath = null);

public sealed record DuplicateFileFingerprint(
    string RelativePath,
    long SizeBytes,
    DateTimeOffset LastWriteTimeUtc,
    string ContentHash,
    DuplicateHashStatus HashStatus);

public sealed record DuplicateAssetFingerprint(
    AssetSummary Asset,
    string LibraryRelativePath,
    IReadOnlyList<DuplicateFileFingerprint> Files)
{
    public long TotalSizeBytes => Files.Sum(static file => file.SizeBytes);
    public int FileCount => Files.Count;
    public IReadOnlySet<string> FileNames { get; } = Files
        .Select(static file => Path.GetFileName(file.RelativePath))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> ContentHashes { get; } = Files
        .Where(static file => !string.IsNullOrWhiteSpace(file.ContentHash))
        .Select(static file => file.ContentHash)
        .ToHashSet(StringComparer.Ordinal);
}

public sealed record DuplicateGroupMember(
    string AssetId,
    string AssetName,
    string AssetType,
    string RelativePath,
    string AssetFolderPath,
    AssetCompletenessStatus Completeness,
    int FileCount,
    long TotalSizeBytes,
    DuplicateHashStatus HashStatus);

public sealed record DuplicateReason(
    string Field,
    string Message);

public sealed record DuplicateGroupResult(
    string GroupId,
    DuplicateCategory Category,
    DuplicateConfidence Confidence,
    IReadOnlyList<DuplicateReason> Reasons,
    IReadOnlyList<string> MatchedFields,
    IReadOnlyList<string> DifferentFields,
    long EstimatedDuplicateSizeBytes,
    IReadOnlyList<DuplicateGroupMember> Members);

public sealed record DuplicateAnalysisSummary(
    int ExactDuplicateGroups,
    int ConflictingIdGroups,
    int ProbableDuplicateGroups,
    int PartialDuplicateGroups,
    int AssetsInvolved,
    long PotentialReclaimableSizeBytes);

public sealed record DuplicateAnalysisRun(
    string Id,
    string LibraryIdentity,
    string LibraryRoot,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    DuplicateAnalysisStatus Status,
    bool IsStale,
    int TotalAssets,
    int CandidateAssets,
    int FilesHashed,
    long BytesHashed,
    int CacheHits,
    TimeSpan? Duration,
    string? ErrorMessage,
    DuplicateAnalysisSummary Summary);

public sealed record DuplicateAnalysisResult(
    DuplicateAnalysisRun Run,
    IReadOnlyList<DuplicateGroupResult> Groups);

public sealed record DuplicateAnalysisPersistRequest(
    DuplicateAnalysisRun Run,
    IReadOnlyList<DuplicateGroupResult> Groups);
