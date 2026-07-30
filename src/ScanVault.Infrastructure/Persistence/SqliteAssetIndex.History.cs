using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ScanVault.Core.Models;

namespace ScanVault.Infrastructure.Persistence;

public sealed partial class SqliteAssetIndex
{
    public const int CurrentFingerprintVersion = 1;
    private const int CompletedRunsToKeepPerLibrary = 20;
    private const int NonCompletedRunsToKeepPerLibrary = 20;

    public async Task<string> BeginScanRunAsync(string libraryRoot, string applicationVersion, string commitSha, CancellationToken cancellationToken)
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
            await MarkStaleRunningRunsAsync(connection, transaction, identity, now, cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO scan_runs(id, library_identity, library_root, started_at_utc, status,
                    application_version, commit_sha, schema_version, normalization_version, fingerprint_version,
                    total_assets, added_assets, changed_assets, removed_assets, unchanged_assets, is_initial_baseline)
                VALUES($id, $identity, $root, $started, $status, $app, $commit, $schema, $normalization,
                    $fingerprint, 0, 0, 0, 0, 0, 0);
                """;
            Add(command, "$id", id);
            Add(command, "$identity", identity);
            Add(command, "$root", identity);
            Add(command, "$started", now.ToString("O", CultureInfo.InvariantCulture));
            Add(command, "$status", (int)ScanRunStatus.Running);
            Add(command, "$app", string.IsNullOrWhiteSpace(applicationVersion) ? "Unknown" : applicationVersion);
            Add(command, "$commit", string.IsNullOrWhiteSpace(commitSha) ? "unavailable" : commitSha);
            Add(command, "$schema", CurrentSchemaVersion);
            Add(command, "$normalization", CurrentNormalizationVersion);
            Add(command, "$fingerprint", CurrentFingerprintVersion);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return id;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task FinishScanRunAsync(string scanRunId, ScanRunStatus status, string? errorMessage, CancellationToken cancellationToken)
    {
        if (status == ScanRunStatus.Completed) throw new ArgumentException("Completed runs finish during replacement.", nameof(status));
        await PrepareWritableDatabaseAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureWritableConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE scan_runs
            SET finished_at_utc = $finished, status = $status, error_message = $error
            WHERE id = $id AND status = $running;
            """;
        Add(command, "$finished", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        Add(command, "$status", (int)status);
        Add(command, "$error", Truncate(errorMessage, 1000));
        Add(command, "$id", scanRunId);
        Add(command, "$running", (int)ScanRunStatus.Running);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ScanRunSummary>> GetScanRunsAsync(int limit, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!Compatibility.IsReadable) return [];
        await using var connection = await OpenReadOnlyAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, library_identity, library_root, started_at_utc, finished_at_utc, status,
                   application_version, commit_sha, schema_version, normalization_version, fingerprint_version,
                   total_assets, added_assets, changed_assets, removed_assets, unchanged_assets, error_message, is_initial_baseline
            FROM scan_runs ORDER BY started_at_utc DESC LIMIT $limit;
            """;
        Add(command, "$limit", Math.Max(1, limit));
        var runs = new List<ScanRunSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) runs.Add(ReadScanRun(reader));
        return runs;
    }

    public async Task<IReadOnlyList<ScanChangeSummary>> GetScanChangesAsync(string scanRunId, AssetChangeKind kind, int limit, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!Compatibility.IsReadable) return [];
        await using var connection = await OpenReadOnlyAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.scan_run_id, c.asset_identity, c.kind, c.flags, c.asset_id, c.name, c.asset_type,
                   c.previous_path, c.current_path, c.completeness,
                   EXISTS(SELECT 1 FROM assets a WHERE a.id = c.asset_id COLLATE NOCASE)
            FROM scan_changes c
            WHERE c.scan_run_id = $scanRunId AND c.kind = $kind
            ORDER BY c.name COLLATE NOCASE, c.asset_id COLLATE NOCASE
            LIMIT $limit;
            """;
        Add(command, "$scanRunId", scanRunId);
        Add(command, "$kind", (int)kind);
        Add(command, "$limit", Math.Max(1, limit));
        var changes = new List<ScanChangeSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            changes.Add(new(reader.GetString(0), reader.GetString(1), (AssetChangeKind)reader.GetInt32(2),
                (AssetChangeReason)reader.GetInt32(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8),
                (AssetCompletenessStatus)reader.GetInt32(9), reader.GetInt32(10) != 0));
        }
        return changes;
    }

    private static async Task<HistoryBuildResult> BuildHistoryAsync(SqliteConnection connection, SqliteTransaction transaction, string libraryRoot, IReadOnlyList<AssetSummary> assets, string scanRunId, CancellationToken cancellationToken)
    {
        var libraryIdentity = NormalizeLibraryIdentity(libraryRoot);
        var previousScanRunId = await ReadPreviousCompletedRunIdAsync(connection, transaction, libraryIdentity, cancellationToken).ConfigureAwait(false);
        var previous = previousScanRunId is null
            ? new Dictionary<string, AssetSnapshot>(StringComparer.OrdinalIgnoreCase)
            : await LoadSnapshotsAsync(connection, transaction, previousScanRunId, cancellationToken).ConfigureAwait(false);
        var current = assets.Select(asset => CreateSnapshot(libraryRoot, scanRunId, asset))
            .ToDictionary(static snapshot => snapshot.AssetIdentity, StringComparer.OrdinalIgnoreCase);
        var changes = new List<AssetChangeRow>();
        foreach (var snapshot in current.Values.OrderBy(static item => item.AssetIdentity, StringComparer.OrdinalIgnoreCase))
        {
            if (!previous.TryGetValue(snapshot.AssetIdentity, out var old)) changes.Add(AssetChangeRow.Added(scanRunId, snapshot));
            else
            {
                var flags = CalculateChangeFlags(old, snapshot);
                changes.Add(flags == AssetChangeReason.None
                    ? AssetChangeRow.Unchanged(scanRunId, snapshot, old)
                    : AssetChangeRow.Changed(scanRunId, snapshot, old, flags));
            }
        }
        foreach (var removed in previous.Values.Where(item => !current.ContainsKey(item.AssetIdentity)).OrderBy(static item => item.AssetIdentity, StringComparer.OrdinalIgnoreCase))
        {
            changes.Add(AssetChangeRow.Removed(scanRunId, removed));
        }
        return new(libraryIdentity, previousScanRunId is null, current.Values.ToArray(), changes,
            changes.Count(static change => change.Kind == AssetChangeKind.Added),
            changes.Count(static change => change.Kind == AssetChangeKind.Changed),
            changes.Count(static change => change.Kind == AssetChangeKind.Removed),
            changes.Count(static change => change.Kind == AssetChangeKind.Unchanged));
    }

    private static async Task PersistCompletedHistoryAsync(SqliteConnection connection, SqliteTransaction transaction, string scanRunId, HistoryBuildResult history, ScanResult draftResult, DateTimeOffset finishedAtUtc, CancellationToken cancellationToken)
    {
        foreach (var snapshot in history.Snapshots) await InsertSnapshotAsync(connection, transaction, snapshot, cancellationToken).ConfigureAwait(false);
        foreach (var change in history.Changes) await InsertChangeAsync(connection, transaction, change, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE scan_runs
            SET finished_at_utc = $finished, status = $status, total_assets = $total,
                added_assets = $added, changed_assets = $changed, removed_assets = $removed,
                unchanged_assets = $unchanged, error_message = NULL, is_initial_baseline = $initial
            WHERE id = $id;
            """;
        Add(command, "$finished", finishedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        Add(command, "$status", (int)ScanRunStatus.Completed);
        Add(command, "$total", draftResult.IndexedAssets);
        Add(command, "$added", history.Added);
        Add(command, "$changed", history.Changed);
        Add(command, "$removed", history.Removed);
        Add(command, "$unchanged", history.Unchanged);
        Add(command, "$initial", history.IsInitialBaseline);
        Add(command, "$id", scanRunId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await ApplyHistoryRetentionAsync(connection, transaction, history.LibraryIdentity, scanRunId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task MigrateVersionThreeAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await CreateHistoryTablesAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, "UPDATE schema_info SET version = 4;", cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task CreateHistoryTablesAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, """
            CREATE TABLE scan_runs (
                id TEXT PRIMARY KEY,
                library_identity TEXT NOT NULL COLLATE NOCASE,
                library_root TEXT NOT NULL,
                started_at_utc TEXT NOT NULL,
                finished_at_utc TEXT NULL,
                status INTEGER NOT NULL,
                application_version TEXT NOT NULL,
                commit_sha TEXT NOT NULL,
                schema_version INTEGER NOT NULL,
                normalization_version INTEGER NOT NULL,
                fingerprint_version INTEGER NOT NULL,
                total_assets INTEGER NOT NULL,
                added_assets INTEGER NOT NULL,
                changed_assets INTEGER NOT NULL,
                removed_assets INTEGER NOT NULL,
                unchanged_assets INTEGER NOT NULL,
                error_message TEXT NULL,
                is_initial_baseline INTEGER NOT NULL
            );
            CREATE TABLE scan_asset_snapshots (
                scan_run_id TEXT NOT NULL,
                asset_identity TEXT NOT NULL COLLATE NOCASE,
                asset_id TEXT NOT NULL COLLATE NOCASE,
                name TEXT NOT NULL,
                asset_type TEXT NOT NULL,
                current_path TEXT NULL,
                completeness INTEGER NOT NULL,
                fingerprint TEXT NOT NULL,
                fingerprint_version INTEGER NOT NULL,
                summary_json TEXT NOT NULL,
                PRIMARY KEY(scan_run_id, asset_identity),
                FOREIGN KEY(scan_run_id) REFERENCES scan_runs(id) ON DELETE CASCADE
            );
            CREATE TABLE scan_changes (
                scan_run_id TEXT NOT NULL,
                asset_identity TEXT NOT NULL COLLATE NOCASE,
                kind INTEGER NOT NULL,
                flags INTEGER NOT NULL,
                asset_id TEXT NOT NULL COLLATE NOCASE,
                name TEXT NOT NULL,
                asset_type TEXT NOT NULL,
                previous_path TEXT NULL,
                current_path TEXT NULL,
                completeness INTEGER NOT NULL,
                previous_fingerprint TEXT NULL,
                current_fingerprint TEXT NULL,
                PRIMARY KEY(scan_run_id, asset_identity, kind),
                FOREIGN KEY(scan_run_id) REFERENCES scan_runs(id) ON DELETE CASCADE
            );
            CREATE INDEX ix_scan_runs_library_started ON scan_runs(library_identity, started_at_utc DESC);
            CREATE INDEX ix_scan_runs_status ON scan_runs(status, started_at_utc DESC);
            CREATE INDEX ix_scan_snapshots_asset ON scan_asset_snapshots(asset_identity);
            CREATE INDEX ix_scan_changes_run_kind ON scan_changes(scan_run_id, kind);
            CREATE INDEX ix_scan_changes_asset ON scan_changes(asset_identity);
            """, cancellationToken).ConfigureAwait(false);
    }

    private static async Task MarkStaleRunningRunsAsync(SqliteConnection connection, SqliteTransaction transaction, string libraryIdentity, DateTimeOffset finishedAtUtc, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE scan_runs
            SET status = $failed, finished_at_utc = $finished, error_message = 'Interrupted by application shutdown before completion.'
            WHERE library_identity = $identity AND status = $running;
            """;
        Add(command, "$failed", (int)ScanRunStatus.Failed);
        Add(command, "$finished", finishedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        Add(command, "$identity", libraryIdentity);
        Add(command, "$running", (int)ScanRunStatus.Running);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> ReadPreviousCompletedRunIdAsync(SqliteConnection connection, SqliteTransaction transaction, string libraryIdentity, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id FROM scan_runs
            WHERE library_identity = $identity AND status = $completed AND fingerprint_version = $fingerprint
            ORDER BY finished_at_utc DESC LIMIT 1;
            """;
        Add(command, "$identity", libraryIdentity);
        Add(command, "$completed", (int)ScanRunStatus.Completed);
        Add(command, "$fingerprint", CurrentFingerprintVersion);
        var value = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static async Task<Dictionary<string, AssetSnapshot>> LoadSnapshotsAsync(SqliteConnection connection, SqliteTransaction transaction, string scanRunId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT asset_identity, asset_id, name, asset_type, current_path, completeness, fingerprint, fingerprint_version, summary_json
            FROM scan_asset_snapshots WHERE scan_run_id = $scanRunId;
            """;
        Add(command, "$scanRunId", scanRunId);
        var snapshots = new Dictionary<string, AssetSnapshot>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var summary = JsonSerializer.Deserialize<AssetSnapshotSummary>(reader.GetString(8)) ?? AssetSnapshotSummary.Empty;
            var snapshot = new AssetSnapshot(scanRunId, reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), (AssetCompletenessStatus)reader.GetInt32(5), reader.GetString(6), reader.GetInt32(7), summary);
            snapshots[snapshot.AssetIdentity] = snapshot;
        }
        return snapshots;
    }

    private static AssetSnapshot CreateSnapshot(string libraryRoot, string scanRunId, AssetSummary asset)
    {
        var summary = AssetSnapshotSummary.FromAsset(libraryRoot, asset);
        return new(scanRunId, CreateAssetIdentity(libraryRoot, asset), asset.Id, asset.Name, asset.AssetType,
            summary.AssetFolderPath, asset.Content.Completeness, ComputeFingerprint(summary), CurrentFingerprintVersion, summary);
    }

    private static string CreateAssetIdentity(string libraryRoot, AssetSummary asset) =>
        !string.IsNullOrWhiteSpace(asset.Id) ? asset.Id.Trim() : NormalizeRelativePath(libraryRoot, asset.JsonPath) ?? asset.JsonPath;

    private static AssetChangeReason CalculateChangeFlags(AssetSnapshot previous, AssetSnapshot current)
    {
        var flags = AssetChangeReason.None;
        if (!StringComparer.OrdinalIgnoreCase.Equals(previous.CurrentPath, current.CurrentPath)) flags |= AssetChangeReason.Path;
        if (previous.Completeness != current.Completeness) flags |= AssetChangeReason.Completeness;
        if (!EqualityComparer<ImageResolution?>.Default.Equals(previous.Summary.MaxResolution, current.Summary.MaxResolution)) flags |= AssetChangeReason.Resolution;
        if (!StringComparer.Ordinal.Equals(previous.Summary.InventoryJson, current.Summary.InventoryJson)) flags |= AssetChangeReason.Inventory;
        if (!StringComparer.Ordinal.Equals(previous.Summary.FilesJson, current.Summary.FilesJson)) flags |= AssetChangeReason.Files;
        if (!StringComparer.Ordinal.Equals(previous.Summary.MetadataJson, current.Summary.MetadataJson) || !StringComparer.Ordinal.Equals(previous.Fingerprint, current.Fingerprint) && flags == AssetChangeReason.None) flags |= AssetChangeReason.Metadata;
        return flags;
    }

    private static async Task InsertSnapshotAsync(SqliteConnection connection, SqliteTransaction transaction, AssetSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO scan_asset_snapshots(scan_run_id, asset_identity, asset_id, name, asset_type, current_path, completeness, fingerprint, fingerprint_version, summary_json)
            VALUES($scanRunId, $identity, $assetId, $name, $type, $path, $completeness, $fingerprint, $version, $summary);
            """;
        Add(command, "$scanRunId", snapshot.ScanRunId);
        Add(command, "$identity", snapshot.AssetIdentity);
        Add(command, "$assetId", snapshot.AssetId);
        Add(command, "$name", snapshot.Name);
        Add(command, "$type", snapshot.AssetType);
        Add(command, "$path", snapshot.CurrentPath);
        Add(command, "$completeness", (int)snapshot.Completeness);
        Add(command, "$fingerprint", snapshot.Fingerprint);
        Add(command, "$version", snapshot.FingerprintVersion);
        Add(command, "$summary", JsonSerializer.Serialize(snapshot.Summary));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertChangeAsync(SqliteConnection connection, SqliteTransaction transaction, AssetChangeRow change, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO scan_changes(scan_run_id, asset_identity, kind, flags, asset_id, name, asset_type, previous_path, current_path, completeness, previous_fingerprint, current_fingerprint)
            VALUES($scanRunId, $identity, $kind, $flags, $assetId, $name, $type, $previousPath, $currentPath, $completeness, $previousFingerprint, $currentFingerprint);
            """;
        Add(command, "$scanRunId", change.ScanRunId);
        Add(command, "$identity", change.AssetIdentity);
        Add(command, "$kind", (int)change.Kind);
        Add(command, "$flags", (int)change.Flags);
        Add(command, "$assetId", change.AssetId);
        Add(command, "$name", change.Name);
        Add(command, "$type", change.AssetType);
        Add(command, "$previousPath", change.PreviousPath);
        Add(command, "$currentPath", change.CurrentPath);
        Add(command, "$completeness", (int)change.Completeness);
        Add(command, "$previousFingerprint", change.PreviousFingerprint);
        Add(command, "$currentFingerprint", change.CurrentFingerprint);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyHistoryRetentionAsync(SqliteConnection connection, SqliteTransaction transaction, string libraryIdentity, string currentScanRunId, CancellationToken cancellationToken)
    {
        await ExecuteRetentionAsync(connection, transaction, libraryIdentity, currentScanRunId, ScanRunStatus.Completed, CompletedRunsToKeepPerLibrary, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM scan_runs WHERE library_identity = $identity AND status IN ($cancelled, $failed)
              AND id NOT IN (SELECT id FROM scan_runs WHERE library_identity = $identity AND status IN ($cancelled, $failed) ORDER BY finished_at_utc DESC LIMIT $keep);
            """;
        Add(command, "$identity", libraryIdentity);
        Add(command, "$cancelled", (int)ScanRunStatus.Cancelled);
        Add(command, "$failed", (int)ScanRunStatus.Failed);
        Add(command, "$keep", NonCompletedRunsToKeepPerLibrary);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteRetentionAsync(SqliteConnection connection, SqliteTransaction transaction, string libraryIdentity, string currentScanRunId, ScanRunStatus status, int keep, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM scan_runs WHERE library_identity = $identity AND status = $status AND id <> $current
              AND id NOT IN (SELECT id FROM scan_runs WHERE library_identity = $identity AND status = $status ORDER BY finished_at_utc DESC LIMIT $keep);
            """;
        Add(command, "$identity", libraryIdentity);
        Add(command, "$status", (int)status);
        Add(command, "$current", currentScanRunId);
        Add(command, "$keep", keep);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ScanRunSummary ReadScanRun(SqliteDataReader reader) => new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
        DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        (ScanRunStatus)reader.GetInt32(5), reader.GetString(6), reader.GetString(7), reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10),
        reader.GetInt32(11), reader.GetInt32(12), reader.GetInt32(13), reader.GetInt32(14), reader.GetInt32(15), reader.IsDBNull(16) ? null : reader.GetString(16), reader.GetInt32(17) != 0);

    private static string NormalizeLibraryIdentity(string libraryRoot) => Path.GetFullPath(libraryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string? NormalizeRelativePath(string libraryRoot, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var relative = Path.GetRelativePath(NormalizeLibraryIdentity(libraryRoot), Path.GetFullPath(path));
        return relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string ComputeFingerprint(AssetSnapshotSummary summary) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(summary))));

    private static string? Truncate(string? value, int maxLength) => string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];

    private sealed record HistoryBuildResult(string LibraryIdentity, bool IsInitialBaseline, IReadOnlyList<AssetSnapshot> Snapshots, IReadOnlyList<AssetChangeRow> Changes, int Added, int Changed, int Removed, int Unchanged);
    private sealed record AssetSnapshot(string ScanRunId, string AssetIdentity, string AssetId, string Name, string AssetType, string? CurrentPath, AssetCompletenessStatus Completeness, string Fingerprint, int FingerprintVersion, AssetSnapshotSummary Summary);
    private sealed record AssetSnapshotSummary(string AssetId, string Name, string AssetType, string? RawAssetType, string? AssetFolderPath, string? JsonPath, ImageResolution? MaxResolution, string MetadataJson, string InventoryJson, string FilesJson, AssetCompletenessStatus Completeness)
    {
        public static AssetSnapshotSummary Empty { get; } = new(string.Empty, string.Empty, string.Empty, null, null, null, null, string.Empty, string.Empty, string.Empty, AssetCompletenessStatus.Unknown);
        public static AssetSnapshotSummary FromAsset(string libraryRoot, AssetSummary asset)
        {
            var metadata = JsonSerializer.Serialize(new
            {
                asset.Id,
                asset.Name,
                asset.AssetType,
                asset.RawAssetType,
                asset.Biome,
                asset.Region,
                asset.PhysicalSize,
                asset.MaxResolution,
                asset.TexelDensity,
                asset.AverageColor,
                Categories = asset.Categories.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase),
                Tags = asset.Tags.OrderBy(static item => item.Kind).ThenBy(static item => item.Value, StringComparer.OrdinalIgnoreCase),
                asset.LastWriteTimeUtc
            });
            var inventory = JsonSerializer.Serialize(asset.Content);
            var files = JsonSerializer.Serialize(CollectFileFacts(libraryRoot, asset));
            return new(asset.Id, asset.Name, asset.AssetType, asset.RawAssetType, NormalizeRelativePath(libraryRoot, asset.AssetFolderPath), NormalizeRelativePath(libraryRoot, asset.JsonPath), asset.MaxResolution, metadata, inventory, files, asset.Content.Completeness);
        }
    }
    private sealed record FileFact(string RelativePath, long? Size, string? LastWriteUtc);
    private static FileFact[] CollectFileFacts(string libraryRoot, AssetSummary asset)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddPath(paths, asset.JsonPath); AddPath(paths, asset.ThumbnailPath); AddPath(paths, asset.PreviewPath);
        foreach (var mesh in asset.Content.Variants.SelectMany(static item => item.Meshes)) AddPath(paths, mesh.Path);
        foreach (var component in asset.Content.TextureSets.SelectMany(static item => item.Components)) AddPath(paths, component.Path);
        foreach (var file in asset.Content.UnclassifiedFiles) AddPath(paths, file.Path);
        return paths.Select(path => CreateFileFact(libraryRoot, path)).OrderBy(static fact => fact.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static void AddPath(HashSet<string> paths, string? path) { if (!string.IsNullOrWhiteSpace(path)) paths.Add(path); }
    private static FileFact CreateFileFact(string libraryRoot, string path)
    {
        var relative = NormalizeRelativePath(libraryRoot, path) ?? path;
        try { var info = new FileInfo(path); return info.Exists ? new(relative, info.Length, info.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture)) : new(relative, null, null); }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { return new(relative, null, null); }
    }
    private sealed record AssetChangeRow(string ScanRunId, string AssetIdentity, AssetChangeKind Kind, AssetChangeReason Flags, string AssetId, string Name, string AssetType, string? PreviousPath, string? CurrentPath, AssetCompletenessStatus Completeness, string? PreviousFingerprint, string? CurrentFingerprint)
    {
        public static AssetChangeRow Added(string scanRunId, AssetSnapshot snapshot) => new(scanRunId, snapshot.AssetIdentity, AssetChangeKind.Added, AssetChangeReason.None, snapshot.AssetId, snapshot.Name, snapshot.AssetType, null, snapshot.CurrentPath, snapshot.Completeness, null, snapshot.Fingerprint);
        public static AssetChangeRow Removed(string scanRunId, AssetSnapshot snapshot) => new(scanRunId, snapshot.AssetIdentity, AssetChangeKind.Removed, AssetChangeReason.None, snapshot.AssetId, snapshot.Name, snapshot.AssetType, snapshot.CurrentPath, null, snapshot.Completeness, snapshot.Fingerprint, null);
        public static AssetChangeRow Changed(string scanRunId, AssetSnapshot current, AssetSnapshot previous, AssetChangeReason flags) => new(scanRunId, current.AssetIdentity, AssetChangeKind.Changed, flags, current.AssetId, current.Name, current.AssetType, previous.CurrentPath, current.CurrentPath, current.Completeness, previous.Fingerprint, current.Fingerprint);
        public static AssetChangeRow Unchanged(string scanRunId, AssetSnapshot current, AssetSnapshot previous) => new(scanRunId, current.AssetIdentity, AssetChangeKind.Unchanged, AssetChangeReason.None, current.AssetId, current.Name, current.AssetType, previous.CurrentPath, current.CurrentPath, current.Completeness, previous.Fingerprint, current.Fingerprint);
    }
}

