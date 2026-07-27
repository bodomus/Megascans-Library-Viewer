using Microsoft.Extensions.Logging.Abstractions;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Infrastructure.Scanning;

namespace ScanVault.Infrastructure.Tests;

public sealed class LibraryScanServiceTests
{
    [Fact]
    public async Task AggregatesFailuresDuplicatesAndCommitsOnlyTheWinner()
    {
        using var temporary = new TemporaryDirectory();
        var libraryRoot = temporary.CreateDirectory("library");
        var winnerPath = Path.Combine(libraryRoot, "A", "same-id.json");
        var skippedPath = Path.Combine(libraryRoot, "B", "same-id.json");
        var malformedPath = Path.Combine(libraryRoot, "broken.json");
        var unrelatedPath = Path.Combine(libraryRoot, "settings.json");
        var inaccessiblePath = Path.Combine(libraryRoot, "protected");
        var scanner = new StubScanner(
            [skippedPath, malformedPath, unrelatedPath, winnerPath],
            [inaccessiblePath]);
        var parser = new StubParser(new Dictionary<string, AssetParseResult>(
            StringComparer.OrdinalIgnoreCase)
        {
            [winnerPath] = AssetParseResult.Success(CreateAsset("same-id", winnerPath)),
            [skippedPath] = AssetParseResult.Success(CreateAsset("SAME-ID", skippedPath)),
            [malformedPath] = AssetParseResult.Malformed("invalid JSON"),
            [unrelatedPath] = AssetParseResult.Unrelated()
        });
        var index = new RecordingIndex(new(1, 2, 3));
        var service = new LibraryScanService(
            scanner,
            parser,
            index,
            NullLogger<LibraryScanService>.Instance);

        var result = await service.ScanAsync(
            new(libraryRoot),
            progress: null,
            CancellationToken.None);

        Assert.Equal(1, result.IndexedAssets);
        Assert.Equal(1, result.SkippedMalformedFiles);
        Assert.Equal(1, result.SkippedUnrelatedFiles);
        Assert.Equal([malformedPath], result.MalformedJsonPaths);
        Assert.Equal([inaccessiblePath], result.InaccessibleDirectories);
        Assert.Equal(new IndexUpdateResult(1, 2, 3),
            new(result.AddedAssets, result.UpdatedAssets, result.RemovedAssets));
        var duplicate = Assert.Single(result.DuplicateGroups);
        Assert.Equal(Path.GetFullPath(winnerPath), duplicate.WinnerJsonPath);
        Assert.Equal([Path.GetFullPath(skippedPath)], duplicate.SkippedCopyJsonPaths);
        var committed = Assert.Single(index.CommittedAssets);
        Assert.Equal(Path.GetFullPath(winnerPath), committed.JsonPath);
    }

    private static AssetSummary CreateAsset(string id, string jsonPath) =>
        new(
            id,
            $"Asset {id}",
            "Surface",
            Path.GetDirectoryName(jsonPath)!,
            jsonPath,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            DateTimeOffset.UnixEpoch);

    private sealed class StubScanner(
        IReadOnlyList<string> files,
        IReadOnlyList<string> inaccessible) : IFileSystemScanner
    {
        public Task<FileDiscoveryResult> DiscoverAsync(
            string libraryRoot,
            IProgress<ScanProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new FileDiscoveryResult(files, inaccessible));
    }

    private sealed class StubParser(
        IReadOnlyDictionary<string, AssetParseResult> results) : IAssetMetadataParser
    {
        public Task<AssetParseResult> ParseAsync(
            string jsonPath,
            CancellationToken cancellationToken) =>
            Task.FromResult(results[jsonPath]);
    }

    private sealed class RecordingIndex(IndexUpdateResult update) : IAssetIndex
    {
        public bool RequiresNormalizationRescan => false;
        public IReadOnlyList<AssetSummary> CommittedAssets { get; private set; } = [];

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<AssetSummary>> GetAssetsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(CommittedAssets);

        public Task<IndexUpdateResult> ReplaceLibraryAsync(
            string libraryRoot,
            IReadOnlyList<AssetSummary> assets,
            ScanResult draftResult,
            CancellationToken cancellationToken)
        {
            CommittedAssets = assets;
            return Task.FromResult(update);
        }
    }
}
