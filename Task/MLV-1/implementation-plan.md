# MLV-1 Implementation Plan

## Scope

Deliver the first usable ScanVault desktop application and the repository
conventions required by MLV-1. Work remains Windows-only and does not alter any
Megascans source file.

## Confirmed foundations

- Ticket identifier and artifact folder: `MLV-1`.
- Code root: `src/`.
- Report root: `review/`.
- Runtime: .NET 10 WPF, stable C# 14.
- Persistence: `Microsoft.Data.Sqlite`, explicit SQL schema and transactions;
  no generic repository abstraction and no EF Core.
- Dependency injection and logging: Microsoft Extensions packages.
- Tests: xUnit, temporary directories, generated JSON fixtures.
- User data: `%LocalAppData%\ScanVault` for index/cache and
  `%AppData%\ScanVault` for settings.

## Phase 1 — repository and workflow bootstrap

1. Add `.gitignore` for build, graph, user-data, database, and IDE artifacts.
2. Add `global.json` selecting SDK `10.0.301` with safe feature-band roll-forward.
3. Create `ScanVault.sln` project membership and shared build settings.
4. Adapt `AGENTS.md` and `.codex/PRE_TICKET_WORKFLOW.md` to .NET/WPF/SQLite.
5. Place adapted graph skills under `.agents/skills/` as required by the ticket.
6. Adapt the repository-local benchmark agent descriptions away from Unreal.
7. Add the local ticket specification and maintained documentation.

## Phase 2 — Core

1. Add immutable asset, tag, settings, scan progress/result, folder, and query
   models.
2. Define scanner, parser, index, settings, image resolver, and clock contracts.
3. Implement settings validation, tag normalization, folder-descendant filtering,
   duplicate selection, preview priority, and scan aggregation policies.
4. Keep all path comparisons explicit and Windows-aware.

## Phase 3 — Infrastructure

1. Implement recursive async discovery with cancellation, reparse-point
   avoidance, deterministic ordering, and per-directory failure reporting.
2. Implement tolerant `JsonDocument` parsing for legacy schema variants.
3. Implement metadata-first thumbnail and preview resolution without decoding
   texture maps.
4. Implement versioned SQLite schema, indexes, transactional full-scan staging,
   upsert, stale removal, and rollback preservation.
5. Implement per-user JSON settings storage with atomic replacement.
6. Implement the orchestration service that scans, reports phases, resolves
   duplicates, and commits only after successful completion.

## Phase 4 — WPF application

1. Add a DI/logging composition root and explicit application lifetime cleanup.
2. Implement focused view models for shell, settings, folder navigation, assets,
   hover coordination, preview, and scan state.
3. Build a single main window with toolbar, folder tree, virtualizing/wrapping
   asset list, status bar, hover popup, and overlay lightbox.
4. Implement the Settings window with `OpenFolderDialog`, validation, explicit
   save, rescan, and cancellation.
5. Implement asynchronous image loading with `BitmapCacheOption.OnLoad`, frozen
   bitmaps, request invalidation, failure fallback, and a bounded LRU cache.
6. Load the existing index at startup; never trigger an automatic scan.

## Phase 5 — tests and documentation

1. Add Core tests for policies, validation, filtering, and aggregation.
2. Add Infrastructure tests for parser variation, recursion, cancellation,
   resolver priority, SQLite creation/version/upsert/removal/rollback, and
   duplicate IDs.
3. Add view-model tests where behavior is independent of WPF rendering.
4. Add sanitized small fixtures and no copyrighted binary assets.
5. Complete `README.md`, `Docs/architecture.md`, and `Docs/index-format.md`.

## Phase 6 — post-change intelligence and validation

1. Update CRG and inspect architecture, changed files, impact radius, flows,
   disconnected code, and related tests.
2. Run targeted test projects, then Release solution build and all tests.
3. Run `dotnet --info`, restore, build, and test commands exactly as recorded.
4. Refresh Graphify because the application architecture is newly established.
5. Perform available launch/manual checks without claiming unexecuted UI steps.
6. Produce `Task/MLV-1/implementation-report.md` and
   `review/review-MLV-1.md`.
7. Update YouTrack fields and add a completion summary.

## Acceptance checkpoints

- No filesystem scan, SQLite operation, JSON parse, or image decode blocks the UI
  thread.
- Cancellation before commit preserves the prior index.
- A malformed or inaccessible asset cannot abort the complete scan.
- Folder selection includes descendants and root includes all assets.
- Thumbnail and preview resolution follows the ticket priority order.
- Source images are never copied, modified, or held open.
- Simultaneous scans are prevented.
- Build and tests must actually pass before completion is claimed.

## Confirmed implementation policies

- Duplicate-ID winner: lexicographically smallest normalized full JSON path;
  reports include the winner and every skipped copy path.
- Stale rows: delete only in a successful full-scan transaction.
- Skill layout: `.agents/skills/` only; remove obsolete top-level `skills/`.
- Git branch: retain `master`.

