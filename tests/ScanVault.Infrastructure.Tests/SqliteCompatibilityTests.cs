using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Infrastructure.Configuration;
using ScanVault.Infrastructure.Persistence;

namespace ScanVault.Infrastructure.Tests;

public sealed class SqliteCompatibilityTests
{
    [Fact]
    public async Task MissingIndexStaysMissingUntilFirstSafeReplacement()
    {
        using var temporary = new TemporaryDirectory();
        var (index, paths) = CreateIndex(temporary);

        await index.InitializeAsync(CancellationToken.None);

        Assert.Equal(IndexCompatibilityState.Missing, index.Compatibility.State);
        Assert.True(index.Compatibility.CanWrite);
        Assert.False(File.Exists(paths.DatabasePath));
        Assert.Empty(await index.GetAssetsAsync(CancellationToken.None));

        var root = temporary.CreateDirectory("library");
        await index.ReplaceLibraryAsync(
            root,
            [CreateAsset("first", root)],
            Draft(1),
            CancellationToken.None);

        Assert.True(File.Exists(paths.DatabasePath));
        Assert.Equal(IndexCompatibilityState.Compatible, index.Compatibility.State);
    }

    [Fact]
    public async Task InspectReportsRequiresMigrationBeforeKnownVersionOneUpgrade()
    {
        using var temporary = new TemporaryDirectory();
        var (index, paths) = CreateIndex(temporary);
        await CreateVersionOneDatabaseAsync(paths.DatabasePath);

        var before = await index.InspectCompatibilityAsync(CancellationToken.None);
        await index.InitializeAsync(CancellationToken.None);

        Assert.Equal(IndexCompatibilityState.RequiresMigration, before.State);
        Assert.Equal(1, before.DatabaseSchemaVersion);
        Assert.Equal(IndexCompatibilityState.RequiresRescan, index.Compatibility.State);
        Assert.Equal(2, index.Compatibility.DatabaseSchemaVersion);
        Assert.Equal(1, index.Compatibility.MetadataNormalizationVersion);
    }

    [Fact]
    public async Task OutdatedNormalizationRemainsReadableAndRequiresRescan()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("library");
        var (index, paths) = CreateIndex(temporary);
        await index.ReplaceLibraryAsync(
            root,
            [CreateAsset("legacy-readable", root)],
            Draft(1),
            CancellationToken.None);
        await ExecuteAsync(paths.DatabasePath, "UPDATE schema_info SET normalization_version = 1;");
        var reopened = new SqliteAssetIndex(paths, NullLogger<SqliteAssetIndex>.Instance);

        await reopened.InitializeAsync(CancellationToken.None);

        Assert.Equal(IndexCompatibilityState.RequiresRescan, reopened.Compatibility.State);
        Assert.True(reopened.Compatibility.IsReadable);
        Assert.True(reopened.Compatibility.CanWrite);
        Assert.Equal("legacy-readable", Assert.Single(await reopened.GetAssetsAsync(CancellationToken.None)).Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NewerSchemaOrNormalizationBlocksWritesAndPreservesBytes(bool newerSchema)
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("library");
        var (index, paths) = CreateIndex(temporary);
        await index.ReplaceLibraryAsync(
            root,
            [CreateAsset("current", root)],
            Draft(1),
            CancellationToken.None);
        await ExecuteAsync(
            paths.DatabasePath,
            newerSchema
                ? "UPDATE schema_info SET version = 99; DROP TABLE tags;"
                : "UPDATE schema_info SET normalization_version = 99;");
        var before = await File.ReadAllBytesAsync(paths.DatabasePath);
        var reopened = new SqliteAssetIndex(paths, NullLogger<SqliteAssetIndex>.Instance);

        await reopened.InitializeAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reopened.ReplaceLibraryAsync(root, [], Draft(0), CancellationToken.None));

        Assert.Equal(IndexCompatibilityState.NewerVersionUnsupported, reopened.Compatibility.State);
        Assert.False(reopened.Compatibility.CanWrite);
        Assert.Equal(before, await File.ReadAllBytesAsync(paths.DatabasePath));
    }

    [Fact]
    public async Task CorruptedIndexBlocksWritesWithoutDeletingOrReplacingFile()
    {
        using var temporary = new TemporaryDirectory();
        var (index, paths) = CreateIndex(temporary);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.DatabasePath)!);
        var before = Encoding.UTF8.GetBytes("not a sqlite database");
        await File.WriteAllBytesAsync(paths.DatabasePath, before);
        var root = temporary.CreateDirectory("library");

        await index.InitializeAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            index.ReplaceLibraryAsync(root, [], Draft(0), CancellationToken.None));

        Assert.Equal(IndexCompatibilityState.Corrupted, index.Compatibility.State);
        Assert.False(index.Compatibility.CanWrite);
        Assert.True(File.Exists(paths.DatabasePath));
        Assert.Equal(before, await File.ReadAllBytesAsync(paths.DatabasePath));
    }

    [Fact]
    public async Task SuccessfulScanMetadataPersistsAndReloads()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("library");
        var (index, paths) = CreateIndex(temporary);
        var duplicate = new DuplicateAssetGroup(
            "duplicate",
            Path.Combine(root, "winner.json"),
            [Path.Combine(root, "copy-a.json"), Path.Combine(root, "copy-b.json")]);
        var draft = new ScanResult(
            0,
            0,
            0,
            1,
            2,
            3,
            [Path.Combine(root, "broken-a.json"), Path.Combine(root, "broken-b.json")],
            [Path.Combine(root, "protected")],
            [duplicate],
            TimeSpan.FromSeconds(4));

        await index.ReplaceLibraryAsync(
            root,
            [CreateAsset("persisted", root)],
            draft,
            CancellationToken.None);
        var reopened = new SqliteAssetIndex(paths, NullLogger<SqliteAssetIndex>.Instance);
        var diagnostics = await reopened.GetDiagnosticsAsync(CancellationToken.None);

        var scan = Assert.IsType<PersistedScanMetadata>(diagnostics.LastSuccessfulScan);
        Assert.Equal(ScanAttemptStatus.Succeeded, scan.LastScanStatus);
        Assert.Equal(1, scan.AddedCount);
        Assert.Equal(0, scan.UpdatedCount);
        Assert.Equal(0, scan.RemovedCount);
        Assert.Equal(7, scan.SkippedCount);
        Assert.Equal(1, scan.InaccessibleFolderCount);
        Assert.True(scan.LastScanDuration >= TimeSpan.FromSeconds(4));
        Assert.Equal(1, diagnostics.IndexedAssetCount);
    }

    [Fact]
    public async Task LegacyScanResultJsonRemainsReadable()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("library");
        var (index, paths) = CreateIndex(temporary);
        await index.ReplaceLibraryAsync(
            root,
            [CreateAsset("legacy-state", root)],
            Draft(1),
            CancellationToken.None);
        var legacy = new ScanResult(3, 2, 1, 4, 1, 2, [], [], [], TimeSpan.FromSeconds(9));
        await ExecuteAsync(
            paths.DatabasePath,
            $"UPDATE scan_state SET result_json = '{JsonSerializer.Serialize(legacy).Replace("'", "''", StringComparison.Ordinal)}';");
        var reopened = new SqliteAssetIndex(paths, NullLogger<SqliteAssetIndex>.Instance);

        var diagnostics = await reopened.GetDiagnosticsAsync(CancellationToken.None);

        var scan = Assert.IsType<PersistedScanMetadata>(diagnostics.LastSuccessfulScan);
        Assert.Equal(3, scan.AddedCount);
        Assert.Equal(2, scan.UpdatedCount);
        Assert.Equal(1, scan.RemovedCount);
        Assert.Equal(3, scan.SkippedCount);
        Assert.Equal(TimeSpan.FromSeconds(9), scan.LastScanDuration);
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

    private static AssetSummary CreateAsset(string id, string root) => new(
        id,
        $"Asset {id}",
        "Surface",
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
        DateTimeOffset.UnixEpoch);

    private static ScanResult Draft(int count) =>
        new(0, 0, 0, count, 0, 0, [], [], [], TimeSpan.Zero);

    private static async Task ExecuteAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateVersionOneDatabaseAsync(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
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
                categories_json, tags_json, last_write_time_utc)
            VALUES (
                'legacy', 'C:\legacy', 'Legacy', 'surface', 'C:\legacy',
                'C:\legacy\legacy.json', '[]', '[]', '1970-01-01T00:00:00.0000000+00:00');
            """;
        await command.ExecuteNonQueryAsync();
    }
}
