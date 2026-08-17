using ScanVault.Core.Models;

namespace ScanVault.Core.Abstractions;

/// <summary>Discovers metadata paths without parsing large asset payloads.</summary>
public interface IFileSystemScanner
{
    Task<FileDiscoveryResult> DiscoverAsync(string libraryRoot, IProgress<ScanProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>Parses a single legacy Megascans metadata document.</summary>
public interface IAssetMetadataParser
{
    Task<AssetParseResult> ParseAsync(string jsonPath, CancellationToken cancellationToken);
}

/// <summary>Inventories one asset folder without reading mesh or texture payloads.</summary>
public interface IAssetContentInventoryService
{
    Task<AssetInventoryResult> InventoryAsync(AssetSummary asset, CancellationToken cancellationToken);
}

/// <summary>Provides transactional persistence and indexed browsing queries.</summary>
public interface IAssetIndex
{
    IndexCompatibilityInfo Compatibility { get; }
    bool RequiresNormalizationRescan { get; }
    Task<IndexCompatibilityInfo> InspectCompatibilityAsync(CancellationToken cancellationToken);
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<IndexDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AssetSummary>> GetAssetsAsync(CancellationToken cancellationToken);
    Task<string> BeginScanRunAsync(string libraryRoot, string applicationVersion, string commitSha, CancellationToken cancellationToken);
    Task FinishScanRunAsync(string scanRunId, ScanRunStatus status, string? errorMessage, CancellationToken cancellationToken);
    Task<IReadOnlyList<ScanRunSummary>> GetScanRunsAsync(int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<ScanChangeSummary>> GetScanChangesAsync(string scanRunId, AssetChangeKind kind, int limit, CancellationToken cancellationToken);
    Task<IndexUpdateResult> ReplaceLibraryAsync(string libraryRoot, IReadOnlyList<AssetSummary> assets, ScanResult draftResult, string scanRunId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AssetSummary>> GetDuplicateAnalysisSourcesAsync(string libraryRoot, CancellationToken cancellationToken) =>
        GetAssetsAsync(cancellationToken);
    Task<DuplicateAnalysisRun> BeginDuplicateAnalysisRunAsync(string libraryRoot, int totalAssets, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Duplicate analysis persistence is not available for this index implementation.");
    Task FinishDuplicateAnalysisRunAsync(string runId, DuplicateAnalysisStatus status, string? errorMessage, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Duplicate analysis persistence is not available for this index implementation.");
    Task PersistDuplicateAnalysisAsync(DuplicateAnalysisPersistRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Duplicate analysis persistence is not available for this index implementation.");
    Task<DuplicateAnalysisResult?> GetLatestDuplicateAnalysisAsync(string libraryRoot, bool includeStale, CancellationToken cancellationToken) =>
        Task.FromResult<DuplicateAnalysisResult?>(null);
    Task<FileHashCacheEntry?> GetFileHashAsync(string normalizedPath, string hashAlgorithm, int hashAlgorithmVersion, CancellationToken cancellationToken) =>
        Task.FromResult<FileHashCacheEntry?>(null);
    Task UpsertFileHashAsync(FileHashCacheEntry entry, CancellationToken cancellationToken) =>
        throw new NotSupportedException("File hash persistence is not available for this index implementation.");
}

/// <summary>Persists settings outside the source repository.</summary>
public interface ISettingsStore
{
    Task<LibrarySettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(LibrarySettings settings, CancellationToken cancellationToken);
}

/// <summary>Persists user-defined smart collection definitions outside the asset index.</summary>
public interface ISmartCollectionStore
{
    Task<IReadOnlyList<SmartCollectionRecord>> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyList<SmartCollectionRecord> collections, CancellationToken cancellationToken);
}

/// <summary>Coordinates a complete, cancellable library refresh.</summary>
public interface ILibraryScanService
{
    Task<ScanResult> ScanAsync(LibrarySettings settings, IProgress<ScanProgress>? progress, CancellationToken cancellationToken);
}

public interface IDuplicateAnalysisService
{
    Task<DuplicateAnalysisResult> AnalyzeAsync(
        LibrarySettings settings,
        IProgress<DuplicateAnalysisProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed record FileHashCacheEntry(
    string NormalizedPath,
    long FileSizeBytes,
    DateTimeOffset LastWriteTimeUtc,
    string HashAlgorithm,
    int HashAlgorithmVersion,
    string ContentHash,
    DateTimeOffset ComputedAtUtc);
