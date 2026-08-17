# Review MLV-13

## Completed

- Added Unreal Engine readiness evaluation with stable `UE_*` reason codes, severities, rule versioning, and explanations.
- Persisted readiness in SQLite schema v7 and recomputed it only as part of successful index replacement.
- Added readiness to filters, asset cards, details, smart collections, comparison, exports, diagnostics, and scan history change detection.
- Added focused rule and integration tests.

## Validation

- `dotnet restore ScanVault.sln` passed.
- `dotnet build ScanVault.sln --configuration Release --no-restore` passed.
- `dotnet test ScanVault.sln --configuration Release --no-build` passed.
- Result: 156 passed, 0 failed, 0 skipped.

## Notes

- Graphify refresh was attempted but failed with `[WinError 5] Access is denied`.
- The feature remains read-only and does not launch Unreal Engine or touch source Megascans assets.

## Review Fix Pass

Addressed the MLV-13 review findings in the same `master` branch.

- `ReplaceLibraryAsync` now forces fresh readiness evaluation from current inventory for persisted assets and duplicate-analysis source copies.
- Added SQLite integration coverage for v6 to v7 migration, stale migrated readiness, restart persistence, failed replacement rollback, cancellation rollback, stale rule-version refresh, history timestamp stability, and a 160-asset deterministic batch.
- Confirmed v6 migrated rows are preserved as stale `Unknown` rather than silently marked current.
- Confirmed `EvaluatedAtUtc` does not cause semantically identical scans to appear changed.

Validation:

- `dotnet restore ScanVault.sln -v:minimal`: passed.
- `dotnet build ScanVault.sln --configuration Release --no-restore -m:1 -v:minimal`: passed.
- `dotnet test ScanVault.sln --configuration Release --no-build -m:1 -v:minimal`: passed.
- Test counts: Core 78, Infrastructure 56, App 31; total 165 passed.
- `git diff --check`: passed.
- CRG update and detect-changes passed.
- Graphify refresh failed with `[WinError 5] Access is denied`.
- GitHub Actions was not verified because `gh` is not authenticated.
