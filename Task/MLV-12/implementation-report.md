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

- Existing normal scan still stores only the winning asset for same-ID duplicates in the main `assets` table; skipped same-ID copies from `DuplicateAssetResolver` are not fully indexed members for deep conflicting-content analysis.
- UI entry point and WPF layout were build-validated but not manually exercised.
- Group export is implemented as Markdown clipboard export, not a full MLV-11 file export dialog.
- Graphify post-change refresh failed with access denied, so architecture refresh evidence is degraded.
