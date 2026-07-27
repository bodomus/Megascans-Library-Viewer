# MLV-6 — Implementation plan

## Objective

Add a compact, testable About / Diagnostics experience and an explicit SQLite compatibility state machine that inspects before writing, preserves unsupported/corrupted files, exposes persisted scan metadata, and blocks unsafe Rescan operations.

## Steps

1. Add Core models and formatting:
   - `IndexCompatibilityState` and immutable compatibility facts;
   - persisted scan metadata and current scan-attempt status;
   - complete diagnostics snapshot and stable field formatter;
   - extend `IAssetIndex` with current compatibility and diagnostics inspection.
2. Refactor `SqliteAssetIndex` initialization:
   - read-only existence/integrity/schema/normalization inspection;
   - no file/directory creation for Missing;
   - retain transactional v1 migration only;
   - map newer versions and corruption explicitly;
   - log state and guidance without destructive recovery.
3. Gate all writes:
   - allow Missing, Compatible, and RequiresRescan;
   - migrate known v1 before normal writes;
   - throw a stable safety exception for NewerVersionUnsupported/Corrupted;
   - preserve bytes and avoid write-mode connections for blocked states.
4. Improve `scan_state` payload inside the successful full-scan transaction:
   - save actual add/update/remove values;
   - save successful status, elapsed duration, skipped and inaccessible totals;
   - expose the persisted row through index diagnostics;
   - read legacy result JSON gracefully.
5. Add App diagnostics composition:
   - capture MLV-5 build information, settings/UI state, paths, index status, and latest attempt;
   - add a testable `DiagnosticsViewModel` with graceful copy command;
   - add structured compatibility and copy-failure logs.
6. Add compact WPF `DiagnosticsWindow` and toolbar entry; keep code-behind limited to window lifecycle.
7. Update MainViewModel startup and scan flow:
   - concise state-specific startup status;
   - block Rescan for unsafe states;
   - record success/cancel/failure attempt status and duration;
   - refresh diagnostics compatibility after successful scan.
8. Add/update Core, Infrastructure, and App/WPF tests for every required state and behavior using temporary files/directories only.
9. Update `README.md`, `Docs/architecture.md`, `Docs/index-format.md`, and add `Docs/diagnostics.md`.
10. Run targeted tests, full Release restore/build/test, formatter, whitespace, and focused manual-safe checks.
11. Update CRG and inspect changed symbols/dependants/write paths. Refresh Graphify because Core/Infrastructure/App relationships and an App entry point change, while preserving the unrelated `.graphifyignore`.
12. Create implementation/review reports, commit only MLV-6 files to `master`, update YouTrack, and attach exact `Tickets/MLV-6.md`.

## Acceptance mapping

- About/Diagnostics UI and copy: steps 5–6 and App tests.
- Required fields/stable formatting/no secrets: steps 1, 5, and formatter tests.
- Explicit compatibility states/read-only startup: steps 1–3 and Infrastructure tests.
- Newer/corrupted preservation and write blocking: step 3 and byte-preservation tests.
- Startup action status and command safety: step 7 and view-model tests.
- Persisted last successful scan metadata: step 4 and restart/readback tests.
- Documentation, validation, graphs, and artifacts: steps 9–12.

## Explicit non-goals

No backup/restore, database repair, silent deletion/replacement, cloud upload, telemetry, crash service, update checker, GitHub integration, release/publish workflow, installer, Unreal integration, asset inventory, or visual redesign.
