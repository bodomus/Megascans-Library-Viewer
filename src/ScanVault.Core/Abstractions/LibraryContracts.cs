using ScanVault.Core.Models;

namespace ScanVault.Core.Abstractions;

/// <summary>Discovers metadata paths without parsing large asset payloads.</summary>
public interface IFileSystemScanner
{
    Task<FileDiscoveryResult> DiscoverAsync(
        string libraryRoot,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>Parses a single legacy Megascans metadata document.</summary>
public interface IAssetMetadataParser
{
    Task<AssetParseResult> ParseAsync(string jsonPath, CancellationToken cancellationToken);
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
    Task<IndexUpdateResult> ReplaceLibraryAsync(
        string libraryRoot,
        IReadOnlyList<AssetSummary> assets,
        ScanResult draftResult,
        CancellationToken cancellationToken);
}

/// <summary>Persists settings outside the source repository.</summary>
public interface ISettingsStore
{
    Task<LibrarySettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(LibrarySettings settings, CancellationToken cancellationToken);
}

/// <summary>Coordinates a complete, cancellable library refresh.</summary>
public interface ILibraryScanService
{
    Task<ScanResult> ScanAsync(
        LibrarySettings settings,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken);
}
