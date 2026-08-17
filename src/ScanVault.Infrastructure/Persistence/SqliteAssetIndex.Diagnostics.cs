using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Infrastructure.Persistence;

public sealed partial class SqliteAssetIndex
{
    public IndexCompatibilityInfo Compatibility { get; private set; } = MissingCompatibility();

    public bool RequiresNormalizationRescan => Compatibility.RequiresRescan;

    public async Task<IndexCompatibilityInfo> InspectCompatibilityAsync(
        CancellationToken cancellationToken)
    {
        _ = ProviderInitialized;
        if (!File.Exists(resolvedPaths.DatabasePath))
        {
            return MissingCompatibility();
        }

        try
        {
            // Compatibility must be known before any connection can create or mutate the file.
            await using var connection = await OpenReadOnlyAsync(cancellationToken)
                .ConfigureAwait(false);
            await using (var queryOnly = connection.CreateCommand())
            {
                queryOnly.CommandText = "PRAGMA query_only = ON;";
                await queryOnly.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var integrity = Convert.ToString(
                await ReadScalarAsync(connection, "PRAGMA quick_check(1);", cancellationToken)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture);
            if (!StringComparer.OrdinalIgnoreCase.Equals(integrity, "ok"))
            {
                return CorruptedCompatibility("SQLite integrity validation failed.");
            }

            if (!await TableExistsAsync(connection, "schema_info", cancellationToken)
                    .ConfigureAwait(false))
            {
                return CorruptedCompatibility("The schema marker table is missing.");
            }

            var schemaRows = Convert.ToInt32(
                await ReadScalarAsync(
                        connection,
                        "SELECT COUNT(*) FROM schema_info;",
                        cancellationToken)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture);
            if (schemaRows != 1)
            {
                return CorruptedCompatibility("The schema marker is invalid.");
            }

            var schemaVersion = Convert.ToInt32(
                await ReadScalarAsync(
                        connection,
                        "SELECT version FROM schema_info LIMIT 1;",
                        cancellationToken)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture);
            // Structural compatibility is decided before reading columns introduced by newer schemas.
            if (schemaVersion > CurrentSchemaVersion)
            {
                return NewerCompatibility(schemaVersion, null);
            }

            if (schemaVersion is 1 or 2 or 3 or 4 or 5 or 6)
            {
                return new(
                    IndexCompatibilityState.RequiresMigration,
                    schemaVersion,
                    null,
                    IsReadable: false,
                    CanWrite: false,
                    RequiresRescan: false,
                    "A supported schema migration is required before the index can be read.");
            }

            if (!await HasRequiredTablesAsync(connection, cancellationToken).ConfigureAwait(false))
            {
                return CorruptedCompatibility("Required index tables are missing.");
            }

            if (schemaVersion != CurrentSchemaVersion)
            {
                return CorruptedCompatibility($"Unsupported older schema marker {schemaVersion}.");
            }

            if (!await TableExistsAsync(connection, "asset_inventory_maps", cancellationToken).ConfigureAwait(false))
            {
                return CorruptedCompatibility("Required inventory index table is missing.");
            }

            foreach (var historyTable in new[] { "scan_runs", "scan_asset_snapshots", "scan_changes" })
            {
                if (!await TableExistsAsync(connection, historyTable, cancellationToken).ConfigureAwait(false))
                {
                    return CorruptedCompatibility($"Required scan history table {historyTable} is missing.");
                }
            }

            foreach (var duplicateTable in new[] { "file_hash_cache", "duplicate_analysis_runs", "duplicate_groups", "duplicate_group_members", "duplicate_reasons", "duplicate_analysis_sources" })
            {
                if (!await TableExistsAsync(connection, duplicateTable, cancellationToken).ConfigureAwait(false))
                {
                    return CorruptedCompatibility($"Required duplicate analysis table {duplicateTable} is missing.");
                }
            }

            var normalizationVersion = Convert.ToInt32(
                await ReadScalarAsync(
                        connection,
                        "SELECT normalization_version FROM schema_info LIMIT 1;",
                        cancellationToken)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture);
            if (normalizationVersion > CurrentNormalizationVersion)
            {
                return NewerCompatibility(schemaVersion, normalizationVersion);
            }

            if (normalizationVersion < 0)
            {
                return CorruptedCompatibility("The normalization marker is invalid.");
            }

            if (normalizationVersion < CurrentNormalizationVersion)
            {
                return new(
                    IndexCompatibilityState.RequiresRescan,
                    schemaVersion,
                    normalizationVersion,
                    IsReadable: true,
                    CanWrite: true,
                    RequiresRescan: true,
                    "Index metadata is outdated вЂ” Rescan required.");
            }

            return CompatibleCompatibility();
        }
        catch (Exception exception) when (
            exception is SqliteException or
                InvalidOperationException or
                FormatException or
                OverflowException)
        {
            InfrastructureLog.IndexCorrupted(logger, resolvedPaths.DatabasePath, exception);
            return CorruptedCompatibility("Index could not be read. The database file was preserved. Back it up, then restore a known-good index or move it aside manually before creating a replacement.");
        }
    }

    public async Task<IndexDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!Compatibility.IsReadable)
        {
            return new(Compatibility, 0, null, UnrealReadinessSummary.Empty);
        }

        try
        {
            // Compatibility must be known before any connection can create or mutate the file.
            await using var connection = await OpenReadOnlyAsync(cancellationToken)
                .ConfigureAwait(false);
            var count = Convert.ToInt32(
                await ReadScalarAsync(connection, "SELECT COUNT(*) FROM assets;", cancellationToken)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture);
            var scan = await ReadScanMetadataAsync(connection, cancellationToken)
                .ConfigureAwait(false);
            var readiness = await ReadUnrealReadinessSummaryAsync(connection, cancellationToken)
                .ConfigureAwait(false);
            return new(Compatibility, count, scan, readiness);
        }
        catch (Exception exception) when (
            exception is SqliteException or
                InvalidOperationException or
                FormatException or
                OverflowException)
        {
            InfrastructureLog.IndexCorrupted(logger, resolvedPaths.DatabasePath, exception);
            Compatibility = CorruptedCompatibility(
                "Index could not be read. The database file was preserved. Back it up, then restore a known-good index or move it aside manually before creating a replacement.");
            return new(Compatibility, 0, null, UnrealReadinessSummary.Empty);
        }
    }

    private async Task PrepareWritableDatabaseAsync(CancellationToken cancellationToken)
    {
        // Re-inspect at the write boundary so stale startup state cannot bypass the safety gate.
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!Compatibility.CanWrite)
        {
            throw new InvalidOperationException(
                $"Index writes are blocked while compatibility state is {Compatibility.State}.");
        }

        if (Compatibility.State != IndexCompatibilityState.Missing)
        {
            return;
        }

        var directory = Path.GetDirectoryName(resolvedPaths.DatabasePath)
            ?? throw new InvalidOperationException("Database path has no parent directory.");
        Directory.CreateDirectory(directory);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureWritableConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        await CreateSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        Compatibility = CompatibleCompatibility();
    }

    private async Task MigrateKnownSchemaAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureWritableConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        var fromVersion = await ReadIntAsync(connection, "SELECT version FROM schema_info LIMIT 1;", cancellationToken)
            .ConfigureAwait(false);
        InfrastructureLog.IndexMigrationStarted(logger, resolvedPaths.DatabasePath, fromVersion, CurrentSchemaVersion);
        if (fromVersion == 1)
        {
            await MigrateVersionOneAsync(connection, cancellationToken).ConfigureAwait(false);
            fromVersion = 2;
        }
        if (fromVersion == 2)
        {
            await MigrateVersionTwoAsync(connection, cancellationToken).ConfigureAwait(false);
            fromVersion = 3;
        }
        if (fromVersion == 3)
        {
            await MigrateVersionThreeAsync(connection, cancellationToken).ConfigureAwait(false);
            fromVersion = 4;
        }
        if (fromVersion == 4)
        {
            await MigrateVersionFourAsync(connection, cancellationToken).ConfigureAwait(false);
            fromVersion = 5;
        }
        if (fromVersion == 5)
        {
            await MigrateVersionFiveAsync(connection, cancellationToken).ConfigureAwait(false);
            fromVersion = 6;
        }
        if (fromVersion == 6)
        {
            await MigrateVersionSixAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        InfrastructureLog.IndexMigrationCompleted(logger, resolvedPaths.DatabasePath, CurrentSchemaVersion);
    }

    private async Task<SqliteConnection> OpenReadOnlyAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = resolvedPaths.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task ConfigureWritableConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            connection,
            """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA foreign_keys = ON;
            """,
            cancellationToken).ConfigureAwait(false);

    private static async Task<bool> HasRequiredTablesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        foreach (var table in new[] { "schema_info", "assets", "tags", "asset_tags", "scan_state" })
        {
            if (!await TableExistsAsync(connection, table, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<object?> ReadScalarAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PersistedScanMetadata?> ReadScanMetadataAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT completed_at_utc, result_json FROM scan_state WHERE singleton_id = 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var completed = DateTimeOffset.Parse(
            reader.GetString(0),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        var json = reader.GetString(1);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty(
                    nameof(PersistedScanMetadata.LastSuccessfulScanUtc),
                    out _) &&
                JsonSerializer.Deserialize<PersistedScanMetadata>(json) is { } current)
            {
                return current;
            }
        }
        catch (JsonException)
        {
            // A pre-MLV-6 payload is attempted below.
        }

        try
        {
            var legacy = JsonSerializer.Deserialize<ScanResult>(json);
            return legacy is null
                ? null
                : new(
                    completed,
                    legacy.Elapsed,
                    ScanAttemptStatus.Succeeded,
                    legacy.AddedAssets,
                    legacy.UpdatedAssets,
                    legacy.RemovedAssets,
                    CountSkipped(legacy),
                    legacy.InaccessibleDirectories.Count);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int CountSkipped(ScanResult result) =>
        result.SkippedMalformedFiles +
        result.SkippedUnrelatedFiles +
        result.DuplicateGroups.Sum(group => group.SkippedCopyJsonPaths.Count);

    private static async Task<UnrealReadinessSummary> ReadUnrealReadinessSummaryAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT readiness_status, readiness_rule_version, readiness_evaluated_at_utc, COUNT(*)
            FROM assets
            GROUP BY readiness_status, readiness_rule_version, readiness_evaluated_at_utc;
            """;

        var ready = 0;
        var readyWithWarnings = 0;
        var notReady = 0;
        var notApplicable = 0;
        var unknown = 0;
        var requiresRecalculation = 0;
        DateTimeOffset? lastEvaluation = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var status = (UnrealReadinessStatus)reader.GetInt32(0);
            var version = reader.GetInt32(1);
            var count = reader.GetInt32(3);
            if (version != UnrealReadinessPolicy.CurrentRuleVersion || reader.IsDBNull(2))
            {
                requiresRecalculation += count;
            }

            if (!reader.IsDBNull(2))
            {
                var evaluated = DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                if (lastEvaluation is null || evaluated > lastEvaluation.Value)
                {
                    lastEvaluation = evaluated;
                }
            }

            switch (status)
            {
                case UnrealReadinessStatus.Ready:
                    ready += count;
                    break;
                case UnrealReadinessStatus.ReadyWithWarnings:
                    readyWithWarnings += count;
                    break;
                case UnrealReadinessStatus.NotReady:
                    notReady += count;
                    break;
                case UnrealReadinessStatus.NotApplicable:
                    notApplicable += count;
                    break;
                case UnrealReadinessStatus.Unknown:
                    unknown += count;
                    break;
            }
        }

        return new(
            ready,
            readyWithWarnings,
            notReady,
            notApplicable,
            unknown,
            requiresRecalculation,
            UnrealReadinessPolicy.CurrentRuleVersion,
            lastEvaluation);
    }

    private static IndexCompatibilityInfo CompatibleCompatibility() => new(
        IndexCompatibilityState.Compatible,
        CurrentSchemaVersion,
        CurrentNormalizationVersion,
        IsReadable: true,
        CanWrite: true,
        RequiresRescan: false,
        "Index is compatible.");

    private static IndexCompatibilityInfo MissingCompatibility() => new(
        IndexCompatibilityState.Missing,
        null,
        null,
        IsReadable: false,
        CanWrite: true,
        RequiresRescan: false,
        "No index exists. Run Rescan to create it.");

    private static IndexCompatibilityInfo NewerCompatibility(int schema, int? normalization) => new(
        IndexCompatibilityState.NewerVersionUnsupported,
        schema,
        normalization,
        IsReadable: false,
        CanWrite: false,
        RequiresRescan: false,
        "Index was created by a newer ScanVault version. Open it with a compatible ScanVault version; this application will not modify it.");

    private static IndexCompatibilityInfo CorruptedCompatibility(string guidance) => new(
        IndexCompatibilityState.Corrupted,
        null,
        null,
        IsReadable: false,
        CanWrite: false,
        RequiresRescan: false,
        guidance);
}
