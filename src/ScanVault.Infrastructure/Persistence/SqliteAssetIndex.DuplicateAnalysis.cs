using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Infrastructure.Persistence;

public sealed partial class SqliteAssetIndex
{
    private const int CompletedDuplicateRunsToKeepPerLibrary = 10;
    private const int NonCompletedDuplicateRunsToKeepPerLibrary = 10;

    public async Task<DuplicateAnalysisRun> BeginDuplicateAnalysisRunAsync(
        string libraryRoot,
        int totalAssets,
        CancellationToken cancellationToken)
    {
        var identity = NormalizeLibraryIdentity(libraryRoot);
        await PrepareWritableDatabaseAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureWritableConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var id = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            await MarkStaleRunningDuplicateRunsAsync(connection, transaction, identity, now, cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO duplicate_analysis_runs(id, library_identity, library_root, started_at_utc, status,
                    is_stale, total_assets, candidate_assets, files_hashed, bytes_hashed, cache_hits,
                    exact_duplicate_groups, conflicting_id_groups, probable_duplicate_groups,
                    partial_duplicate_groups, assets_involved, potential_reclaimable_size_bytes)
                VALUES($id, $identity, $root, $started, $status, 0, $total, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                """;
            Add(command, "$id", id);
            Add(command, "$identity", identity);
            Add(command, "$root", identity);
            Add(command, "$started", now.ToString("O", CultureInfo.InvariantCulture));
            Add(command, "$status", (int)DuplicateAnalysisStatus.Running);
            Add(command, "$total", totalAssets);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(id, identity, identity, now, null, DuplicateAnalysisStatus.Running, false, totalAssets, 0, 0, 0, 0, null, null, new(0, 0, 0, 0, 0, 0));
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task FinishDuplicateAnalysisRunAsync(
        string runId,
        DuplicateAnalysisStatus status,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        if (status == DuplicateAnalysisStatus.Completed) throw new ArgumentException("Completed duplicate runs are persisted atomically.", nameof(status));
        await PrepareWritableDatabaseAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureWritableConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE duplicate_analysis_runs
            SET finished_at_utc = $finished, status = $status, error_message = $error
            WHERE id = $id AND status = $running;
            """;
        Add(command, "$finished", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        Add(command, "$status", (int)status);
        Add(command, "$error", Truncate(errorMessage, 1000));
        Add(command, "$id", runId);
        Add(command, "$running", (int)DuplicateAnalysisStatus.Running);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AssetSummary>> GetDuplicateAnalysisSourcesAsync(
        string libraryRoot,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!Compatibility.IsReadable) return [];

        var identity = NormalizeLibraryIdentity(libraryRoot);
        await using var connection = await OpenReadOnlyAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT asset_json
            FROM duplicate_analysis_sources
            WHERE library_identity = $identity
            ORDER BY asset_key COLLATE NOCASE;
            """;
        Add(command, "$identity", identity);

        var sources = new List<AssetSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (JsonSerializer.Deserialize<AssetSummary>(reader.GetString(0)) is { } asset) sources.Add(asset);
        }

        return sources.Count == 0 ? await GetAssetsAsync(cancellationToken).ConfigureAwait(false) : sources;
    }

    public async Task PersistDuplicateAnalysisAsync(
        DuplicateAnalysisPersistRequest request,
        CancellationToken cancellationToken)
    {
        await PrepareWritableDatabaseAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureWritableConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var run = request.Run;
            var summary = run.Summary;
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE duplicate_analysis_runs
                    SET finished_at_utc = $finished, status = $status, is_stale = 0,
                        candidate_assets = $candidates, files_hashed = $filesHashed,
                        bytes_hashed = $bytesHashed, cache_hits = $cacheHits,
                        duration_ms = $duration, error_message = NULL,
                        exact_duplicate_groups = $exact, conflicting_id_groups = $conflict,
                        probable_duplicate_groups = $probable, partial_duplicate_groups = $partial,
                        assets_involved = $assetsInvolved,
                        potential_reclaimable_size_bytes = $reclaimable
                    WHERE id = $id;
                    """;
                Add(command, "$finished", (run.FinishedAtUtc ?? DateTimeOffset.UtcNow).ToString("O", CultureInfo.InvariantCulture));
                Add(command, "$status", (int)DuplicateAnalysisStatus.Completed);
                Add(command, "$candidates", run.CandidateAssets);
                Add(command, "$filesHashed", run.FilesHashed);
                Add(command, "$bytesHashed", run.BytesHashed);
                Add(command, "$cacheHits", run.CacheHits);
                Add(command, "$duration", (long)(run.Duration ?? TimeSpan.Zero).TotalMilliseconds);
                Add(command, "$exact", summary.ExactDuplicateGroups);
                Add(command, "$conflict", summary.ConflictingIdGroups);
                Add(command, "$probable", summary.ProbableDuplicateGroups);
                Add(command, "$partial", summary.PartialDuplicateGroups);
                Add(command, "$assetsInvolved", summary.AssetsInvolved);
                Add(command, "$reclaimable", summary.PotentialReclaimableSizeBytes);
                Add(command, "$id", run.Id);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM duplicate_groups WHERE run_id = $id;";
                Add(delete, "$id", request.Run.Id);
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            foreach (var group in request.Groups)
            {
                await InsertDuplicateGroupAsync(connection, transaction, run.Id, group, cancellationToken).ConfigureAwait(false);
            }

            await ApplyDuplicateAnalysisRetentionAsync(connection, transaction, run.LibraryIdentity, run.Id, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<DuplicateAnalysisResult?> GetLatestDuplicateAnalysisAsync(
        string libraryRoot,
        bool includeStale,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!Compatibility.IsReadable) return null;
        var identity = NormalizeLibraryIdentity(libraryRoot);
        await using var connection = await OpenReadOnlyAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, library_identity, library_root, started_at_utc, finished_at_utc, status, is_stale,
                   total_assets, candidate_assets, files_hashed, bytes_hashed, cache_hits,
                   duration_ms, error_message, exact_duplicate_groups, conflicting_id_groups,
                   probable_duplicate_groups, partial_duplicate_groups, assets_involved,
                   potential_reclaimable_size_bytes
            FROM duplicate_analysis_runs
            WHERE library_identity = $identity AND status = $completed {(includeStale ? "" : "AND is_stale = 0")}
            ORDER BY finished_at_utc DESC LIMIT 1;
            """;
        Add(command, "$identity", identity);
        Add(command, "$completed", (int)DuplicateAnalysisStatus.Completed);
        DuplicateAnalysisRun? run = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) run = ReadDuplicateRun(reader);
        }

        if (run is null) return null;
        var groups = await LoadDuplicateGroupsAsync(connection, run.Id, cancellationToken).ConfigureAwait(false);
        return new(run, groups);
    }

    public async Task<FileHashCacheEntry?> GetFileHashAsync(
        string normalizedPath,
        string hashAlgorithm,
        int hashAlgorithmVersion,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!Compatibility.IsReadable) return null;
        await using var connection = await OpenReadOnlyAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT normalized_path, file_size_bytes, last_write_time_utc, hash_algorithm,
                   hash_algorithm_version, content_hash, computed_at_utc
            FROM file_hash_cache
            WHERE normalized_path = $path COLLATE NOCASE
              AND hash_algorithm = $algorithm
              AND hash_algorithm_version = $version;
            """;
        Add(command, "$path", normalizedPath);
        Add(command, "$algorithm", hashAlgorithm);
        Add(command, "$version", hashAlgorithmVersion);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadFileHash(reader) : null;
    }

    public async Task UpsertFileHashAsync(FileHashCacheEntry entry, CancellationToken cancellationToken)
    {
        await PrepareWritableDatabaseAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureWritableConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO file_hash_cache(normalized_path, file_size_bytes, last_write_time_utc,
                hash_algorithm, hash_algorithm_version, content_hash, computed_at_utc)
            VALUES($path, $size, $lastWrite, $algorithm, $version, $hash, $computed)
            ON CONFLICT(normalized_path, hash_algorithm, hash_algorithm_version) DO UPDATE SET
                file_size_bytes = excluded.file_size_bytes,
                last_write_time_utc = excluded.last_write_time_utc,
                content_hash = excluded.content_hash,
                computed_at_utc = excluded.computed_at_utc;
            """;
        Add(command, "$path", entry.NormalizedPath);
        Add(command, "$size", entry.FileSizeBytes);
        Add(command, "$lastWrite", entry.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture));
        Add(command, "$algorithm", entry.HashAlgorithm);
        Add(command, "$version", entry.HashAlgorithmVersion);
        Add(command, "$hash", entry.ContentHash);
        Add(command, "$computed", entry.ComputedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task MigrateVersionFourAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await CreateVersionFiveDuplicateAnalysisTablesAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, "UPDATE schema_info SET version = 5;", cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task MigrateVersionFiveAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await CreateDuplicateAnalysisSourceTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, """
                ALTER TABLE duplicate_group_members ADD COLUMN json_path TEXT NOT NULL DEFAULT '';
                UPDATE schema_info SET version = 6;
                """, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task CreateDuplicateAnalysisTablesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, """
            CREATE TABLE file_hash_cache (
                normalized_path TEXT NOT NULL COLLATE NOCASE,
                file_size_bytes INTEGER NOT NULL,
                last_write_time_utc TEXT NOT NULL,
                hash_algorithm TEXT NOT NULL,
                hash_algorithm_version INTEGER NOT NULL,
                content_hash TEXT NOT NULL,
                computed_at_utc TEXT NOT NULL,
                PRIMARY KEY(normalized_path, hash_algorithm, hash_algorithm_version)
            );
            CREATE TABLE duplicate_analysis_runs (
                id TEXT PRIMARY KEY,
                library_identity TEXT NOT NULL COLLATE NOCASE,
                library_root TEXT NOT NULL,
                started_at_utc TEXT NOT NULL,
                finished_at_utc TEXT NULL,
                status INTEGER NOT NULL,
                is_stale INTEGER NOT NULL,
                total_assets INTEGER NOT NULL,
                candidate_assets INTEGER NOT NULL,
                files_hashed INTEGER NOT NULL,
                bytes_hashed INTEGER NOT NULL,
                cache_hits INTEGER NOT NULL,
                duration_ms INTEGER NULL,
                error_message TEXT NULL,
                exact_duplicate_groups INTEGER NOT NULL,
                conflicting_id_groups INTEGER NOT NULL,
                probable_duplicate_groups INTEGER NOT NULL,
                partial_duplicate_groups INTEGER NOT NULL,
                assets_involved INTEGER NOT NULL,
                potential_reclaimable_size_bytes INTEGER NOT NULL
            );
            CREATE TABLE duplicate_groups (
                run_id TEXT NOT NULL,
                group_id TEXT NOT NULL,
                category INTEGER NOT NULL,
                confidence INTEGER NOT NULL,
                estimated_duplicate_size_bytes INTEGER NOT NULL,
                matched_fields TEXT NOT NULL,
                different_fields TEXT NOT NULL,
                PRIMARY KEY(run_id, group_id),
                FOREIGN KEY(run_id) REFERENCES duplicate_analysis_runs(id) ON DELETE CASCADE
            );
            CREATE TABLE duplicate_group_members (
                run_id TEXT NOT NULL,
                group_id TEXT NOT NULL,
                ordinal INTEGER NOT NULL,
                asset_id TEXT NOT NULL COLLATE NOCASE,
                asset_name TEXT NOT NULL,
                asset_type TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                asset_folder_path TEXT NOT NULL,
                json_path TEXT NOT NULL,
                completeness INTEGER NOT NULL,
                file_count INTEGER NOT NULL,
                total_size_bytes INTEGER NOT NULL,
                hash_status INTEGER NOT NULL,
                PRIMARY KEY(run_id, group_id, ordinal),
                FOREIGN KEY(run_id, group_id) REFERENCES duplicate_groups(run_id, group_id) ON DELETE CASCADE
            );
            CREATE TABLE duplicate_reasons (
                run_id TEXT NOT NULL,
                group_id TEXT NOT NULL,
                ordinal INTEGER NOT NULL,
                field TEXT NOT NULL,
                message TEXT NOT NULL,
                PRIMARY KEY(run_id, group_id, ordinal),
                FOREIGN KEY(run_id, group_id) REFERENCES duplicate_groups(run_id, group_id) ON DELETE CASCADE
            );
            CREATE INDEX ix_file_hash_cache_metadata ON file_hash_cache(normalized_path, file_size_bytes, last_write_time_utc);
            CREATE INDEX ix_duplicate_runs_library_finished ON duplicate_analysis_runs(library_identity, status, is_stale, finished_at_utc DESC);
            """, cancellationToken).ConfigureAwait(false);
        await CreateDuplicateAnalysisSourceTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CreateVersionFiveDuplicateAnalysisTablesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, """
            CREATE TABLE file_hash_cache (
                normalized_path TEXT NOT NULL COLLATE NOCASE,
                file_size_bytes INTEGER NOT NULL,
                last_write_time_utc TEXT NOT NULL,
                hash_algorithm TEXT NOT NULL,
                hash_algorithm_version INTEGER NOT NULL,
                content_hash TEXT NOT NULL,
                computed_at_utc TEXT NOT NULL,
                PRIMARY KEY(normalized_path, hash_algorithm, hash_algorithm_version)
            );
            CREATE TABLE duplicate_analysis_runs (
                id TEXT PRIMARY KEY,
                library_identity TEXT NOT NULL COLLATE NOCASE,
                library_root TEXT NOT NULL,
                started_at_utc TEXT NOT NULL,
                finished_at_utc TEXT NULL,
                status INTEGER NOT NULL,
                is_stale INTEGER NOT NULL,
                total_assets INTEGER NOT NULL,
                candidate_assets INTEGER NOT NULL,
                files_hashed INTEGER NOT NULL,
                bytes_hashed INTEGER NOT NULL,
                cache_hits INTEGER NOT NULL,
                duration_ms INTEGER NULL,
                error_message TEXT NULL,
                exact_duplicate_groups INTEGER NOT NULL,
                conflicting_id_groups INTEGER NOT NULL,
                probable_duplicate_groups INTEGER NOT NULL,
                partial_duplicate_groups INTEGER NOT NULL,
                assets_involved INTEGER NOT NULL,
                potential_reclaimable_size_bytes INTEGER NOT NULL
            );
            CREATE TABLE duplicate_groups (
                run_id TEXT NOT NULL,
                group_id TEXT NOT NULL,
                category INTEGER NOT NULL,
                confidence INTEGER NOT NULL,
                estimated_duplicate_size_bytes INTEGER NOT NULL,
                matched_fields TEXT NOT NULL,
                different_fields TEXT NOT NULL,
                PRIMARY KEY(run_id, group_id),
                FOREIGN KEY(run_id) REFERENCES duplicate_analysis_runs(id) ON DELETE CASCADE
            );
            CREATE TABLE duplicate_group_members (
                run_id TEXT NOT NULL,
                group_id TEXT NOT NULL,
                ordinal INTEGER NOT NULL,
                asset_id TEXT NOT NULL COLLATE NOCASE,
                asset_name TEXT NOT NULL,
                asset_type TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                asset_folder_path TEXT NOT NULL,
                completeness INTEGER NOT NULL,
                file_count INTEGER NOT NULL,
                total_size_bytes INTEGER NOT NULL,
                hash_status INTEGER NOT NULL,
                PRIMARY KEY(run_id, group_id, ordinal),
                FOREIGN KEY(run_id, group_id) REFERENCES duplicate_groups(run_id, group_id) ON DELETE CASCADE
            );
            CREATE TABLE duplicate_reasons (
                run_id TEXT NOT NULL,
                group_id TEXT NOT NULL,
                ordinal INTEGER NOT NULL,
                field TEXT NOT NULL,
                message TEXT NOT NULL,
                PRIMARY KEY(run_id, group_id, ordinal),
                FOREIGN KEY(run_id, group_id) REFERENCES duplicate_groups(run_id, group_id) ON DELETE CASCADE
            );
            CREATE INDEX ix_file_hash_cache_metadata ON file_hash_cache(normalized_path, file_size_bytes, last_write_time_utc);
            CREATE INDEX ix_duplicate_runs_library_finished ON duplicate_analysis_runs(library_identity, status, is_stale, finished_at_utc DESC);
            """, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CreateDuplicateAnalysisSourceTableAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, """
            CREATE TABLE duplicate_analysis_sources (
                library_identity TEXT NOT NULL COLLATE NOCASE,
                asset_key TEXT NOT NULL COLLATE NOCASE,
                asset_id TEXT NOT NULL COLLATE NOCASE,
                asset_json TEXT NOT NULL,
                is_browsable_winner INTEGER NOT NULL,
                scan_run_id TEXT NULL,
                PRIMARY KEY(library_identity, asset_key)
            );
            CREATE INDEX ix_duplicate_sources_asset_id ON duplicate_analysis_sources(library_identity, asset_id);
            """, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReplaceDuplicateAnalysisSourcesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string libraryRoot,
        IReadOnlyList<AssetSummary> sources,
        IReadOnlyList<AssetSummary> browsableAssets,
        string scanRunId,
        CancellationToken cancellationToken)
    {
        var identity = NormalizeLibraryIdentity(libraryRoot);
        var winners = browsableAssets.Select(static asset => asset.JsonPath).ToHashSet(PathPolicy.Comparer);
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM duplicate_analysis_sources WHERE library_identity = $identity;";
            Add(delete, "$identity", identity);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var asset in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO duplicate_analysis_sources(library_identity, asset_key, asset_id,
                    asset_json, is_browsable_winner, scan_run_id)
                VALUES($identity, $key, $assetId, $assetJson, $winner, $scanRunId);
                """;
            Add(command, "$identity", identity);
            Add(command, "$key", asset.JsonPath);
            Add(command, "$assetId", asset.Id);
            Add(command, "$assetJson", JsonSerializer.Serialize(asset));
            Add(command, "$winner", winners.Contains(asset.JsonPath));
            Add(command, "$scanRunId", scanRunId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task MarkDuplicateAnalysesStaleAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string libraryRoot,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE duplicate_analysis_runs
            SET is_stale = 1
            WHERE library_identity = $identity AND status = $completed;
            """;
        Add(command, "$identity", NormalizeLibraryIdentity(libraryRoot));
        Add(command, "$completed", (int)DuplicateAnalysisStatus.Completed);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task MarkStaleRunningDuplicateRunsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string libraryIdentity,
        DateTimeOffset finishedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE duplicate_analysis_runs
            SET status = $failed, finished_at_utc = $finished, error_message = 'Interrupted by application shutdown before completion.'
            WHERE library_identity = $identity AND status = $running;
            """;
        Add(command, "$failed", (int)DuplicateAnalysisStatus.Failed);
        Add(command, "$finished", finishedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        Add(command, "$identity", libraryIdentity);
        Add(command, "$running", (int)DuplicateAnalysisStatus.Running);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertDuplicateGroupAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string runId,
        DuplicateGroupResult group,
        CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO duplicate_groups(run_id, group_id, category, confidence,
                    estimated_duplicate_size_bytes, matched_fields, different_fields)
                VALUES($runId, $groupId, $category, $confidence, $size, $matched, $different);
                """;
            Add(command, "$runId", runId);
            Add(command, "$groupId", group.GroupId);
            Add(command, "$category", (int)group.Category);
            Add(command, "$confidence", (int)group.Confidence);
            Add(command, "$size", group.EstimatedDuplicateSizeBytes);
            Add(command, "$matched", string.Join("\n", group.MatchedFields));
            Add(command, "$different", string.Join("\n", group.DifferentFields));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        for (var index = 0; index < group.Members.Count; index++)
        {
            var member = group.Members[index];
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO duplicate_group_members(run_id, group_id, ordinal, asset_id, asset_name,
                    asset_type, relative_path, asset_folder_path, json_path, completeness, file_count,
                    total_size_bytes, hash_status)
                VALUES($runId, $groupId, $ordinal, $assetId, $name, $type, $relativePath,
                    $folder, $jsonPath, $completeness, $fileCount, $totalSize, $hashStatus);
                """;
            Add(command, "$runId", runId);
            Add(command, "$groupId", group.GroupId);
            Add(command, "$ordinal", index);
            Add(command, "$assetId", member.AssetId);
            Add(command, "$name", member.AssetName);
            Add(command, "$type", member.AssetType);
            Add(command, "$relativePath", member.RelativePath);
            Add(command, "$folder", member.AssetFolderPath);
            Add(command, "$jsonPath", member.JsonPath);
            Add(command, "$completeness", (int)member.Completeness);
            Add(command, "$fileCount", member.FileCount);
            Add(command, "$totalSize", member.TotalSizeBytes);
            Add(command, "$hashStatus", (int)member.HashStatus);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        for (var index = 0; index < group.Reasons.Count; index++)
        {
            var reason = group.Reasons[index];
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO duplicate_reasons(run_id, group_id, ordinal, field, message)
                VALUES($runId, $groupId, $ordinal, $field, $message);
                """;
            Add(command, "$runId", runId);
            Add(command, "$groupId", group.GroupId);
            Add(command, "$ordinal", index);
            Add(command, "$field", reason.Field);
            Add(command, "$message", reason.Message);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<DuplicateGroupResult>> LoadDuplicateGroupsAsync(
        SqliteConnection connection,
        string runId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT group_id, category, confidence, estimated_duplicate_size_bytes, matched_fields, different_fields
            FROM duplicate_groups
            WHERE run_id = $runId
            ORDER BY category, estimated_duplicate_size_bytes DESC, group_id;
            """;
        Add(command, "$runId", runId);
        var rows = new List<DuplicateGroupRow>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(new(
                    reader.GetString(0),
                    (DuplicateCategory)reader.GetInt32(1),
                    (DuplicateConfidence)reader.GetInt32(2),
                    reader.GetInt64(3),
                    SplitLines(reader.GetString(4)),
                    SplitLines(reader.GetString(5))));
            }
        }

        var groups = new List<DuplicateGroupResult>();
        foreach (var row in rows)
        {
            groups.Add(new(
                row.GroupId,
                row.Category,
                row.Confidence,
                await LoadDuplicateReasonsAsync(connection, runId, row.GroupId, cancellationToken).ConfigureAwait(false),
                row.MatchedFields,
                row.DifferentFields,
                row.EstimatedDuplicateSizeBytes,
                await LoadDuplicateMembersAsync(connection, runId, row.GroupId, cancellationToken).ConfigureAwait(false)));
        }

        return groups;
    }

    private static async Task<IReadOnlyList<DuplicateReason>> LoadDuplicateReasonsAsync(
        SqliteConnection connection,
        string runId,
        string groupId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT field, message FROM duplicate_reasons WHERE run_id = $runId AND group_id = $groupId ORDER BY ordinal;";
        Add(command, "$runId", runId);
        Add(command, "$groupId", groupId);
        var reasons = new List<DuplicateReason>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) reasons.Add(new(reader.GetString(0), reader.GetString(1)));
        return reasons;
    }

    private static async Task<IReadOnlyList<DuplicateGroupMember>> LoadDuplicateMembersAsync(
        SqliteConnection connection,
        string runId,
        string groupId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT asset_id, asset_name, asset_type, relative_path, asset_folder_path, json_path,
                   completeness, file_count, total_size_bytes, hash_status
            FROM duplicate_group_members
            WHERE run_id = $runId AND group_id = $groupId
            ORDER BY ordinal;
            """;
        Add(command, "$runId", runId);
        Add(command, "$groupId", groupId);
        var members = new List<DuplicateGroupMember>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            members.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), (AssetCompletenessStatus)reader.GetInt32(6), reader.GetInt32(7),
                reader.GetInt64(8), (DuplicateHashStatus)reader.GetInt32(9)));
        }

        return members;
    }

    private static async Task ApplyDuplicateAnalysisRetentionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string libraryIdentity,
        string currentRunId,
        CancellationToken cancellationToken)
    {
        await ExecuteDuplicateRetentionAsync(connection, transaction, libraryIdentity, currentRunId, DuplicateAnalysisStatus.Completed, CompletedDuplicateRunsToKeepPerLibrary, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM duplicate_analysis_runs
            WHERE library_identity = $identity AND status IN ($cancelled, $failed)
              AND id NOT IN (
                SELECT id FROM duplicate_analysis_runs
                WHERE library_identity = $identity AND status IN ($cancelled, $failed)
                ORDER BY finished_at_utc DESC LIMIT $keep);
            """;
        Add(command, "$identity", libraryIdentity);
        Add(command, "$cancelled", (int)DuplicateAnalysisStatus.Cancelled);
        Add(command, "$failed", (int)DuplicateAnalysisStatus.Failed);
        Add(command, "$keep", NonCompletedDuplicateRunsToKeepPerLibrary);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteDuplicateRetentionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string libraryIdentity,
        string currentRunId,
        DuplicateAnalysisStatus status,
        int keep,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM duplicate_analysis_runs
            WHERE library_identity = $identity AND status = $status AND id <> $current
              AND id NOT IN (
                SELECT id FROM duplicate_analysis_runs
                WHERE library_identity = $identity AND status = $status
                ORDER BY finished_at_utc DESC LIMIT $keep);
            """;
        Add(command, "$identity", libraryIdentity);
        Add(command, "$status", (int)status);
        Add(command, "$current", currentRunId);
        Add(command, "$keep", keep);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DuplicateAnalysisRun ReadDuplicateRun(SqliteDataReader reader)
    {
        var duration = reader.IsDBNull(12) ? (TimeSpan?)null : TimeSpan.FromMilliseconds(reader.GetInt64(12));
        var summary = new DuplicateAnalysisSummary(reader.GetInt32(14), reader.GetInt32(15), reader.GetInt32(16),
            reader.GetInt32(17), reader.GetInt32(18), reader.GetInt64(19));
        return new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            (DuplicateAnalysisStatus)reader.GetInt32(5), reader.GetInt32(6) != 0, reader.GetInt32(7),
            reader.GetInt32(8), reader.GetInt32(9), reader.GetInt64(10), reader.GetInt32(11), duration,
            reader.IsDBNull(13) ? null : reader.GetString(13), summary);
    }

    private static FileHashCacheEntry ReadFileHash(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetInt64(1),
        DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        reader.GetString(3),
        reader.GetInt32(4),
        reader.GetString(5),
        DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    private static string[] SplitLines(string value) =>
        string.IsNullOrEmpty(value) ? [] : value.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private sealed record DuplicateGroupRow(
        string GroupId,
        DuplicateCategory Category,
        DuplicateConfidence Confidence,
        long EstimatedDuplicateSizeBytes,
        string[] MatchedFields,
        string[] DifferentFields);
}
