# Review MLV-9

## Result

Implemented scan history and change detection for ScanVault on branch `codex/MLV-9-scan-history`.

## What changed

- Added persistent `scan_runs`, `scan_asset_snapshots`, and `scan_changes` in SQLite schema v4.
- Added scan lifecycle states: `Running`, `Completed`, `Cancelled`, `Failed`.
- Added logical fingerprint v1 and classification for `Added`, `Changed`, `Removed`, `Unchanged`.
- Added initial baseline handling.
- Added retention for last 20 completed and bounded failed/cancelled runs per library.
- Added `Scan History` window and toolbar entry.
- Added post-scan summary in the main status text.
- Added tests for baseline/change classification and cancelled-run exclusion.
- Updated README and docs.

## Validation

- Restore: passed.
- Build: passed.
- Tests: passed, 99 total.
- Format verification: passed.
- `git diff --check`: passed.
- CRG update/detect-changes: passed.
- Graphify update: passed.

## Not executed

- Manual GUI two-rescan scenario on a copied real library.
- Manual cancelled/failed GUI scan scenarios.
- Performance before/after measurement on a large real library.
- GitHub Actions after push/PR.

## Notes

MLV-9 detects logical changes from indexed metadata, content inventory and file properties. It does not compare binary file contents.
