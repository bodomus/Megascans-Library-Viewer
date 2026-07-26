using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Infrastructure.Configuration;

namespace ScanVault.Infrastructure.Persistence;

public sealed class SqliteAssetIndex(
    ScanVaultPaths paths,
    ILogger<SqliteAssetIndex> logger) : IAssetIndex
{
    public const int CurrentSchemaVersion = 1;
    private static readonly bool ProviderInitialized = InitializeProvider();

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
        var directory = Path.GetDirectoryName(paths.DatabasePath)
            ?? throw new InvalidOperationException("Database path has no parent directory.");
        Directory.CreateDirectory(directory);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS schema_info (
                version INTEGER NOT NULL
            );
            INSERT INTO schema_info(version)
            SELECT 1
            WHERE NOT EXISTS (SELECT 1 FROM schema_info);

            CREATE TABLE IF NOT EXISTS assets (
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

            CREATE TABLE IF NOT EXISTS tags (
                tag_id INTEGER PRIMARY KEY AUTOINCREMENT,
                kind INTEGER NOT NULL,
                value TEXT NOT NULL COLLATE NOCASE,
                UNIQUE(kind, value)
            );

            CREATE TABLE IF NOT EXISTS asset_tags (
                asset_id TEXT NOT NULL COLLATE NOCASE,
                tag_id INTEGER NOT NULL,
                PRIMARY KEY(asset_id, tag_id),
                FOREIGN KEY(asset_id) REFERENCES assets(id) ON DELETE CASCADE,
                FOREIGN KEY(tag_id) REFERENCES tags(tag_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS scan_state (
                singleton_id INTEGER PRIMARY KEY CHECK(singleton_id = 1),
                library_root TEXT NOT NULL,
                completed_at_utc TEXT NOT NULL,
                result_json TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_assets_library_root ON assets(library_root);
            CREATE INDEX IF NOT EXISTS ix_assets_folder ON assets(asset_folder_path);
            CREATE INDEX IF NOT EXISTS ix_assets_name ON assets(name COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS ix_assets_type ON assets(asset_type COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS ix_assets_biome ON assets(biome COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS ix_assets_region ON assets(region COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS ix_asset_tags_tag ON asset_tags(tag_id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT version FROM schema_info LIMIT 1;";
        var version = Convert.ToInt32(
            await versionCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        if (version != CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported ScanVault schema version {version}; expected {CurrentSchemaVersion}.");
        }

        InfrastructureLog.IndexReady(logger, paths.DatabasePath, version);
    }

    public async Task<IReadOnlyList<AssetSummary>> GetAssetsAsync(
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, asset_type, asset_folder_path, json_path,
                   thumbnail_path, preview_path, biome, region, physical_size,
                   max_resolution, texel_density, average_color, categories_json,
                   tags_json, last_write_time_utc
            FROM assets
            ORDER BY name COLLATE NOCASE, id COLLATE NOCASE;
            """;

        var assets = new List<AssetSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var categories = JsonSerializer.Deserialize<string[]>(reader.GetString(13)) ?? [];
            var tags = JsonSerializer.Deserialize<AssetTag[]>(reader.GetString(14)) ?? [];
            assets.Add(new(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetInt32(10),
                reader.IsDBNull(11) ? null : reader.GetDouble(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                categories,
                tags,
                DateTimeOffset.Parse(
                    reader.GetString(15),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind)));
        }

        return assets;
    }

    public async Task<IndexUpdateResult> ReplaceLibraryAsync(
        string libraryRoot,
        IReadOnlyList<AssetSummary> assets,
        ScanResult draftResult,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var existing = await LoadExistingAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);
            var added = 0;
            var updated = 0;

            await ExecuteAsync(
                connection,
                transaction,
                "CREATE TEMP TABLE current_scan_ids(id TEXT PRIMARY KEY COLLATE NOCASE) WITHOUT ROWID;",
                cancellationToken).ConfigureAwait(false);

            foreach (var asset in assets)
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
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            scanState.Parameters.AddWithValue("$result", JsonSerializer.Serialize(draftResult));
            await scanState.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(added, updated, removed);
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
                id, library_root, name, asset_type, asset_folder_path, json_path,
                thumbnail_path, preview_path, biome, region, physical_size,
                max_resolution, texel_density, average_color, categories_json,
                tags_json, last_write_time_utc)
            VALUES(
                $id, $root, $name, $type, $folder, $json, $thumb, $preview,
                $biome, $region, $size, $resolution, $texel, $color,
                $categories, $tags, $lastWrite)
            ON CONFLICT(id) DO UPDATE SET
                library_root = excluded.library_root,
                name = excluded.name,
                asset_type = excluded.asset_type,
                asset_folder_path = excluded.asset_folder_path,
                json_path = excluded.json_path,
                thumbnail_path = excluded.thumbnail_path,
                preview_path = excluded.preview_path,
                biome = excluded.biome,
                region = excluded.region,
                physical_size = excluded.physical_size,
                max_resolution = excluded.max_resolution,
                texel_density = excluded.texel_density,
                average_color = excluded.average_color,
                categories_json = excluded.categories_json,
                tags_json = excluded.tags_json,
                last_write_time_utc = excluded.last_write_time_utc;
            """;
        Add(command, "$id", asset.Id);
        Add(command, "$root", libraryRoot);
        Add(command, "$name", asset.Name);
        Add(command, "$type", asset.AssetType);
        Add(command, "$folder", asset.AssetFolderPath);
        Add(command, "$json", asset.JsonPath);
        Add(command, "$thumb", asset.ThumbnailPath);
        Add(command, "$preview", asset.PreviewPath);
        Add(command, "$biome", asset.Biome);
        Add(command, "$region", asset.Region);
        Add(command, "$size", asset.PhysicalSize);
        Add(command, "$resolution", asset.MaxResolution);
        Add(command, "$texel", asset.TexelDensity);
        Add(command, "$color", asset.AverageColor);
        Add(command, "$categories", JsonSerializer.Serialize(asset.Categories));
        Add(command, "$tags", JsonSerializer.Serialize(asset.Tags));
        Add(
            command,
            "$lastWrite",
            asset.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture));
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

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

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
