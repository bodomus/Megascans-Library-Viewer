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
    public async Task CreatesVersionedDatabaseUsingPatchedSystemSqlite()
    {
        using var temporary = new TemporaryDirectory();
        var (index, paths) = CreateIndex(temporary);

        await index.InitializeAsync(CancellationToken.None);

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
