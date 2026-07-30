using System.Collections;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Infrastructure.Configuration;
using ScanVault.Infrastructure.Persistence;

namespace ScanVault.Infrastructure.Tests;

public sealed class SqliteAssetIndexTests
{
    [Fact]
    public async Task FirstReplacementCreatesVersionedDatabaseUsingPatchedSystemSqlite()
    {
        using var temporary = new TemporaryDirectory();
        var (index, paths) = CreateIndex(temporary);

        await index.InitializeAsync(CancellationToken.None);
        Assert.Equal(IndexCompatibilityState.Missing, index.Compatibility.State);
        Assert.False(File.Exists(paths.DatabasePath));

        var root = temporary.CreateDirectory("library");
        await index.ReplaceLibraryAsync(
            root,
            [CreateAsset("initial", root, DateTimeOffset.UnixEpoch)],
            Draft(1),
            CancellationToken.None);

        Assert.True(File.Exists(paths.DatabasePath));
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabasePath, Pooling = false }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT version FROM schema_info), sqlite_version();";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(SqliteAssetIndex.CurrentSchemaVersion, reader.GetInt32(0));
        Assert.True(Version.Parse(reader.GetString(1)) >= new Version(3, 50, 2));
    }

    [Fact]
    public async Task UpsertsAndRemovesStaleAssetsOnlyOnSuccessfulCommit()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("library");
        var (index, _) = CreateIndex(temporary);
        var first = CreateAsset("first", root, DateTimeOffset.UnixEpoch);
        var second = CreateAsset("second", root, DateTimeOffset.UnixEpoch);

        var initial = await index.ReplaceLibraryAsync(
            root,
            [first, second],
            Draft(2),
            CancellationToken.None);
        var updatedFirst = first with
        {
            Name = "Updated",
            LastWriteTimeUtc = DateTimeOffset.UnixEpoch.AddMinutes(1)
        };
        var refresh = await index.ReplaceLibraryAsync(
            root,
            [updatedFirst],
            Draft(1),
            CancellationToken.None);
        var assets = await index.GetAssetsAsync(CancellationToken.None);

        Assert.Equal(new IndexUpdateResult(2, 0, 0), initial);
        Assert.Equal(new IndexUpdateResult(0, 1, 1), refresh);
        var remaining = Assert.Single(assets);
        Assert.Equal("Updated", remaining.Name);
    }

    [Fact]
    public async Task CancellationDuringTransactionPreservesPreviousIndex()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("library");
        var (index, _) = CreateIndex(temporary);
        var previous = CreateAsset("previous", root, DateTimeOffset.UnixEpoch);
        await index.ReplaceLibraryAsync(
            root,
            [previous],
            Draft(1),
            CancellationToken.None);

        using var cancellation = new CancellationTokenSource();
        var replacement = new CancelOnSecondItemList(
            [
                CreateAsset("new-a", root, DateTimeOffset.UtcNow),
                CreateAsset("new-b", root, DateTimeOffset.UtcNow)
            ],
            cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => index.ReplaceLibraryAsync(
                root,
                replacement,
                Draft(2),
                cancellation.Token));
        var assets = await index.GetAssetsAsync(CancellationToken.None);

        var preserved = Assert.Single(assets);
        Assert.Equal("previous", preserved.Id);
    }


    [Fact]
    public async Task ScanHistoryClassifiesInitialAndSubsequentRuns()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("library");
        var (index, _) = CreateIndex(temporary);
        var first = CreateAsset("first", root, DateTimeOffset.UnixEpoch);
        var removed = CreateAsset("removed", root, DateTimeOffset.UnixEpoch);

        var firstRun = await index.BeginScanRunAsync(root, "9.8.7", "abcdef1", CancellationToken.None);
        var initial = await index.ReplaceLibraryAsync(root, [first, removed], Draft(2), firstRun, CancellationToken.None);

        Assert.True(initial.IsInitialBaseline);
        Assert.Equal(2, initial.ChangedAssets + initial.UnchangedAssets + initial.AddedAssets);
        Assert.Equal(2, (await index.GetScanChangesAsync(firstRun, AssetChangeKind.Added, 10, CancellationToken.None)).Count);

        var changedFirst = first with { Name = "Changed", LastWriteTimeUtc = DateTimeOffset.UnixEpoch.AddMinutes(1) };
        var added = CreateAsset("added", root, DateTimeOffset.UnixEpoch);
        var secondRun = await index.BeginScanRunAsync(root, "9.8.7", "abcdef1", CancellationToken.None);
        var second = await index.ReplaceLibraryAsync(root, [changedFirst, added], Draft(2), secondRun, CancellationToken.None);

        Assert.False(second.IsInitialBaseline);
        Assert.Equal(1, second.AddedAssets);
        Assert.Equal(1, second.ChangedAssets);
        Assert.Equal(1, second.RemovedAssets);
        Assert.Equal(0, second.UnchangedAssets);
        Assert.Equal("added", Assert.Single(await index.GetScanChangesAsync(secondRun, AssetChangeKind.Added, 10, CancellationToken.None)).AssetId);
        Assert.Equal("first", Assert.Single(await index.GetScanChangesAsync(secondRun, AssetChangeKind.Changed, 10, CancellationToken.None)).AssetId);
        Assert.Equal("removed", Assert.Single(await index.GetScanChangesAsync(secondRun, AssetChangeKind.Removed, 10, CancellationToken.None)).AssetId);
    }

    [Fact]
    public async Task CancelledScanRunDoesNotBecomeBaseline()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("library");
        var (index, _) = CreateIndex(temporary);
        var cancelledRun = await index.BeginScanRunAsync(root, "9.8.7", "abcdef1", CancellationToken.None);
        await index.FinishScanRunAsync(cancelledRun, ScanRunStatus.Cancelled, "cancelled", CancellationToken.None);

        var asset = CreateAsset("baseline", root, DateTimeOffset.UnixEpoch);
        var firstCompleted = await index.BeginScanRunAsync(root, "9.8.7", "abcdef1", CancellationToken.None);
        var initial = await index.ReplaceLibraryAsync(root, [asset], Draft(1), firstCompleted, CancellationToken.None);
        var secondCompleted = await index.BeginScanRunAsync(root, "9.8.7", "abcdef1", CancellationToken.None);
        var second = await index.ReplaceLibraryAsync(root, [asset], Draft(1), secondCompleted, CancellationToken.None);

        Assert.True(initial.IsInitialBaseline);
        Assert.False(second.IsInitialBaseline);
        Assert.Equal(1, second.UnchangedAssets);
        Assert.Equal(ScanRunStatus.Cancelled, (await index.GetScanRunsAsync(10, CancellationToken.None)).Single(run => run.Id == cancelledRun).Status);
    }

    private static (SqliteAssetIndex Index, ScanVaultPaths Paths) CreateIndex(
        TemporaryDirectory temporary)
    {
        var paths = new ScanVaultPaths(
            Path.Combine(temporary.Path, "data", "scanvault.db"),
            Path.Combine(temporary.Path, "settings", "settings.json"),
            Path.Combine(temporary.Path, "cache"));
        return (new(paths, NullLogger<SqliteAssetIndex>.Instance), paths);
    }

    private static AssetSummary CreateAsset(
        string id,
        string root,
        DateTimeOffset lastWrite) =>
        new(
            id,
            $"Asset {id}",
            "surface",
            Path.Combine(root, id),
            Path.Combine(root, id, $"{id}.json"),
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
            lastWrite);

    private static ScanResult Draft(int count) =>
        new(0, 0, 0, count, 0, 0, [], [], [], TimeSpan.Zero);

    private sealed class CancelOnSecondItemList(
        IReadOnlyList<AssetSummary> items,
        CancellationTokenSource cancellation) : IReadOnlyList<AssetSummary>
    {
        public int Count => items.Count;
        public AssetSummary this[int index] => items[index];

        public IEnumerator<AssetSummary> GetEnumerator() => Enumerate().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private IEnumerable<AssetSummary> Enumerate()
        {
            for (var index = 0; index < items.Count; index++)
            {
                if (index == 1)
                {
                    cancellation.Cancel();
                }

                yield return items[index];
            }
        }
    }
}
