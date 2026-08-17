using System.Globalization;
using Microsoft.Data.Sqlite;
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

        var service = CreateService(index);
        var firstResult = await service.AnalyzeAsync(new(root), null, CancellationToken.None);
        var restarted = new SqliteAssetIndex(paths, NullLogger<SqliteAssetIndex>.Instance);
        var secondResult = await CreateService(restarted)
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
        await CreateService(index)
            .AnalyzeAsync(new(root), null, CancellationToken.None);

        await index.ReplaceLibraryAsync(root, [first], Draft(1), CancellationToken.None);
        var stale = await index.GetLatestDuplicateAnalysisAsync(root, includeStale: true, CancellationToken.None);
        var current = await index.GetLatestDuplicateAnalysisAsync(root, includeStale: false, CancellationToken.None);

        Assert.NotNull(stale);
        Assert.True(stale.Run.IsStale);
        Assert.Null(current);
    }

    [Fact]
    public async Task AnalysisUsesPersistedSameIdSourcesAfterIndexReplacement()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("library");
        var winner = CreateAsset(root, "same", "Stone", "shared.bin", [1, 2, 3]);
        var skipped = CreateAsset(root, "SAME", "Stone Copy", "copy.bin", [1, 2, 3], "same-copy");
        var paths = CreatePaths(temporary);
        var index = new SqliteAssetIndex(paths, NullLogger<SqliteAssetIndex>.Instance);
        await index.ReplaceLibraryAsync(root, [winner], Draft(1) with { DuplicateAnalysisSources = [winner, skipped] }, CancellationToken.None);

        var browsable = Assert.Single(await index.GetAssetsAsync(CancellationToken.None));
        Assert.Equal(winner.JsonPath, browsable.JsonPath);

        var result = await CreateService(index).AnalyzeAsync(new(root), null, CancellationToken.None);

        var group = Assert.Single(result.Groups);
        Assert.Equal(DuplicateCategory.ExactIdDuplicate, group.Category);
        Assert.Equal(
            new[] { winner.JsonPath, skipped.JsonPath }.Order(StringComparer.OrdinalIgnoreCase),
            group.Members.Select(static member => member.JsonPath).Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnalysisReportsConflictingSameIdSourcesAfterIndexReplacement()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("library");
        var winner = CreateAsset(root, "same", "Stone", "shared.bin", [1, 2, 3]);
        var skipped = CreateAsset(root, "SAME", "Stone Copy", "copy.bin", [9, 9, 9], "same-copy");
        var paths = CreatePaths(temporary);
        var index = new SqliteAssetIndex(paths, NullLogger<SqliteAssetIndex>.Instance);
        await index.ReplaceLibraryAsync(root, [winner], Draft(1) with { DuplicateAnalysisSources = [winner, skipped] }, CancellationToken.None);

        var result = await CreateService(index).AnalyzeAsync(new(root), null, CancellationToken.None);

        var group = Assert.Single(result.Groups);
        Assert.Equal(DuplicateCategory.ConflictingIdDuplicate, group.Category);
        Assert.Equal(
            new[] { winner.JsonPath, skipped.JsonPath }.Order(StringComparer.OrdinalIgnoreCase),
            group.Members.Select(static member => member.JsonPath).Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CancelledAnalysisKeepsPreviousCompletedRunCurrentAndDoesNotPersistPartialGroups()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("library");
        var first = CreateAsset(root, "first", "Stone", "shared.bin", [1, 2, 3]);
        var second = CreateAsset(root, "second", "Stone", "copy.bin", [1, 2, 3]);
        var paths = CreatePaths(temporary);
        var index = new SqliteAssetIndex(paths, NullLogger<SqliteAssetIndex>.Instance);
        await index.ReplaceLibraryAsync(root, [first, second], Draft(2), CancellationToken.None);
        var completed = await CreateService(index).AnalyzeAsync(new(root), null, CancellationToken.None);
        File.AppendAllBytes(first.Content.UnclassifiedFiles[0].Path, [4]);
        var blockingHasher = new BlockingHasher();
        using var cancellation = new CancellationTokenSource();
        var cancelledTask = new DuplicateAnalysisService(index, blockingHasher, NullLogger<DuplicateAnalysisService>.Instance)
            .AnalyzeAsync(new(root), null, cancellation.Token);

        await blockingHasher.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledTask);

        var latest = await index.GetLatestDuplicateAnalysisAsync(root, includeStale: false, CancellationToken.None);
        var runs = await ReadDuplicateRunStatusesAsync(paths.DatabasePath);
        var cancelled = Assert.Single(runs, run => run.Status == DuplicateAnalysisStatus.Cancelled);
        Assert.NotNull(latest);
        Assert.Equal(completed.Run.Id, latest.Run.Id);
        Assert.Equal(0, await ReadGroupCountAsync(paths.DatabasePath, cancelled.Id));
        Assert.True(File.Exists(first.Content.UnclassifiedFiles[0].Path));
        Assert.True(File.Exists(second.Content.UnclassifiedFiles[0].Path));
    }

    [Fact]
    public async Task FailedAnalysisKeepsPreviousCompletedRunCurrentAndLaterSuccessReplacesLatest()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("library");
        var first = CreateAsset(root, "first", "Stone", "shared.bin", [1, 2, 3]);
        var second = CreateAsset(root, "second", "Stone", "copy.bin", [1, 2, 3]);
        var paths = CreatePaths(temporary);
        var index = new SqliteAssetIndex(paths, NullLogger<SqliteAssetIndex>.Instance);
        await index.ReplaceLibraryAsync(root, [first, second], Draft(2), CancellationToken.None);
        var completed = await CreateService(index).AnalyzeAsync(new(root), null, CancellationToken.None);
        File.AppendAllBytes(first.Content.UnclassifiedFiles[0].Path, [4]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DuplicateAnalysisService(index, new ThrowingHasher(), NullLogger<DuplicateAnalysisService>.Instance)
                .AnalyzeAsync(new(root), null, CancellationToken.None));

        var latestAfterFailure = await index.GetLatestDuplicateAnalysisAsync(root, includeStale: false, CancellationToken.None);
        var failed = Assert.Single(await ReadDuplicateRunStatusesAsync(paths.DatabasePath), run => run.Status == DuplicateAnalysisStatus.Failed);
        Assert.NotNull(latestAfterFailure);
        Assert.Equal(completed.Run.Id, latestAfterFailure.Run.Id);
        Assert.Equal(0, await ReadGroupCountAsync(paths.DatabasePath, failed.Id));
        Assert.True(File.Exists(first.Content.UnclassifiedFiles[0].Path));
        Assert.True(File.Exists(second.Content.UnclassifiedFiles[0].Path));

        var recovered = await CreateService(index).AnalyzeAsync(new(root), null, CancellationToken.None);
        var latestAfterRecovery = await index.GetLatestDuplicateAnalysisAsync(root, includeStale: false, CancellationToken.None);

        Assert.NotNull(latestAfterRecovery);
        Assert.Equal(recovered.Run.Id, latestAfterRecovery.Run.Id);
        Assert.NotEqual(completed.Run.Id, recovered.Run.Id);
    }

    private static ScanVaultPaths CreatePaths(TemporaryDirectory temporary) =>
        new(
            Path.Combine(temporary.Path, "data", "scanvault.db"),
            Path.Combine(temporary.Path, "settings", "settings.json"),
            Path.Combine(temporary.Path, "cache"));

    private static DuplicateAnalysisService CreateService(IAssetIndex index) =>
        new(index, new Sha256DuplicateContentHasher(), NullLogger<DuplicateAnalysisService>.Instance);

    private static async Task<IReadOnlyList<(string Id, DuplicateAnalysisStatus Status)>> ReadDuplicateRunStatusesAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, status FROM duplicate_analysis_runs ORDER BY started_at_utc;";
        var runs = new List<(string, DuplicateAnalysisStatus)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) runs.Add((reader.GetString(0), (DuplicateAnalysisStatus)reader.GetInt32(1)));
        return runs;
    }

    private static async Task<int> ReadGroupCountAsync(string databasePath, string runId)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM duplicate_groups WHERE run_id = $runId;";
        command.Parameters.AddWithValue("$runId", runId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static AssetSummary CreateAsset(
        string root,
        string id,
        string name,
        string fileName,
        byte[] content,
        string? folderName = null)
    {
        var folder = Path.Combine(root, folderName ?? id);
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

    private sealed class BlockingHasher : IDuplicateContentHasher
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return string.Empty;
        }
    }

    private sealed class ThrowingHasher : IDuplicateContentHasher
    {
        public Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Controlled duplicate analysis failure.");
    }
}
