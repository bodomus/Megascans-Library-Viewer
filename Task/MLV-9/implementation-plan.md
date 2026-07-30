# MLV-9 Implementation Plan

## Architecture

Implement scan history as a versioned extension of the existing SQLite index, not as UI-owned SQL and not as a separate file. Core remains free of WPF/SQLite; Infrastructure owns persistence; App owns window lifecycle and binding.

## Data model

Add Core records/enums:

- `ScanRunStatus`: `Running`, `Completed`, `Cancelled`, `Failed`.
- `AssetChangeKind`: `Added`, `Changed`, `Removed`, `Unchanged`.
- `AssetChangeFlags`: `Metadata`, `Path`, `Resolution`, `Inventory`, `Files`, `Completeness`.
- `ScanRunSummary` and `ScanChangeSummary` for read models.
- Scan result extension properties for changed/unchanged/initial baseline/scan run id.

Add SQLite schema v4:

- `scan_runs` with run status, identity, versions, counts, error message, initial-baseline marker.
- `scan_asset_snapshots` with per-run asset identity, display summary, path, completeness, fingerprint.
- `scan_changes` with per-run category, change flags, previous/current path, summary fields.

Foreign keys cascade from `scan_runs` to snapshots and changes. Index by library identity, status, start time, scan run, and asset identity.

## Scan lifecycle

- `LibraryScanService.ScanAsync` starts a `Running` scan run through `IAssetIndex` immediately after root normalization.
- Stale `Running` rows for that library are marked `Failed` before the new run starts.
- On success, `ReplaceLibraryAsync` receives the scan run id and completes it atomically with asset replacement, snapshot persistence, change rows, and retention cleanup.
- On cancellation/failure before completed commit, `LibraryScanService` marks the run `Cancelled`/`Failed` using `CancellationToken.None` and rethrows.
- Only `Completed` rows with the current fingerprint version are eligible baselines.

## Fingerprint strategy

Fingerprint version: `1`.

Build a stable JSON payload and SHA-256 hash from:

- asset identity, normalized relative paths, name, type/raw type, categories, tags, biome/region/size, resolution, texel density, color, metadata last-write UTC;
- full structured `AssetContentInventory` projected into deterministic order;
- physical file facts for JSON, thumbnail/preview when present, inventory mesh/texture/unclassified/issue paths: normalized relative path, file size, last-write UTC if file exists.

Do not read binary payload contents.

Change flags compare previous/current snapshot summaries and fingerprints; minimum groups: metadata, path, resolution, inventory, files, completeness.

## UI

- Add `Scan History` entry point near existing top actions.
- Add `ScanHistoryWindow` with run list and change-detail tabs/filters for Added/Changed/Removed/Unchanged.
- Use WPF virtualization on change grids.
- Add post-scan summary in status text and expose a `View changes`/history command path without modal interruption.
- Navigation from a change row selects an existing asset when present; removed rows disable the command.

## Retention

After each completed scan run:

- keep last 20 completed runs per library;
- keep last 20 failed/cancelled runs per library;
- delete old runs with cascading snapshots/changes;
- do not delete the current completed run.

## Tests

Add focused Core tests for fingerprint/change detection, Infrastructure integration tests for schema v4, initial baseline, added/removed/changed/unchanged, cancellation/failure not baseline, retention, migration v3 -> v4, and App ViewModel tests for history sorting/empty states/navigation disabled for removed rows.

## Documentation and reports

Update README and Docs index/architecture/diagnostics with history, fingerprint semantics, initial baseline and limitations. Create `Task/MLV-9/implementation-report.md` and `review/review-MLV-9.md` after implementation.

## Validation

Run:

```powershell
dotnet restore ScanVault.sln
dotnet build ScanVault.sln --configuration Release --no-restore
dotnet test ScanVault.sln --configuration Release --no-build
dotnet format ScanVault.sln --verify-no-changes
git diff --check
```

Manual real scan validation depends on an available sample library; if unavailable, record as not executed rather than claiming it passed.
