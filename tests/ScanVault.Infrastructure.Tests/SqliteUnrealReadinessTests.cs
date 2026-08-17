using System.Collections;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Core.Policies;
using ScanVault.Infrastructure.Configuration;
using ScanVault.Infrastructure.Persistence;

namespace ScanVault.Infrastructure.Tests;

public sealed class SqliteUnrealReadinessTests
{
    // Regression test: protects against stale current-version readiness being persisted after inventory changes.
    [Fact]
    public async Task ReplacementRecomputesCurrentVersionReadinessFromChangedInventory()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("library");
        var (index, _) = CreateIndex(temporary);
        var staleReady = ReadyAsset("asset", root).UnrealReadiness;
        var changedInventory = NotReadyAsset("asset", root) with { UnrealReadiness = staleReady };

        await index.ReplaceLibraryAsync(root, [changedInventory], Draft(1), CancellationToken.None);

        var saved = Assert.Single(await index.GetAssetsAsync(CancellationToken.None));
        Assert.Equal(UnrealReadinessStatus.NotReady, saved.UnrealReadiness.Status);
        Assert.Contains(saved.UnrealReadiness.Reasons, static reason => reason.RuleCode == UnrealReadinessRuleCode.UeMissingMesh);
    }

    // Regression test: protects MLV-12 duplicate-analysis source snapshots from stale readiness.
    [Fact]
    public async Task ReplacementRecomputesDuplicateAnalysisSourcesFromChangedInventory()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("library");
        var (index, _) = CreateIndex(temporary);
        var staleReady = ReadyAsset("source", root).UnrealReadiness;
        var winner = ReadyAsset("winner", root);
        var source = NotReadyAsset("source", root) with { UnrealReadiness = staleReady };

        await index.ReplaceLibraryAsync(
            root,
            [winner],
            Draft(1) with { DuplicateAnalysisSources = [source] },
            CancellationToken.None);

        var saved = Assert.Single(await index.GetDuplicateAnalysisSourcesAsync(root, CancellationToken.None));
        Assert.Equal(UnrealReadinessStatus.NotReady, saved.UnrealReadiness.Status);
        Assert.Contains(saved.UnrealReadiness.Reasons, static reason => reason.RuleCode == UnrealReadinessRuleCode.UeMissingMesh);
    }

    // Migration test: verifies SQLite v6 to v7 preserves rows as stale Unknown readiness.
    [Fact]
    public async Task VersionSixMigrationPreservesRowsAsStaleUnknownAndRemainsWritable()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("library");
        var sourceFile = temporary.WriteFile("library/source.asset", "do not touch");
        var beforeWrite = File.GetLastWriteTimeUtc(sourceFile);
        var beforeText = await File.ReadAllTextAsync(sourceFile);
        var (index, paths) = CreateIndex(temporary);
        await index.ReplaceLibraryAsync(root, [ReadyAsset("legacy", root)], Draft(1), CancellationToken.None);
        await DowngradeCurrentDatabaseToVersionSixAsync(paths.DatabasePath);

        var reopened = new SqliteAssetIndex(paths, NullLogger<SqliteAssetIndex>.Instance);
        await reopened.InitializeAsync(CancellationToken.None);
        var asset = Assert.Single(await reopened.GetAssetsAsync(CancellationToken.None));
        var diagnostics = await reopened.GetDiagnosticsAsync(CancellationToken.None);

        Assert.Equal(SqliteAssetIndex.CurrentSchemaVersion, reopened.Compatibility.DatabaseSchemaVersion);
        Assert.Equal("legacy", asset.Id);
        Assert.Equal("Asset legacy", asset.Name);
        Assert.Equal(AssetCompletenessStatus.Complete, asset.Content.Completeness);
        Assert.Equal(UnrealReadinessStatus.Unknown, asset.UnrealReadiness.Status);
        Assert.Equal(0, asset.UnrealReadiness.ReadinessRuleVersion);
        Assert.Null(asset.UnrealReadiness.EvaluatedAtUtc);
        Assert.Equal(1, diagnostics.UnrealReadiness!.RequiresRecalculationCount);
        Assert.True(await ColumnExistsAsync(paths.DatabasePath, "assets", "readiness_json"));
        Assert.Equal(beforeWrite, File.GetLastWriteTimeUtc(sourceFile));
        Assert.Equal(beforeText, await File.ReadAllTextAsync(sourceFile));

        await reopened.ReplaceLibraryAsync(root, [ReadyAsset("current", root)], Draft(1), CancellationToken.None);
        Assert.Equal("current", Assert.Single(await reopened.GetAssetsAsync(CancellationToken.None)).Id);
    }

    // Persistence test: verifies readiness survives reopening the SQLite index.
    [Fact]
    public async Task SuccessfulReplacementPersistsReadinessAcrossRestart()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("library");
        var (index, paths) = CreateIndex(temporary);

        await index.ReplaceLibraryAsync(root, [ReadyWithWarningsAsset("warn", root)], Draft(1), CancellationToken.None);
        var reopened = new SqliteAssetIndex(paths, NullLogger<SqliteAssetIndex>.Instance);
        var saved = Assert.Single(await reopened.GetAssetsAsync(CancellationToken.None));

        Assert.Equal(UnrealReadinessStatus.ReadyWithWarnings, saved.UnrealReadiness.Status);
        Assert.Equal(UnrealReadinessPolicy.CurrentRuleVersion, saved.UnrealReadiness.ReadinessRuleVersion);
        Assert.Equal(0, saved.UnrealReadiness.BlockingCount);
        Assert.True(saved.UnrealReadiness.WarningCount > 0);
        Assert.Contains(saved.UnrealReadiness.Reasons, static reason => reason.Code == "UE_MISSING_DISPLACEMENT");
        Assert.NotNull(saved.UnrealReadiness.EvaluatedAtUtc);
    }

    // Persistence test: verifies a failed replacement rolls back readiness changes.
    [Fact]
    public async Task FailedReplacementPreservesPreviousReadiness()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("library");
        var (index, paths) = CreateIndex(temporary);
        await index.ReplaceLibraryAsync(root, [ReadyAsset("previous", root)], Draft(1), CancellationToken.None);
        var duplicateJson = Path.Combine(root, "duplicate.json");
        var first = NotReadyAsset("first", root) with { JsonPath = duplicateJson };
        var second = ReadyWithWarningsAsset("second", root) with { JsonPath = duplicateJson };

        await Assert.ThrowsAsync<SqliteException>(() =>
            index.ReplaceLibraryAsync(root, [first, second], Draft(2), CancellationToken.None));

        var reopened = new SqliteAssetIndex(paths, NullLogger<SqliteAssetIndex>.Instance);
        var saved = Assert.Single(await reopened.GetAssetsAsync(CancellationToken.None));
        Assert.Equal("previous", saved.Id);
        Assert.Equal(UnrealReadinessStatus.Ready, saved.UnrealReadiness.Status);
    }

    // Persistence test: verifies a cancelled replacement rolls back readiness changes.
    [Fact]
    public async Task CancelledReplacementPreservesPreviousReadiness()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("library");
        var (index, paths) = CreateIndex(temporary);
        await index.ReplaceLibraryAsync(root, [ReadyAsset("previous", root)], Draft(1), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var replacement = new CancelOnSecondItemList(
            [NotReadyAsset("new-a", root), ReadyWithWarningsAsset("new-b", root)],
            cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            index.ReplaceLibraryAsync(root, replacement, Draft(2), cancellation.Token));

        var reopened = new SqliteAssetIndex(paths, NullLogger<SqliteAssetIndex>.Instance);
        var saved = Assert.Single(await reopened.GetAssetsAsync(CancellationToken.None));
        Assert.Equal("previous", saved.Id);
        Assert.Equal(UnrealReadinessStatus.Ready, saved.UnrealReadiness.Status);
    }

    // Diagnostics test: verifies stale rule-version readiness is reported and refreshed.
    [Fact]
    public async Task StaleRuleVersionIsReportedAndSuccessfulReplacementRefreshesIt()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("library");
        var (index, paths) = CreateIndex(temporary);
        await index.ReplaceLibraryAsync(root, [ReadyAsset("asset", root)], Draft(1), CancellationToken.None);
        await ExecuteAsync(paths.DatabasePath, """
            UPDATE assets
            SET readiness_rule_version = 0,
                readiness_evaluated_at_utc = NULL,
                readiness_json = '{"Status":0,"ReadinessRuleVersion":0,"Reasons":[],"EvaluatedAtUtc":null}';
            """);
        var reopened = new SqliteAssetIndex(paths, NullLogger<SqliteAssetIndex>.Instance);

        var stale = Assert.Single(await reopened.GetAssetsAsync(CancellationToken.None));
        var diagnostics = await reopened.GetDiagnosticsAsync(CancellationToken.None);
        Assert.False(UnrealReadinessPolicy.IsCurrent(stale.UnrealReadiness));
        Assert.Equal(1, diagnostics.UnrealReadiness!.RequiresRecalculationCount);

        await reopened.ReplaceLibraryAsync(root, [ReadyAsset("asset", root)], Draft(1), CancellationToken.None);

        var refreshed = Assert.Single(await reopened.GetAssetsAsync(CancellationToken.None));
        Assert.True(UnrealReadinessPolicy.IsCurrent(refreshed.UnrealReadiness));
        Assert.Equal(UnrealReadinessPolicy.CurrentRuleVersion, refreshed.UnrealReadiness.ReadinessRuleVersion);
    }

    // History test: verifies readiness changes are recorded without timestamp-only noise.
    [Fact]
    public async Task ReadinessHistoryChangesAreRecordedButEvaluationTimestampIsStable()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("library");
        var (index, _) = CreateIndex(temporary);
        var firstRun = await index.BeginScanRunAsync(root, "test", "abcdef1", CancellationToken.None);
        await index.ReplaceLibraryAsync(root, [ReadyAsset("asset", root)], Draft(1), firstRun, CancellationToken.None);
        var secondRun = await index.BeginScanRunAsync(root, "test", "abcdef1", CancellationToken.None);
        await index.ReplaceLibraryAsync(root, [NotReadyAsset("asset", root)], Draft(1), secondRun, CancellationToken.None);
        var thirdRun = await index.BeginScanRunAsync(root, "test", "abcdef1", CancellationToken.None);
        var third = await index.ReplaceLibraryAsync(root, [NotReadyAsset("asset", root)], Draft(1), thirdRun, CancellationToken.None);

        var changed = Assert.Single(await index.GetScanChangesAsync(secondRun, AssetChangeKind.Changed, 10, CancellationToken.None));
        Assert.True(changed.Flags.HasFlag(AssetChangeReason.Readiness));
        Assert.Equal(1, third.UnchangedAssets);
        Assert.Empty(await index.GetScanChangesAsync(thirdRun, AssetChangeKind.Changed, 10, CancellationToken.None));
    }

    // Integration test: verifies deterministic mixed-status batch persistence and diagnostics counts.
    [Fact]
    public async Task DeterministicBatchPersistsMixedReadinessCounts()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("library");
        var (assetIndex, _) = CreateIndex(temporary);
        var assets = Enumerable.Range(0, 160)
            .Select(item => (item % 4) switch
            {
                0 => ReadyAsset($"ready-{item}", root),
                1 => ReadyWithWarningsAsset($"warning-{item}", root),
                2 => NotReadyAsset($"not-ready-{item}", root),
                _ => UnknownAsset($"unknown-{item}", root)
            })
            .ToArray();

        await assetIndex.ReplaceLibraryAsync(root, assets, Draft(assets.Length), CancellationToken.None);
        var saved = await assetIndex.GetAssetsAsync(CancellationToken.None);
        var diagnostics = await assetIndex.GetDiagnosticsAsync(CancellationToken.None);

        Assert.Equal(160, saved.Count);
        Assert.Equal(40, diagnostics.UnrealReadiness!.ReadyCount);
        Assert.Equal(40, diagnostics.UnrealReadiness.ReadyWithWarningsCount);
        Assert.Equal(40, diagnostics.UnrealReadiness.NotReadyCount);
        Assert.Equal(40, diagnostics.UnrealReadiness.UnknownCount);
        Assert.Equal(0, diagnostics.UnrealReadiness.RequiresRecalculationCount);
    }

    private static (SqliteAssetIndex Index, ScanVaultPaths Paths) CreateIndex(TemporaryDirectory temporary)
    {
        var paths = new ScanVaultPaths(
            Path.Combine(temporary.Path, "data", "scanvault.db"),
            Path.Combine(temporary.Path, "settings", "settings.json"),
            Path.Combine(temporary.Path, "cache"));
        return (new(paths, NullLogger<SqliteAssetIndex>.Instance), paths);
    }

    private static AssetSummary ReadyAsset(string id, string root) => Asset(id, root, "Surface",
        "asset_4K_Albedo.jpg",
        "asset_4K_Normal.jpg",
        "asset_4K_Roughness.jpg",
        "asset_4K_Displacement.exr");

    private static AssetSummary ReadyWithWarningsAsset(string id, string root) => Asset(id, root, "Surface",
        "asset_4K_Albedo.jpg",
        "asset_4K_Normal.jpg",
        "asset_4K_Roughness.jpg");

    private static AssetSummary NotReadyAsset(string id, string root) => Asset(id, root, "3D Asset",
        "asset_4K_Albedo.jpg",
        "asset_4K_Normal.jpg",
        "asset_4K_Roughness.jpg");

    private static AssetSummary UnknownAsset(string id, string root) => Asset(id, root, "Mystery",
        "asset_4K_Albedo.jpg",
        "asset_4K_Normal.jpg");

    private static AssetSummary Asset(string id, string root, string assetType, params string[] relativePaths)
    {
        var folder = Path.Combine(root, id);
        var files = relativePaths.Select(path => new AssetContentFileCandidate(Path.Combine(folder, path), path)).ToArray();
        var asset = new AssetSummary(
            id,
            $"Asset {id}",
            assetType,
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
            Content = AssetContentAnalyzer.Analyze(assetType, files)
        };
        return asset with { UnrealReadiness = UnrealReadinessPolicy.Evaluate(asset, DateTimeOffset.UnixEpoch) };
    }

    private static ScanResult Draft(int count) =>
        new(0, 0, 0, count, 0, 0, [], [], [], TimeSpan.Zero);

    private static async Task DowngradeCurrentDatabaseToVersionSixAsync(string databasePath)
    {
        await ExecuteAsync(databasePath, """
            PRAGMA foreign_keys = OFF;
            DROP INDEX IF EXISTS ix_assets_readiness_status;
            DROP INDEX IF EXISTS ix_assets_readiness_rule_version;
            CREATE TABLE assets_v6 (
                id TEXT PRIMARY KEY COLLATE NOCASE,
                library_root TEXT NOT NULL,
                name TEXT NOT NULL,
                asset_type TEXT NOT NULL,
                raw_asset_type TEXT NULL,
                asset_folder_path TEXT NOT NULL,
                json_path TEXT NOT NULL UNIQUE,
                thumbnail_path TEXT NULL,
                preview_path TEXT NULL,
                biome TEXT NULL,
                region TEXT NULL,
                physical_size TEXT NULL,
                max_resolution INTEGER NULL,
                resolution_width INTEGER NULL,
                resolution_height INTEGER NULL,
                texel_density REAL NULL,
                average_color TEXT NULL,
                categories_json TEXT NOT NULL,
                tags_json TEXT NOT NULL,
                last_write_time_utc TEXT NOT NULL,
                inventory_json TEXT NOT NULL,
                completeness INTEGER NOT NULL,
                has_fbx INTEGER NOT NULL,
                has_lods INTEGER NOT NULL,
                has_billboard INTEGER NOT NULL,
                has_atlas INTEGER NOT NULL,
                variant_count INTEGER NOT NULL,
                lod_count INTEGER NOT NULL,
                texture_set_count INTEGER NOT NULL
            );
            INSERT INTO assets_v6(
                id, library_root, name, asset_type, raw_asset_type, asset_folder_path,
                json_path, thumbnail_path, preview_path, biome, region, physical_size,
                max_resolution, resolution_width, resolution_height, texel_density,
                average_color, categories_json, tags_json, last_write_time_utc,
                inventory_json, completeness, has_fbx, has_lods, has_billboard,
                has_atlas, variant_count, lod_count, texture_set_count)
            SELECT id, library_root, name, asset_type, raw_asset_type, asset_folder_path,
                json_path, thumbnail_path, preview_path, biome, region, physical_size,
                max_resolution, resolution_width, resolution_height, texel_density,
                average_color, categories_json, tags_json, last_write_time_utc,
                inventory_json, completeness, has_fbx, has_lods, has_billboard,
                has_atlas, variant_count, lod_count, texture_set_count
            FROM assets;
            DROP TABLE assets;
            ALTER TABLE assets_v6 RENAME TO assets;
            UPDATE schema_info SET version = 6;
            PRAGMA foreign_keys = ON;
            """);
    }

    private static async Task<bool> ColumnExistsAsync(string databasePath, string table, string column)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task ExecuteAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
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
