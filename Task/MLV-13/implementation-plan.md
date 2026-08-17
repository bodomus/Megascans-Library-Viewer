# MLV-13 Implementation Plan

## Rules and Models

1. Add Unreal readiness models to Core:
   - status, severity, stable rule codes, reason, evaluation, and summary.
   - `ReadinessRuleVersion` constant owned by an explicit policy.
2. Add `UnrealReadiness` to `AssetSummary` with `Unknown` as the safe default.
3. Implement deterministic `UnrealReadinessPolicy` using only indexed inventory data.

## Persistence and Recompute

1. Move SQLite schema from version 6 to 7.
2. Add persisted readiness columns and readiness JSON to `assets`.
3. Recompute readiness in batch before `ReplaceLibraryAsync` persists a successful scan.
4. Keep failed or partial scans transactional by relying on the existing replacement transaction.
5. Surface stale readiness through diagnostics when stored rule versions differ from the current policy.

## Integrations

1. Add common-pipeline filters for readiness status, missing mesh, missing normal, missing LODs, blocking issues, and warnings.
2. Add asset card badge and details fields with status, counts, rule version, and reasons.
3. Add MLV-8 smart collections for UE readiness.
4. Add MLV-10 comparison rows for readiness, counts, and reasons.
5. Add MLV-11 export columns.
6. Add MLV-9 readiness change history flag.
7. Add diagnostics summary and formatting.

## Tests

1. Core rule tests for ready/not-ready/warning/unknown/not-applicable cases, deterministic order, version invalidation, and summary counts.
2. Persistence tests for migration and restart persistence.
3. Policy/view-model tests for filters, smart collections, comparison, exports, diagnostics, and badges.
4. Validation:
   - `dotnet restore ScanVault.sln`
   - `dotnet build ScanVault.sln --configuration Release --no-restore`
   - `dotnet test ScanVault.sln --configuration Release --no-build`

## Completion

1. Run post-change CRG impact inspection.
2. Refresh Graphify if architectural shape changed and the tool can update on this machine.
3. Create `Task/MLV-13/implementation-report.md`.
4. Create `review/review-MLV-13.md`.
5. Update YouTrack fields when possible.

## Review Fix Pass

1. Force fresh readiness evaluation in the authoritative SQLite replacement path.
2. Add focused Infrastructure SQLite tests for:
   - changed inventory with stale current-version readiness;
   - v6 to v7 migration preserving index rows as stale `Unknown`;
   - restart persistence;
   - failed and cancelled replacement rollback;
   - rule-version stale diagnostics and replacement refresh;
   - readiness history timestamp stability;
   - deterministic mixed-status batch persistence.
3. Re-run required validation with minimal verbosity and record exact project counts.
