# Review MLV-12

Implemented Duplicate Detection as a read-only analysis flow.

## Completed

- Added duplicate-analysis models, confidence/category enums, progress, run summaries, group members, reasons, and hash status.
- Added deterministic classification for exact ID, conflicting ID, exact content, probable, and partial duplicate groups.
- Added SQLite schema v5 migration and persistence for file hash cache and duplicate analysis runs/groups/reasons.
- Added streaming SHA-256 candidate hashing with cache reuse by size, timestamp, algorithm, and version.
- Added stale marking after successful Rescan.
- Added Duplicate Detection WPF window with summary, filters, groups, details, run/cancel, open folder/asset, compare selected pair, and export group.
- Integrated comparison handoff through the existing MLV-10 `AssetComparisonViewModel` path.
- Added tests for classification, order independence, low-confidence not exact, persistent hash reuse, and stale state.

## Validation

- Restore: passed with approved network access.
- Build: `dotnet build ScanVault.sln --configuration Release --no-restore -m:1 -v:minimal` passed.
- Tests: `dotnet test ScanVault.sln --configuration Release --no-build -m:1 -v:minimal` passed.
- Diff check: `git diff --check` passed, with CRLF conversion warnings only.
- CRG post-change update: passed.
- Graphify post-change update: failed with `[WinError 5] Access is denied`.

## Notes

- The feature does not delete, move, or modify Megascans source files.
- Review follow-up fixed skipped same-ID copy analysis by adding dedicated persisted duplicate-analysis sources outside the normal browsing `assets` table.

## Review Findings Follow-up - 2026-08-17

- Persisted all parsed physical asset copies for duplicate analysis in schema v6 table `duplicate_analysis_sources`; normal browsing still keeps the deterministic same-ID winner only.
- Made duplicate analysis read those persisted source candidates, so exact and conflicting same-ID groups include both physical copies after a real scan commit path.
- Added `json_path` to duplicate group members and used it for UI open/compare identity.
- Blocked exact classification when any required hash is missing, failed, empty, or not required; `Computed` and `CacheHit` remain valid exact evidence.
- Split `Open asset` from `Open folder`: asset selects the metadata JSON file, folder opens the asset directory.
- Added cancellation and failed-run regressions proving previous completed results remain current, partial groups are not persisted for non-completed runs, source files are untouched, and a later successful run becomes latest.

## Follow-up Validation

- `dotnet restore ScanVault.sln -v:minimal`: passed with approved execution outside sandbox; all projects up-to-date.
- `dotnet build ScanVault.sln --configuration Release --no-restore -m:1 -v:minimal`: passed.
- `dotnet test ScanVault.sln --configuration Release --no-build -m:1 -v:minimal`: passed.
- `git diff --check`: passed, with CRLF conversion warnings only.
- CRG update: passed.
- Graphify update: failed with `[WinError 5] Access is denied`.
