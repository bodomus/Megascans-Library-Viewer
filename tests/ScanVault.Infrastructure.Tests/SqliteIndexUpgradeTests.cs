using System.Collections;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Infrastructure.Configuration;
using ScanVault.Infrastructure.Persistence;

namespace ScanVault.Infrastructure.Tests;

public sealed class SqliteIndexUpgradeTests
{
    [Fact]
    public async Task VersionOneIndexRemainsReadableAndRequiresCorrectiveRescan()
    {
        using var temporary = new TemporaryDirectory();
        var (index, paths, root) = await CreateVersionOneIndexAsync(temporary);

        await index.InitializeAsync(CancellationToken.None);
        var asset = Assert.Single(await index.GetAssetsAsync(CancellationToken.None));

        Assert.True(index.RequiresNormalizationRescan);
        Assert.Equal("legacy", asset.Id);
        Assert.Equal(new ImageResolution(400400, 400400), asset.MaxResolution);
        Assert.Equal(2, await ReadScalarAsync(paths.DatabasePath, "SELECT version FROM schema_info;"));
        Assert.Equal(
            1,
            await ReadScalarAsync(paths.DatabasePath, "SELECT normalization_version FROM schema_info;"));
        Assert.Equal(root, asset.AssetFolderPath);
    }

    [Fact]
    public async Task SuccessfulReplacementAtomicallyPromotesNormalizationVersion()
    {
        using var temporary = new TemporaryDirectory();
        var (index, paths, root) = await CreateVersionOneIndexAsync(temporary);
        await index.InitializeAsync(CancellationToken.None);
        var corrected = CreateAsset("corrected", root, new ImageResolution(4096, 2048));

        await index.ReplaceLibraryAsync(
            root,
            [corrected],
            Draft(1),
            CancellationToken.None);

        Assert.False(index.RequiresNormalizationRescan);
        Assert.Equal(
            SqliteAssetIndex.CurrentNormalizationVersion,
            await ReadScalarAsync(paths.DatabasePath, "SELECT normalization_version FROM schema_info;"));
        var saved = Assert.Single(await index.GetAssetsAsync(CancellationToken.None));
        Assert.Equal("corrected", saved.Id);
        Assert.Equal(new ImageResolution(4096, 2048), saved.MaxResolution);
    }

    [Fact]
    public async Task CancelledCorrectiveRescanKeepsOldRowsAndOldMarker()
    {
        using var temporary = new TemporaryDirectory();
        var (index, paths, root) = await CreateVersionOneIndexAsync(temporary);
        await index.InitializeAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var replacement = new CancelOnSecondItemList(
        [
            CreateAsset("new-a", root, new ImageResolution(2048, 2048)),
            CreateAsset("new-b", root, new ImageResolution(4096, 4096))
        ], cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            index.ReplaceLibraryAsync(root, replacement, Draft(2), cancellation.Token));

        Assert.True(index.RequiresNormalizationRescan);
        Assert.Equal(
            1,
            await ReadScalarAsync(paths.DatabasePath, "SELECT normalization_version FROM schema_info;"));
        Assert.Equal("legacy", Assert.Single(await index.GetAssetsAsync(CancellationToken.None)).Id);
    }

    private static async Task<(SqliteAssetIndex Index, ScanVaultPaths Paths, string Root)>
        CreateVersionOneIndexAsync(TemporaryDirectory temporary)
    {
        var root = temporary.CreateDirectory("library");
        var paths = new ScanVaultPaths(
            Path.Combine(temporary.Path, "data", "scanvault.db"),
            Path.Combine(temporary.Path, "settings", "settings.json"),
            Path.Combine(temporary.Path, "cache"));
        var index = new SqliteAssetIndex(paths, NullLogger<SqliteAssetIndex>.Instance);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.DatabasePath)!);
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = paths.DatabasePath,
                Pooling = false
            }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE schema_info (version INTEGER NOT NULL);
            INSERT INTO schema_info(version) VALUES (1);
            CREATE TABLE assets (
                id TEXT PRIMARY KEY COLLATE NOCASE,
                library_root TEXT NOT NULL,
                name TEXT NOT NULL,
                asset_type TEXT NOT NULL,
                asset_folder_path TEXT NOT NULL,
                json_path TEXT NOT NULL UNIQUE,
                thumbnail_path TEXT NULL,
                preview_path TEXT NULL,
                biome TEXT NULL,
                region TEXT NULL,
                physical_size TEXT NULL,
                max_resolution INTEGER NULL,
                texel_density REAL NULL,
                average_color TEXT NULL,
                categories_json TEXT NOT NULL,
                tags_json TEXT NOT NULL,
                last_write_time_utc TEXT NOT NULL
            );
            CREATE TABLE tags (
                tag_id INTEGER PRIMARY KEY AUTOINCREMENT,
                kind INTEGER NOT NULL,
                value TEXT NOT NULL COLLATE NOCASE,
                UNIQUE(kind, value)
            );
            CREATE TABLE asset_tags (
                asset_id TEXT NOT NULL COLLATE NOCASE,
                tag_id INTEGER NOT NULL,
                PRIMARY KEY(asset_id, tag_id)
            );
            CREATE TABLE scan_state (
                singleton_id INTEGER PRIMARY KEY CHECK(singleton_id = 1),
                library_root TEXT NOT NULL,
                completed_at_utc TEXT NOT NULL,
                result_json TEXT NOT NULL
            );
            INSERT INTO assets(
                id, library_root, name, asset_type, asset_folder_path, json_path,
                max_resolution, categories_json, tags_json, last_write_time_utc)
            VALUES (
                'legacy', $root, 'Legacy Asset', 'normal', $root, $json,
                400400, '[]', '[]', '1970-01-01T00:00:00.0000000+00:00');
            """;
        command.Parameters.AddWithValue("$root", root);
        command.Parameters.AddWithValue("$json", Path.Combine(root, "legacy.json"));
        await command.ExecuteNonQueryAsync();
        return (index, paths, root);
    }

    private static AssetSummary CreateAsset(
        string id,
        string root,
        ImageResolution resolution) =>
        new(
            id,
            $"Asset {id}",
            "Surface",
            root,
            Path.Combine(root, $"{id}.json"),
            null,
            null,
            null,
            null,
            null,
            resolution,
            null,
            null,
            [],
            [],
            DateTimeOffset.UnixEpoch);

    private static ScanResult Draft(int count) =>
        new(0, 0, 0, count, 0, 0, [], [], [], TimeSpan.Zero);

    private static async Task<int> ReadScalarAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Pooling = false
            }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

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
