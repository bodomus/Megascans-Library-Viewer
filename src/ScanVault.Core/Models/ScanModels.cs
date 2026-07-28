namespace ScanVault.Core.Models;

public enum ScanPhase
{
    Validating,
    Discovering,
    Parsing,
    Inventory,
    Committing,
    Refreshing,
    Completed
}

public sealed record ScanProgress(ScanPhase Phase, int DiscoveredFiles, int ProcessedFiles, string? CurrentPath = null);
public sealed record FileDiscoveryResult(IReadOnlyList<string> MetadataFiles, IReadOnlyList<string> InaccessibleDirectories);

public enum AssetParseStatus { Success, UnrelatedJson, MalformedJson }

public sealed record AssetParseResult(AssetParseStatus Status, AssetSummary? Asset = null, string? Error = null)
{
    public static AssetParseResult Success(AssetSummary asset) => new(AssetParseStatus.Success, asset);
    public static AssetParseResult Unrelated() => new(AssetParseStatus.UnrelatedJson);
    public static AssetParseResult Malformed(string error) => new(AssetParseStatus.MalformedJson, Error: error);
}

public sealed record DuplicateAssetGroup(string AssetId, string WinnerJsonPath, IReadOnlyList<string> SkippedCopyJsonPaths);
public sealed record DuplicateResolution(IReadOnlyList<AssetSummary> Assets, IReadOnlyList<DuplicateAssetGroup> DuplicateGroups);
public sealed record IndexUpdateResult(int AddedAssets, int UpdatedAssets, int RemovedAssets);

public sealed record ScanResult(
    int AddedAssets,
    int UpdatedAssets,
    int RemovedAssets,
    int IndexedAssets,
    int SkippedMalformedFiles,
    int SkippedUnrelatedFiles,
    IReadOnlyList<string> MalformedJsonPaths,
    IReadOnlyList<string> InaccessibleDirectories,
    IReadOnlyList<DuplicateAssetGroup> DuplicateGroups,
    TimeSpan Elapsed)
{
    public int AssetsInventoried { get; init; }
    public int MeshFilesFound { get; init; }
    public int TextureFilesFound { get; init; }
    public int AmbiguousAssets { get; init; }
    public int AssetsMissingCriticalFiles { get; init; }
}
