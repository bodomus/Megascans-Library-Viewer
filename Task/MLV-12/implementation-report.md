# MLV-12 Implementation Report

## Summary

Implemented read-only duplicate analysis with Core models/classification, SQLite schema v5 persistence, streaming SHA-256 hash cache, an infrastructure analysis service, WPF Duplicate Detection window, and focused tests.

## Repository

- Root: `J:/Projects/UE_Projects/Megascans Library Viewer`
- Branch: `master`
- Initial commit: `4bf6f5680365637ba5197f6a6eeb7b594728f0ee`
- Initial working tree: clean.

## Graph Status

- Graphify preflight: `graphify query "ScanVault asset identity content inventory fingerprints file metadata SQLite migrations duplicate detection UI entry points tests" --budget 4000` succeeded and identified asset models, content inventory, scan history fingerprints, SQLite index, MLV-10 comparison, MLV-11 export, and UI entry points.
- CRG preflight: `code-review-graph update --brief` updated 2 files but first hit a cp1251 panel rendering error; rerun with `PYTHONIOENCODING=utf-8` succeeded.
- CRG post-change: `code-review-graph update --brief` succeeded; 12 files updated, 29 changed symbols, risk score 0.55. Reported UI/logging test gaps for `ApplicationLog`, `MainWindow`, `OnDuplicateDetectionClick`, and `MainViewModel`.
- Graphify post-change refresh: `graphify update .` and `graphify update src` both failed with `[WinError 5] Access is denied`. Existing preflight graph and CRG were used with direct source/build/test validation.

## Changes

- Added duplicate-analysis domain models and deterministic `DuplicateAnalysisPolicy`.
- Added `IDuplicateAnalysisService` and duplicate/hash persistence members to `IAssetIndex`.
- Added SQLite schema v5 tables:
  - `file_hash_cache`
  - `duplicate_analysis_runs`
  - `duplicate_groups`
  - `duplicate_group_members`
  - `duplicate_reasons`
- Added v4 to v5 migration without deleting the existing index.
- Added `DuplicateAnalysisService` with candidate selection, streaming SHA-256 hashing, cache reuse by path/size/timestamp/version, cancellation, and bounded hash parallelism.
- Rescan now marks completed duplicate analysis runs stale inside the successful index replacement transaction.
- Added `DuplicateAnalysisWindow` and `DuplicateAnalysisViewModel` with summary, filters, groups, details, cancellation, open folder/asset, compare selected pair, and Markdown group export to clipboard.
- Registered the duplicate analysis service in DI and added a main-window entry point.
- Added Core and Infrastructure tests for classification, order independence, partial confidence, hash cache reuse after restart, and stale state.

## Measurements

Synthetic integration validation:

- Asset count: 2
- Candidate count: 2
- First run files hashed: 2
- First run bytes hashed: 6
- First run cache hit rate: 0%
- Restarted run files hashed: 0
- Restarted run cache hits: 2
- Restarted run cache hit rate: 100%
- Duration: covered by persisted run model; exact duration is environment-dependent.
- Peak memory: not measured in automated tests.

Real library volume was not measured because tests must not depend on the user's real library and no manual UI run was performed.

## Validation

- `dotnet restore ScanVault.sln -v:minimal`: passed after approved network access.
- `dotnet build ScanVault.sln --configuration Release --no-restore -m:1 -v:minimal`: passed.
- `dotnet test ScanVault.sln --configuration Release --no-build -m:1 -v:minimal`: passed.
  - Core: 60 passed.
  - Infrastructure: 43 passed.
  - App: 29 passed.
- `git diff --check`: passed; only CRLF conversion warnings from Git.

## Risks

- UI entry point and WPF layout were build-validated but not manually exercised.
- Group export is implemented as Markdown clipboard export, not a full MLV-11 file export dialog.
- Graphify post-change refresh failed with access denied, so architecture refresh evidence is degraded.

## Review Findings Fix - 2026-08-17

Addressed the attached MLV-12 review findings in-place on `master`.

- Added dedicated duplicate-analysis source persistence in SQLite schema v6. Successful scan replacement now writes all parsed physical asset copies to `duplicate_analysis_sources` while the normal `assets` table still keeps the deterministic same-ID winner.
- Updated `LibraryScanService` to inventory all parsed physical copies before committing, so skipped same-ID copies retain content inventory, mesh/texture/unclassified paths, completeness, and metadata needed by duplicate analysis.
- Updated `DuplicateAnalysisService` to read persisted source candidates via `GetDuplicateAnalysisSourcesAsync`, with fallback to current assets for older/empty source data.
- Added `json_path` to duplicate group members and persistence so UI actions and comparison can distinguish two physical copies with the same asset ID.
- Hardened exact duplicate classification: exact ID/content groups now require every participating file hash to be valid `Computed` or `CacheHit`; missing, failed, empty, or not-required hash evidence cannot produce exact confidence.
- Split duplicate-analysis UI actions: `Open folder` opens the asset folder, while `Open asset` selects the metadata JSON file through `IAssetInteractionService.OpenFile`.
- Added `IDuplicateContentHasher` seam with the existing SHA-256 implementation for deterministic cancellation/failure regression tests.
- Added regression tests for persisted same-ID source analysis, conflicting same-ID source analysis, invalid-hash exact prevention, computed/cache-hit exact acceptance, UI open/compare resolution by JSON path, cancelled run persistence, failed run persistence, and subsequent successful recovery.

### Review Validation

- `dotnet restore ScanVault.sln -v:minimal`: passed with approved execution outside sandbox; all projects up-to-date.
- `dotnet build ScanVault.sln --configuration Release --no-restore -m:1 -v:minimal`: passed with 0 warnings and 0 errors.
- `dotnet test ScanVault.sln --configuration Release --no-build -m:1 -v:minimal`: passed.
  - Core: 64 passed.
  - Infrastructure: 47 passed.
  - App: 31 passed.
- `git diff --check`: passed; Git reported CRLF conversion warnings only.
- `code-review-graph update --brief`: passed; CRG status after update is 1041 nodes, 2915 edges, 126 files, last updated 2026-08-17T07:59:05.
- `graphify update .`: failed with `[WinError 5] Access is denied`, same local refresh blocker as the original implementation report.
