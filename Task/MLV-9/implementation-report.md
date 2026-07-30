# MLV-9 Implementation Report

## Summary

Implemented persisted scan history and logical change detection for ScanVault. Every scan started through `LibraryScanService` now creates a `Running` scan run. Successful scans complete that run atomically with the SQLite catalog replacement, per-asset snapshots, classified change rows, `scan_state`, and retention cleanup. Cancelled and failed runs are finalized separately and are not eligible baselines.

## Changed files

- Core: scan history models, scan result extensions, `IAssetIndex` history methods, build-info provider abstraction.
- Infrastructure: SQLite schema v4, v3->v4 migration, scan run persistence, snapshot/change persistence, fingerprint v1, lifecycle integration in `LibraryScanService`.
- App: `Scan History` toolbar entry, `ScanHistoryWindow`, `ScanHistoryViewModel`, post-scan Added/Changed/Removed/Unchanged status summary, navigation to existing assets from history.
- Tests: infrastructure regression tests for initial baseline, Added/Changed/Removed/Unchanged, and cancelled-run baseline exclusion; updated fakes for new contracts.
- Docs: README, architecture, index format.

## Data schema

Schema version increased from 3 to 4. New tables:

- `scan_runs`: run identity, library identity/root, timestamps, status, app version, commit SHA, schema/normalization/fingerprint versions, counts, error, initial-baseline marker.
- `scan_asset_snapshots`: per completed run asset identity, summary, completeness, fingerprint.
- `scan_changes`: per completed run category, reason flags, previous/current paths and fingerprints.

Foreign keys cascade from `scan_runs` to snapshots and changes. Indexes cover library/time, status/time, scan run + kind, and asset identity.

## Migration path

Existing v1/v2 migrations remain. v3 now reports `RequiresMigration`; `MigrateKnownSchemaAsync` adds v4 history tables transactionally and updates `schema_info.version` to 4. Existing catalog rows are preserved. Existing populated old normalization markers remain readable and still require Rescan.

## Identity strategy

- Library identity: normalized absolute library root path.
- Asset identity: non-empty Megascans asset ID; fallback to normalized relative JSON path.
- Display name is not used as identity.
- Duplicate ID handling remains the existing deterministic winner policy before persistence.

## Fingerprint

Fingerprint version: `1`.

The SHA-256 payload includes stable metadata projection, relative paths, resolution, serialized content inventory, completeness, and file facts for known JSON/preview/inventory paths. File facts include normalized relative path, size, and last-write UTC. Binary file contents are not read.

## Transaction strategy

Completed scan history is persisted inside `SqliteAssetIndex.ReplaceLibraryAsync` using the same SQLite transaction as asset upsert, stale deletion, `scan_state`, and normalization marker promotion. Cancellation/failure before commit preserves the previous completed baseline.

## UI changes

Main toolbar now has `Scan History`. The window shows scan runs newest-first and change details filtered by Added/Changed/Removed/Unchanged. Large grids use WPF DataGrid virtualization. Existing assets can be opened from history; removed assets keep navigation disabled.

Post-scan status text now includes Added/Changed/Removed/Unchanged and marks initial baseline.

## Retention

After each completed scan, retention keeps the last 20 completed runs per library and last 20 failed/cancelled runs per library. Deleted runs cascade related snapshots and changes. The current completed run is excluded from deletion.

## Tests

Added coverage:

- initial baseline creates Added changes;
- subsequent scan classifies Added, Changed, Removed, Unchanged;
- cancelled scan run does not become baseline;
- existing scan service and ViewModel fakes updated for run lifecycle.

## Validation results

- `dotnet restore ScanVault.sln`: passed.
- `dotnet build ScanVault.sln --configuration Release --no-restore`: passed, 0 warnings/errors.
- `dotnet test ScanVault.sln --configuration Release --no-build`: passed, 99 tests.
- `dotnet format ScanVault.sln --verify-no-changes`: passed after running `dotnet format ScanVault.sln` once.
- `git diff --check`: passed.
- CRG post-update: passed, 583 rows indexed; risk score 0.55.
- CRG detect-changes from `master`: passed; noted untested WPF startup/logging proximity.
- Graphify update: passed; graph rebuilt to 1189 nodes / 2598 edges / 41 communities; warning persisted for `global.json` zero-node extraction.

## Performance measurement

Manual before/after scan timing over a real library was not executed in this environment. The implementation avoids binary hashing and reuses scan-time indexed data plus file metadata checks. Automated tests use temporary fixtures, not a large real library, so they are not a meaningful performance sample.

## Known limitations

- No binary diff or content hashing of FBX/ABC/textures.
- Change reasons are grouped, not line-by-line JSON diffs.
- `Unchanged` is persisted and loaded with the same query path as other categories; very large histories rely on grid virtualization and SQL limit.
- Manual two-rescan validation, cancelled/failed GUI scenarios, migration on a copied user SQLite DB, and GitHub Actions were not executed locally.

## Blast radius

Direct: Core contracts/models, SQLite schema/migration/persistence, scan lifecycle, WPF main window/history window, tests/docs.

Adjacent: diagnostics compatibility flow, app DI, scan status text, existing migration tests, existing scan service tests.

## Graphify / CRG status

Preflight and post-change CRG/Graphify were executed. Graph findings were validated against source and tests before implementation decisions.
