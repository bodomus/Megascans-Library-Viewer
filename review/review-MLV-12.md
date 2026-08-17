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
- Exact duplicate behavior works for assets present in the current index. Because the legacy scan path still drops skipped same-ID copies from the main indexed asset list, full conflicting-content analysis for those skipped copies remains limited until duplicate copies are indexed separately.
