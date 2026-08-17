using Microsoft.Extensions.Logging.Abstractions;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Infrastructure.Configuration;
using ScanVault.Infrastructure.Persistence;
using ScanVault.Infrastructure.Scanning;

namespace ScanVault.Infrastructure.Tests;

public sealed class DuplicateAnalysisServiceTests
{
    [Fact]
    public async Task AnalysisPersistsHashCacheAndReusesItAfterRestart()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("library");
        var first = CreateAsset(root, "first", "Stone", "shared.bin", [1, 2, 3]);
        var second = CreateAsset(root, "second", "Stone", "copy.bin", [1, 2, 3]);
        var paths = CreatePaths(temporary);
        var index = new SqliteAssetIndex(paths, NullLogger<SqliteAssetIndex>.Instance);
        await index.ReplaceLibraryAsync(root, [first, second], Draft(2), CancellationToken.None);

        var service = new DuplicateAnalysisService(index, NullLogger<DuplicateAnalysisService>.Instance);
        var firstResult = await service.AnalyzeAsync(new(root), null, CancellationToken.None);
        var restarted = new SqliteAssetIndex(paths, NullLogger<SqliteAssetIndex>.Instance);
        var secondResult = await new DuplicateAnalysisService(restarted, NullLogger<DuplicateAnalysisService>.Instance)
            .AnalyzeAsync(new(root), null, CancellationToken.None);

        Assert.Contains(firstResult.Groups, group => group.Category == DuplicateCategory.ExactContentDuplicate);
        Assert.Equal(2, firstResult.Run.FilesHashed);
        Assert.Equal(0, firstResult.Run.CacheHits);
        Assert.Equal(0, secondResult.Run.FilesHashed);
        Assert.Equal(2, secondResult.Run.CacheHits);
    }

    [Fact]
    public async Task CompletedDuplicateAnalysisBecomesStaleAfterRescan()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("library");
        var first = CreateAsset(root, "first", "Stone", "shared.bin", [1, 2, 3]);
        var second = CreateAsset(root, "second", "Stone", "copy.bin", [1, 2, 3]);
        var paths = CreatePaths(temporary);
        var index = new SqliteAssetIndex(paths, NullLogger<SqliteAssetIndex>.Instance);
        await index.ReplaceLibraryAsync(root, [first, second], Draft(2), CancellationToken.None);
        await new DuplicateAnalysisService(index, NullLogger<DuplicateAnalysisService>.Instance)
            .AnalyzeAsync(new(root), null, CancellationToken.None);

        await index.ReplaceLibraryAsync(root, [first], Draft(1), CancellationToken.None);
        var stale = await index.GetLatestDuplicateAnalysisAsync(root, includeStale: true, CancellationToken.None);
        var current = await index.GetLatestDuplicateAnalysisAsync(root, includeStale: false, CancellationToken.None);

        Assert.NotNull(stale);
        Assert.True(stale.Run.IsStale);
        Assert.Null(current);
    }

    private static ScanVaultPaths CreatePaths(TemporaryDirectory temporary) =>
        new(
            Path.Combine(temporary.Path, "data", "scanvault.db"),
            Path.Combine(temporary.Path, "settings", "settings.json"),
            Path.Combine(temporary.Path, "cache"));

    private static AssetSummary CreateAsset(
        string root,
        string id,
        string name,
        string fileName,
        byte[] content)
    {
        var folder = Path.Combine(root, id);
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, fileName);
        File.WriteAllBytes(file, content);
        var inventory = new AssetContentInventory(
            [],
            [],
            [new(file, "Synthetic test payload.")],
            AssetCompletenessStatus.Usable,
            []);
        return new(
            id,
            name,
            "Surface",
            folder,
            Path.Combine(folder, $"{id}.json"),
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
            DateTimeOffset.UnixEpoch)
        {
            Content = inventory
        };
    }

    private static ScanResult Draft(int count) =>
        new(0, 0, 0, count, 0, 0, [], [], [], TimeSpan.Zero);
}
