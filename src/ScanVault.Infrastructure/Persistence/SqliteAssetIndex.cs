using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Core.Policies;
using ScanVault.Infrastructure.Configuration;

namespace ScanVault.Infrastructure.Persistence;

public sealed partial class SqliteAssetIndex(
    ScanVaultPaths paths,
    ILogger<SqliteAssetIndex> logger) : IAssetIndex
{
    public const int CurrentSchemaVersion = 7;
    public const int CurrentNormalizationVersion = 3;
    private static readonly bool ProviderInitialized = InitializeProvider();
    private readonly ScanVaultPaths resolvedPaths = paths;

    private readonly string connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = paths.DatabasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
        ForeignKeys = true,
        Pooling = false
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Compatibility = await InspectCompatibilityAsync(cancellationToken).ConfigureAwait(false);
        if (Compatibility.State == IndexCompatibilityState.RequiresMigration)
        {
            try
            {
                await MigrateKnownSchemaAsync(cancellationToken).ConfigureAwait(false);
                Compatibility = await InspectCompatibilityAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
            {
                InfrastructureLog.IndexCorrupted(logger, resolvedPaths.DatabasePath, exception);
                Compatibility = new(
                    IndexCompatibilityState.Corrupted,
                    null,
                    null,
                    IsReadable: false,
                    CanWrite: false,
                    RequiresRescan: false,
                    "Index migration failed. The database file was preserved. Back it up before attempting manual recovery.");
            }
        }

        InfrastructureLog.IndexCompatibilityEvaluated(
            logger,
            resolvedPaths.DatabasePath,
            Compatibility.State,
            Compatibility.DatabaseSchemaVersion,
            Compatibility.MetadataNormalizationVersion,
            Compatibility.RequiresRescan);
        if (!Compatibility.CanWrite &&
            Compatibility.State is IndexCompatibilityState.NewerVersionUnsupported or
                IndexCompatibilityState.Corrupted)
        {
            InfrastructureLog.IndexWritesBlocked(
                logger,
                resolvedPaths.DatabasePath,
                Compatibility.State,
                Compatibility.Guidance);
        }
    }

    public async Task<IReadOnlyList<AssetSummary>> GetAssetsAsync(
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!Compatibility.IsReadable)
        {
            return [];
        }

        await using var connection = await OpenReadOnlyAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, asset_type, raw_asset_type, asset_folder_path, json_path,
                   thumbnail_path, preview_path, biome, region, physical_size,
                   max_resolution, resolution_width, resolution_height, texel_density,
                   average_color, categories_json, tags_json, last_write_time_utc,
                   inventory_json, readiness_json
            FROM assets
            ORDER BY name COLLATE NOCASE, id COLLATE NOCASE;
            """;

        var assets = new List<AssetSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var categories = JsonSerializer.Deserialize<string[]>(reader.GetString(16)) ?? [];
            var tags = JsonSerializer.Deserialize<AssetTag[]>(reader.GetString(17)) ?? [];
            ImageResolution? resolution = null;
            if (!reader.IsDBNull(12) && !reader.IsDBNull(13))
            {
                resolution = new(reader.GetInt32(12), reader.GetInt32(13));
            }
            else if (!reader.IsDBNull(11) && reader.GetInt32(11) > 0)
            {
                var legacy = reader.GetInt32(11);
                resolution = new(legacy, legacy);
            }

            assets.Add(new AssetSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                resolution,
                reader.IsDBNull(14) ? null : reader.GetDouble(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                categories,
                tags,
                DateTimeOffset.Parse(
                    reader.GetString(18),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind))
            {
                RawAssetType = reader.IsDBNull(3) ? null : reader.GetString(3),
                Content = JsonSerializer.Deserialize<AssetContentInventory>(reader.GetString(19))
                    ?? AssetContentInventory.Empty,
                UnrealReadiness = ReadReadiness(reader.GetString(20))
            });
        }

        return assets;
    }

    public async Task<IndexUpdateResult> ReplaceLibraryAsync(
        string libraryRoot,
        IReadOnlyList<AssetSummary> assets,
        ScanResult draftResult,
        CancellationToken cancellationToken)
    {
        var scanRunId = await BeginScanRunAsync(libraryRoot, "Test", "test-scan-history", cancellationToken)
            .ConfigureAwait(false);
        var result = await ReplaceLibraryAsync(libraryRoot, assets, draftResult, scanRunId, cancellationToken)
            .ConfigureAwait(false);
        return result with { ChangedAssets = 0, UnchangedAssets = 0, IsInitialBaseline = false, ScanRunId = null };
    }
    public async Task<IndexUpdateResult> ReplaceLibraryAsync(
        string libraryRoot,
        IReadOnlyList<AssetSummary> assets,
        ScanResult draftResult,
        string scanRunId,
        CancellationToken cancellationToken)
    {
        var persistenceStopwatch = Stopwatch.StartNew();
        await PrepareWritableDatabaseAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureWritableConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var readinessStopwatch = Stopwatch.StartNew();
            var evaluatedAtUtc = DateTimeOffset.UtcNow;
            var assetsToPersist = assets
                .Select(asset =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return asset with { UnrealReadiness = UnrealReadinessPolicy.Evaluate(asset, evaluatedAtUtc) };
                })
                .ToArray();
            readinessStopwatch.Stop();
            var readinessSummary = UnrealReadinessPolicy.Summarize(assetsToPersist);
            InfrastructureLog.UnrealReadinessEvaluated(
                logger,
                assetsToPersist.Length,
                readinessStopwatch.Elapsed,
                readinessStopwatch.Elapsed.TotalSeconds <= 0 ? assetsToPersist.Length : assetsToPersist.Length / readinessStopwatch.Elapsed.TotalSeconds,
                readinessSummary.ReadyCount,
                readinessSummary.ReadyWithWarningsCount,
                readinessSummary.NotReadyCount,
                readinessSummary.UnknownCount);

            var existing = await LoadExistingAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);
            var history = await BuildHistoryAsync(connection, transaction, libraryRoot, assetsToPersist, scanRunId, cancellationToken)
                .ConfigureAwait(false);
            var added = 0;
            var updated = 0;

            await ExecuteAsync(
                connection,
                transaction,
                "CREATE TEMP TABLE current_scan_ids(id TEXT PRIMARY KEY COLLATE NOCASE) WITHOUT ROWID;",
                cancellationToken).ConfigureAwait(false);

            foreach (var asset in assetsToPersist)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!existing.TryGetValue(asset.Id, out var previous))
                {
                    added++;
                }
                else if (!previous.Equals(new(asset.JsonPath, asset.LastWriteTimeUtc)))
                {
                    updated++;
                }

                await UpsertAssetAsync(connection, transaction, libraryRoot, asset, cancellationToken)
                    .ConfigureAwait(false);
                await ReplaceTagsAsync(connection, transaction, asset, cancellationToken)
                    .ConfigureAwait(false);
                await ReplaceInventoryMapIndexAsync(connection, transaction, asset, cancellationToken)
                    .ConfigureAwait(false);

                await using var currentCommand = connection.CreateCommand();
                currentCommand.Transaction = transaction;
                currentCommand.CommandText = "INSERT INTO current_scan_ids(id) VALUES ($id);";
                currentCommand.Parameters.AddWithValue("$id", asset.Id);
                await currentCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var removed = Convert.ToInt32(
                await ScalarAsync(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM assets WHERE NOT EXISTS (SELECT 1 FROM current_scan_ids WHERE current_scan_ids.id = assets.id);",
                    cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);

            await ExecuteAsync(
                connection,
                transaction,
                "DELETE FROM assets WHERE NOT EXISTS (SELECT 1 FROM current_scan_ids WHERE current_scan_ids.id = assets.id);",
                cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(
                connection,
                transaction,
                "DELETE FROM tags WHERE NOT EXISTS (SELECT 1 FROM asset_tags WHERE asset_tags.tag_id = tags.tag_id);",
                cancellationToken).ConfigureAwait(false);

            var completedAtUtc = DateTimeOffset.UtcNow;
            var persistedScan = new PersistedScanMetadata(
                completedAtUtc,
                draftResult.Elapsed + persistenceStopwatch.Elapsed,
                ScanAttemptStatus.Succeeded,
                added,
                updated,
                removed,
                draftResult.SkippedMalformedFiles +
                draftResult.SkippedUnrelatedFiles +
                draftResult.DuplicateGroups.Sum(group => group.SkippedCopyJsonPaths.Count),
                draftResult.InaccessibleDirectories.Count);
            await PersistCompletedHistoryAsync(connection, transaction, scanRunId, history, draftResult, completedAtUtc, cancellationToken)
                .ConfigureAwait(false);
            await ReplaceDuplicateAnalysisSourcesAsync(
                    connection,
                    transaction,
                    libraryRoot,
                    draftResult.DuplicateAnalysisSources.Count == 0
                        ? assetsToPersist
                        : draftResult.DuplicateAnalysisSources.Select(asset =>
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            return asset with { UnrealReadiness = UnrealReadinessPolicy.Evaluate(asset, evaluatedAtUtc) };
                        }).ToArray(),
                    assetsToPersist,
                    scanRunId,
                    cancellationToken)
                .ConfigureAwait(false);
            await MarkDuplicateAnalysesStaleAsync(connection, transaction, libraryRoot, cancellationToken).ConfigureAwait(false);

            await using var scanState = connection.CreateCommand();
            scanState.Transaction = transaction;
            scanState.CommandText = """
                INSERT INTO scan_state(singleton_id, library_root, completed_at_utc, result_json)
                VALUES (1, $root, $completed, $result)
                ON CONFLICT(singleton_id) DO UPDATE SET
                    library_root = excluded.library_root,
                    completed_at_utc = excluded.completed_at_utc,
                    result_json = excluded.result_json;
                """;
            scanState.Parameters.AddWithValue("$root", libraryRoot);
            scanState.Parameters.AddWithValue(
                "$completed",
                persistedScan.LastSuccessfulScanUtc.ToString(
                    "O",
                    CultureInfo.InvariantCulture));
            scanState.Parameters.AddWithValue(
                "$result",
                JsonSerializer.Serialize(persistedScan));
            await scanState.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            // The new parser becomes authoritative only in the same transaction that
            // replaces every asset. Cancellation therefore leaves the old marker intact.
            await ExecuteAsync(
                connection,
                transaction,
                $"UPDATE schema_info SET normalization_version = {CurrentNormalizationVersion};",
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            Compatibility = CompatibleCompatibility();
            return new(added, updated, removed, history.Changed, history.Unchanged, history.IsInitialBaseline, scanRunId);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception rollbackException)
            {
                InfrastructureLog.RollbackFailed(logger, rollbackException);
            }

            throw;
        }
    }

    private static async Task CreateSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            """
            CREATE TABLE schema_info (
                version INTEGER NOT NULL,
                normalization_version INTEGER NOT NULL
            );
            INSERT INTO schema_info(version, normalization_version) VALUES (7, 3);

            CREATE TABLE assets (
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
                readiness_json TEXT NOT NULL,
                readiness_status INTEGER NOT NULL,
                readiness_rule_version INTEGER NOT NULL,
                readiness_blocking_count INTEGER NOT NULL,
                readiness_warning_count INTEGER NOT NULL,
                readiness_evaluated_at_utc TEXT NULL,
                completeness INTEGER NOT NULL,
                has_fbx INTEGER NOT NULL,
                has_lods INTEGER NOT NULL,
                has_billboard INTEGER NOT NULL,
                has_atlas INTEGER NOT NULL,
                variant_count INTEGER NOT NULL,
                lod_count INTEGER NOT NULL,
                texture_set_count INTEGER NOT NULL
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
                PRIMARY KEY(asset_id, tag_id),
                FOREIGN KEY(asset_id) REFERENCES assets(id) ON DELETE CASCADE,
                FOREIGN KEY(tag_id) REFERENCES tags(tag_id) ON DELETE CASCADE
            );

            CREATE TABLE asset_inventory_maps (
                asset_id TEXT NOT NULL COLLATE NOCASE,
                map_type INTEGER NOT NULL,
                set_kind INTEGER NOT NULL,
                resolution INTEGER NULL,
                format TEXT NOT NULL,
                path TEXT NOT NULL,
                PRIMARY KEY(asset_id, path),
                FOREIGN KEY(asset_id) REFERENCES assets(id) ON DELETE CASCADE
            );

            CREATE TABLE scan_state (
                singleton_id INTEGER PRIMARY KEY CHECK(singleton_id = 1),
                library_root TEXT NOT NULL,
                completed_at_utc TEXT NOT NULL,
                result_json TEXT NOT NULL
            );

            CREATE INDEX ix_assets_library_root ON assets(library_root);
            CREATE INDEX ix_assets_folder ON assets(asset_folder_path);
            CREATE INDEX ix_assets_name ON assets(name COLLATE NOCASE);
            CREATE INDEX ix_assets_type ON assets(asset_type COLLATE NOCASE);
            CREATE INDEX ix_assets_biome ON assets(biome COLLATE NOCASE);
            CREATE INDEX ix_assets_region ON assets(region COLLATE NOCASE);
            CREATE INDEX ix_assets_completeness ON assets(completeness);
            CREATE INDEX ix_assets_readiness_status ON assets(readiness_status);
            CREATE INDEX ix_assets_readiness_rule_version ON assets(readiness_rule_version);
            CREATE INDEX ix_assets_has_fbx ON assets(has_fbx);
            CREATE INDEX ix_assets_has_lods ON assets(has_lods);
            CREATE INDEX ix_assets_has_billboard ON assets(has_billboard);
            CREATE INDEX ix_assets_has_atlas ON assets(has_atlas);
            CREATE INDEX ix_assets_variant_count ON assets(variant_count);
            CREATE INDEX ix_asset_inventory_maps_type ON asset_inventory_maps(map_type, asset_id);
            CREATE INDEX ix_asset_tags_tag ON asset_tags(tag_id);
            """,
            cancellationToken).ConfigureAwait(false);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await CreateHistoryTablesAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            await CreateDuplicateAnalysisTablesAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task MigrateVersionOneAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                ALTER TABLE schema_info
                    ADD COLUMN normalization_version INTEGER NOT NULL DEFAULT 1;
                ALTER TABLE assets ADD COLUMN raw_asset_type TEXT NULL;
                ALTER TABLE assets ADD COLUMN resolution_width INTEGER NULL;
                ALTER TABLE assets ADD COLUMN resolution_height INTEGER NULL;
                UPDATE assets
                SET resolution_width = max_resolution,
                    resolution_height = max_resolution
                WHERE max_resolution IS NOT NULL AND max_resolution > 0;
                UPDATE schema_info
                SET version = 2,
                    normalization_version = CASE
                        WHEN EXISTS (SELECT 1 FROM assets) THEN 1
                        ELSE 2
                    END;
                """,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task MigrateVersionTwoAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                ALTER TABLE assets ADD COLUMN inventory_json TEXT NOT NULL
                    DEFAULT '{"Variants":[],"TextureSets":[],"UnclassifiedFiles":[],"Completeness":5,"Issues":[]}';
                ALTER TABLE assets ADD COLUMN completeness INTEGER NOT NULL DEFAULT 5;
                ALTER TABLE assets ADD COLUMN has_fbx INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE assets ADD COLUMN has_lods INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE assets ADD COLUMN has_billboard INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE assets ADD COLUMN has_atlas INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE assets ADD COLUMN variant_count INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE assets ADD COLUMN lod_count INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE assets ADD COLUMN texture_set_count INTEGER NOT NULL DEFAULT 0;

                CREATE TABLE asset_inventory_maps (
                    asset_id TEXT NOT NULL COLLATE NOCASE,
                    map_type INTEGER NOT NULL,
                    set_kind INTEGER NOT NULL,
                    resolution INTEGER NULL,
                    format TEXT NOT NULL,
                    path TEXT NOT NULL,
                    PRIMARY KEY(asset_id, path),
                    FOREIGN KEY(asset_id) REFERENCES assets(id) ON DELETE CASCADE
                );
                CREATE INDEX ix_assets_completeness ON assets(completeness);
                CREATE INDEX ix_assets_has_fbx ON assets(has_fbx);
                CREATE INDEX ix_assets_has_lods ON assets(has_lods);
                CREATE INDEX ix_assets_has_billboard ON assets(has_billboard);
                CREATE INDEX ix_assets_has_atlas ON assets(has_atlas);
                CREATE INDEX ix_assets_variant_count ON assets(variant_count);
                CREATE INDEX ix_asset_inventory_maps_type ON asset_inventory_maps(map_type, asset_id);
                UPDATE schema_info
                SET version = 3,
                    normalization_version = CASE
                        WHEN EXISTS (SELECT 1 FROM assets) THEN MIN(normalization_version, 2)
                        ELSE 3
                    END;
                """,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task<Dictionary<string, AssetIdentity>> LoadExistingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id, json_path, last_write_time_utc FROM assets;";
        var result = new Dictionary<string, AssetIdentity>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result[reader.GetString(0)] = new(
                reader.GetString(1),
                DateTimeOffset.Parse(
                    reader.GetString(2),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind));
        }

        return result;
    }

    private static async Task UpsertAssetAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string libraryRoot,
        AssetSummary asset,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO assets(
                id, library_root, name, asset_type, raw_asset_type,
                asset_folder_path, json_path, thumbnail_path, preview_path,
                biome, region, physical_size, max_resolution,
                resolution_width, resolution_height, texel_density, average_color,
                categories_json, tags_json, last_write_time_utc, inventory_json,
                readiness_json, readiness_status, readiness_rule_version,
                readiness_blocking_count, readiness_warning_count, readiness_evaluated_at_utc,
                completeness, has_fbx, has_lods, has_billboard, has_atlas,
                variant_count, lod_count, texture_set_count)
            VALUES(
                $id, $root, $name, $type, $rawType, $folder, $json, $thumb, $preview,
                $biome, $region, $size, $resolution, $resolutionWidth,
                $resolutionHeight, $texel, $color, $categories, $tags, $lastWrite,
                $inventory, $readiness, $readinessStatus, $readinessVersion,
                $readinessBlocking, $readinessWarnings, $readinessEvaluated,
                $completeness, $hasFbx, $hasLods, $hasBillboard, $hasAtlas,
                $variantCount, $lodCount, $textureSetCount)
            ON CONFLICT(id) DO UPDATE SET
                library_root = excluded.library_root,
                name = excluded.name,
                asset_type = excluded.asset_type,
                raw_asset_type = excluded.raw_asset_type,
                asset_folder_path = excluded.asset_folder_path,
                json_path = excluded.json_path,
                thumbnail_path = excluded.thumbnail_path,
                preview_path = excluded.preview_path,
                biome = excluded.biome,
                region = excluded.region,
                physical_size = excluded.physical_size,
                max_resolution = excluded.max_resolution,
                resolution_width = excluded.resolution_width,
                resolution_height = excluded.resolution_height,
                texel_density = excluded.texel_density,
                average_color = excluded.average_color,
                categories_json = excluded.categories_json,
                tags_json = excluded.tags_json,
                last_write_time_utc = excluded.last_write_time_utc,
                inventory_json = excluded.inventory_json,
                readiness_json = excluded.readiness_json,
                readiness_status = excluded.readiness_status,
                readiness_rule_version = excluded.readiness_rule_version,
                readiness_blocking_count = excluded.readiness_blocking_count,
                readiness_warning_count = excluded.readiness_warning_count,
                readiness_evaluated_at_utc = excluded.readiness_evaluated_at_utc,
                completeness = excluded.completeness,
                has_fbx = excluded.has_fbx,
                has_lods = excluded.has_lods,
                has_billboard = excluded.has_billboard,
                has_atlas = excluded.has_atlas,
                variant_count = excluded.variant_count,
                lod_count = excluded.lod_count,
                texture_set_count = excluded.texture_set_count;
            """;
        Add(command, "$id", asset.Id);
        Add(command, "$root", libraryRoot);
        Add(command, "$name", asset.Name);
        Add(command, "$type", asset.AssetType);
        Add(command, "$rawType", asset.RawAssetType);
        Add(command, "$folder", asset.AssetFolderPath);
        Add(command, "$json", asset.JsonPath);
        Add(command, "$thumb", asset.ThumbnailPath);
        Add(command, "$preview", asset.PreviewPath);
        Add(command, "$biome", asset.Biome);
        Add(command, "$region", asset.Region);
        Add(command, "$size", asset.PhysicalSize);
        Add(command, "$resolution", asset.MaxResolution?.MaxDimension);
        Add(command, "$resolutionWidth", asset.MaxResolution?.Width);
        Add(command, "$resolutionHeight", asset.MaxResolution?.Height);
        Add(command, "$texel", asset.TexelDensity);
        Add(command, "$color", asset.AverageColor);
        Add(command, "$categories", JsonSerializer.Serialize(asset.Categories));
        Add(command, "$tags", JsonSerializer.Serialize(asset.Tags));
        Add(command, "$lastWrite", asset.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture));
        Add(command, "$inventory", JsonSerializer.Serialize(asset.Content));
        Add(command, "$readiness", JsonSerializer.Serialize(asset.UnrealReadiness));
        Add(command, "$readinessStatus", (int)asset.UnrealReadiness.Status);
        Add(command, "$readinessVersion", asset.UnrealReadiness.ReadinessRuleVersion);
        Add(command, "$readinessBlocking", asset.UnrealReadiness.BlockingCount);
        Add(command, "$readinessWarnings", asset.UnrealReadiness.WarningCount);
        Add(command, "$readinessEvaluated", asset.UnrealReadiness.EvaluatedAtUtc?.ToString("O", CultureInfo.InvariantCulture));
        Add(command, "$completeness", (int)asset.Content.Completeness);
        Add(command, "$hasFbx", asset.Content.HasFbx);
        Add(command, "$hasLods", asset.Content.HasLods);
        Add(command, "$hasBillboard", asset.Content.HasBillboard);
        Add(command, "$hasAtlas", asset.Content.HasAtlas);
        Add(command, "$variantCount", asset.Content.VariantCount);
        Add(command, "$lodCount", asset.Content.LodCount);
        Add(command, "$textureSetCount", asset.Content.TextureSetCount);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReplaceTagsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetSummary asset,
        CancellationToken cancellationToken)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM asset_tags WHERE asset_id = $assetId;";
            delete.Parameters.AddWithValue("$assetId", asset.Id);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var tag in asset.Tags)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO tags(kind, value) VALUES ($kind, $value);
                INSERT OR IGNORE INTO asset_tags(asset_id, tag_id)
                SELECT $assetId, tag_id FROM tags WHERE kind = $kind AND value = $value;
                """;
            command.Parameters.AddWithValue("$assetId", asset.Id);
            command.Parameters.AddWithValue("$kind", (int)tag.Kind);
            command.Parameters.AddWithValue("$value", tag.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ReplaceInventoryMapIndexAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetSummary asset,
        CancellationToken cancellationToken)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM asset_inventory_maps WHERE asset_id = $assetId;";
            delete.Parameters.AddWithValue("$assetId", asset.Id);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var set in asset.Content.TextureSets)
        {
            foreach (var component in set.Components)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO asset_inventory_maps(asset_id, map_type, set_kind, resolution, format, path)
                    VALUES ($assetId, $mapType, $setKind, $resolution, $format, $path);
                    """;
                Add(command, "$assetId", asset.Id);
                Add(command, "$mapType", (int)component.MapType);
                Add(command, "$setKind", (int)set.Kind);
                Add(command, "$resolution", component.Resolution);
                Add(command, "$format", component.Format);
                Add(command, "$path", component.Path);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static UnrealReadinessEvaluation ReadReadiness(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<UnrealReadinessEvaluation>(json) ?? UnrealReadinessEvaluation.Unknown;
        }
        catch (JsonException)
        {
            return UnrealReadinessEvaluation.Unknown;
        }
    }
    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(
                   await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                   CultureInfo.InvariantCulture) > 0;
    }

    private static async Task<int> ReadIntAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<object?> ScalarAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool InitializeProvider()
    {
        SQLitePCL.Batteries.Init();
        return true;
    }

    private sealed record AssetIdentity(string JsonPath, DateTimeOffset LastWriteTimeUtc);
}
