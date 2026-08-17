# MLV-13 Implementation Report

## Summary

Implemented read-only, rule-based Unreal Engine readiness evaluation for indexed assets.

## Main Changes

- Added Core readiness models:
  - `UnrealReadinessStatus`
  - `UnrealReadinessSeverity`
  - `UnrealReadinessRuleCode`
  - `UnrealReadinessReason`
  - `UnrealReadinessEvaluation`
  - `UnrealReadinessSummary`
- Added deterministic `UnrealReadinessPolicy` with `CurrentRuleVersion = 1`.
- Added `AssetSummary.UnrealReadiness`.
- Added common query filters for UE statuses and common readiness reasons.
- Added SQLite schema v7 readiness columns and readiness JSON persistence.
- Recomputed readiness before successful `ReplaceLibraryAsync` transaction commit.
- Added diagnostics readiness summary and stale rule-version detection.
- Added readiness to scan-history fingerprint semantics without including `EvaluatedAtUtc`.
- Added asset card UE badge, tooltip, hover details, and inventory-window readiness tab.
- Added smart collection readiness criteria and built-ins:
  - UE Ready Assets
  - UE Ready With Warnings
  - Not UE Ready
  - UE Missing Mesh
  - UE Missing LODs
- Added comparison overview rows:
  - UE Readiness
  - UE Blocking Count
  - UE Warning Count
  - UE Readiness Reasons
- Added report schema v2 catalog columns:
  - `UnrealReadinessStatus`
  - `ReadinessRuleVersion`
  - `BlockingIssueCount`
  - `WarningCount`
  - `ReadinessReasons`

## Rule Scope

Rules are explicit, deterministic, and use only indexed data:

- asset type
- content inventory
- normalized texture map types
- mesh formats
- LOD chain data
- inventory issue codes

The implementation does not launch Unreal Engine, read binary file contents, create `.uasset` files, convert assets, or modify the source Megascans library.

## Measurements

Validation used synthetic test fixtures rather than a real user library.

- Asset count in measured test runs: synthetic unit/integration fixtures.
- Evaluation duration: logged during successful index replacement through `InfrastructureLog.UnrealReadinessEvaluated`.
- Assets per second: logged with the same event.
- Ready, warning, not-ready, and unknown counts: exposed in diagnostics and logged for persisted scans.

## Validation

- `dotnet restore ScanVault.sln` passed.
- `dotnet build ScanVault.sln --configuration Release --no-restore` passed.
- `dotnet test ScanVault.sln --configuration Release --no-build` passed.
- Final test count: 156 passed, 0 failed, 0 skipped.

## Review Fix Pass

Addressed review findings from `MLV-13-fix-review-findings.md`.

### Forced Readiness Recompute

`SqliteAssetIndex.ReplaceLibraryAsync` now always evaluates readiness from the current `AssetSummary.Content` in the authoritative successful replacement path:

- indexed assets use `UnrealReadinessPolicy.Evaluate(asset, evaluatedAtUtc)`;
- MLV-12 duplicate-analysis source snapshots use the same fresh evaluation rule;
- one shared `evaluatedAtUtc` is used for the batch.

`EnsureCurrent()` remains available for lazy non-authoritative use, but persistence no longer trusts an attached current-version readiness object.

### Added Infrastructure Coverage

Added `SqliteUnrealReadinessTests` covering:

- changed inventory with stale current-version readiness is recomputed before persistence;
- duplicate-analysis source copies are recomputed before persistence;
- v6 to v7 migration preserves existing index rows and initializes readiness as stale `Unknown`;
- migrated stale rows report diagnostics recalculation required;
- successful replacement persists readiness across a reopened `SqliteAssetIndex`;
- failed replacement rolls back and preserves previous readiness;
- cancelled replacement preserves previous readiness;
- stale rule version is detected and refreshed by successful replacement;
- scan history records readiness changes and ignores `EvaluatedAtUtc`-only changes;
- deterministic mixed-status batch of 160 assets persists expected counts.

### Migration Behavior

The v6 to v7 migration initializes readiness columns conservatively:

- `readiness_json`: Unknown, rule version 0, no evaluation timestamp;
- `readiness_status`: `Unknown`;
- `readiness_rule_version`: `0`;
- `readiness_blocking_count`: `0`;
- `readiness_warning_count`: `0`;
- `readiness_evaluated_at_utc`: `NULL`.

Migrated rows are readable but stale until a controlled successful replacement/rescan recomputes readiness.

### Fix-Pass Validation

- `dotnet restore ScanVault.sln -v:minimal`: passed.
- `dotnet build ScanVault.sln --configuration Release --no-restore -m:1 -v:minimal`: passed.
- `dotnet test ScanVault.sln --configuration Release --no-build -m:1 -v:minimal`: passed.
- Test counts:
  - `ScanVault.Core.Tests`: 78 passed, 0 failed, 0 skipped.
  - `ScanVault.Infrastructure.Tests`: 56 passed, 0 failed, 0 skipped.
  - `ScanVault.App.Tests`: 31 passed, 0 failed, 0 skipped.
  - Total: 165 passed, 0 failed, 0 skipped.
- `git diff --check`: passed.
- `code-review-graph update --brief`: passed.
- `code-review-graph detect-changes --base HEAD --brief`: passed.
- `graphify update .`: failed with `[WinError 5] Access is denied`.
- GitHub Actions status: not verified; `gh` is not authenticated in this environment.
- Manual UI validation: not performed in this fix pass.

## Code Intelligence

- `code-review-graph update --brief` passed after setting `PYTHONIOENCODING=utf-8`.
- `code-review-graph detect-changes --base HEAD --brief` passed.
- `graphify update .` failed with `[WinError 5] Access is denied`; source and tests were used as final authority.

## Risks

- Readiness rules are conservative and target typical manual Unreal Engine import, not a guaranteed import result.
- Real Megascans libraries may contain filename conventions not represented in current deterministic fixtures; those should surface as `Unknown`, `Not UE Ready`, or warnings rather than false `UE Ready`.
- UI density increased in the filter bar; the row now wraps to avoid clipping.
- GitHub Actions must be checked after the corrected commit is pushed by an authenticated environment.
