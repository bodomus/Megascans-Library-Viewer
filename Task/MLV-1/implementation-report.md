# MLV-1 Implementation Report

## Ticket

**MLV-1 — Bootstrap ScanVault and implement the first usable Megascans library viewer**  
YouTrack: https://bodomus.youtrack.cloud/issue/MLV-1

Workflow level: 2 (initial architecture / structural feature).

## Repository baseline

- Root: `J:\Projects\UE_Projects\Megascans Library Viewer`
- Git initialized during the ticket at the user's request.
- Branch: `master` (explicitly confirmed; not renamed to `main`).
- Initial commit: none.
- Initial state: no solution, application, source projects, tests, or usable code graphs; supplied workflows were Unreal-specific.
- Code convention: `src/`; report convention: `review/review-MLV-1.md`.

## Confirmed decisions

1. Duplicate IDs: lexicographically smallest normalized full JSON path wins with Windows ordinal-ignore-case comparison; winner and every skipped path are reported.
2. Stale assets: removed only inside the successful full-scan SQLite transaction; cancellation/failure preserves the prior index.
3. Skills: adapted Graphify and CRG skills moved to `.agents/skills/`; obsolete top-level `skills/` removed.
4. Git branch: retain `master`.

## Toolchain

- .NET SDK: `10.0.301` (latest stable installed).
- Runtime target: `net10.0-windows` / Windows Desktop Runtime 10.
- Language: `latestMajor`, C# 14, no preview features.
- Nullable reference types, recommended analyzers, deterministic builds, and warnings-as-errors enabled.
- SQLite: `Microsoft.Data.Sqlite.Core 10.0.10` with Windows system SQLite provider `3.0.4`; runtime SQLite validated as at least 3.50.2.
- The initially evaluated bundled native SQLite dependency was rejected because its transitive native library had advisory GHSA-2m69-gcr7-jv3q. Final package audit reports no vulnerable packages.

## Implemented solution

### `ScanVault.Core`

Immutable asset/settings/scan models, contracts, Windows path policy, settings validation, tag normalization, folder-tree/descendant filtering, preview-priority selection, and deterministic duplicate resolution.

### `ScanVault.Infrastructure`

- cancellable deterministic recursive JSON discovery;
- inaccessible-folder reporting and reparse-point avoidance;
- tolerant async `JsonDocument` parsing of legacy schema variants;
- metadata-first local thumbnail/preview path resolution;
- atomic per-user settings JSON;
- scan orchestration with progress, malformed/unrelated aggregation and duplicate reporting;
- versioned SQLite schema for assets, tags, relationships, and scan state;
- transactional upsert, stale deletion, rollback, explicit indexes, and disabled pooling to release file handles deterministically.

### `ScanVault.App`

- WPF/Host/DI composition root and existing-index startup;
- no automatic startup rescan;
- main window toolbar, folder tree, search/status, virtualized wrapping grid, card metadata popup, and overlay preview;
- explicit Settings save, folder picker, Rescan and Cancel;
- MVVM command/state flow and view-model filtering;
- async image decoding with `BitmapCacheOption.OnLoad`, frozen images, cancellation/invalidation, no retained source lock, and a 128 MiB LRU cache;
- in-application preview closed by `Esc`, close button, or backdrop.

### Repository workflow and documentation

Adapted `AGENTS.md`, `.codex/PRE_TICKET_WORKFLOW.md`, both repository-local graph skills, and benchmark-agent wording to .NET/WPF. Added `README.md`, `Docs/architecture.md`, `Docs/index-format.md`, `Tickets/MLV-1.md`, investigation, plan, and reports.

## Duplicate-ID evidence and copy paths

No user Megascans library was selected or scanned during implementation, so **no real-library duplicate groups or copy paths were observed**. This avoids reading or changing user assets during deterministic validation.

The policy is covered by automated fixture `LexicographicallySmallestFullJsonPathWinsAndAllCopiesAreReported` with these exact paths:

- asset ID (case-insensitive): `same-id`;
- winner: `A:\TMP\ScanVault\duplicates\A\same-id.json`;
- skipped copy 1: `A:\TMP\ScanVault\duplicates\B\same-id.json`;
- skipped copy 2: `A:\TMP\ScanVault\duplicates\C\same-id.json`.

The orchestration test additionally proves that only the winning asset is passed to the index while the complete duplicate group is returned by `ScanResult`.

## Repository intelligence

### Graphify preflight

Initial repository corpus: 10 documents, no code. A graph was created with 46 nodes, 49 edges, and 8 communities. It correctly exposed the Unreal-oriented starting workflow and absence of application architecture; direct file inspection confirmed both findings.

### Graphify post-change

Confirmed command: `graphify update .` (first sandboxed attempt returned WinError 5; approved retry succeeded). Final graph: 646 nodes, 1127 edges, 36 communities. Focused query located `MainViewModel`, `LibraryScanService`, `SqliteAssetIndex`, `BoundedImageLoader`, DI boundaries, and relevant tests. `graphify diagnose multigraph --json` reported no missing/dangling endpoints, self-loops, exact duplicates, or same-endpoint collapses. `global.json` produced zero AST nodes, an expected non-code limitation.

### CRG preflight and post-change

Initial CRG build over the empty repository: 0 files/nodes/edges. Post-change graph was rebuilt after deleting only recoverable `bin/obj` outputs so generated XAML code was absent. Final state: 49 files, 283 nodes, 543 edges.

CRG classified the bootstrap impact as high, which is expected because this ticket establishes every application boundary. Five principal affected flows were found: transactional replacement/index reads plus settings/scan view-model refresh paths. Direct inspection verified DI registration, XAML event bindings, awaited scan path, transaction/rollback boundary, image ownership, and tests. Several reported isolated nodes were CRG parsing noise around XAML handlers and source-generated logging; source/XAML proved those paths are active.

## Validation

Working directory for every command: `J:\Projects\UE_Projects\Megascans Library Viewer`.

- `dotnet --info` — succeeded; SDK 10.0.301, Windows x64, Desktop Runtime 10.0.9.
- `dotnet restore ScanVault.sln` — succeeded; all projects restored.
- `dotnet list ScanVault.sln package --vulnerable --include-transitive` — succeeded; no vulnerable packages in six projects.
- `dotnet format ScanVault.sln --verify-no-changes --no-restore --verbosity minimal` — succeeded after applying standard formatting.
- `dotnet build ScanVault.sln --configuration Release --no-restore` — succeeded, 6 projects, 0 warnings, 0 errors.
- `dotnet test ScanVault.sln --configuration Release --no-build --logger "console;verbosity=minimal"` — succeeded, 22/22 tests:
  - Core: 8;
  - Infrastructure: 11;
  - App/ViewModels: 3.
- Release GUI smoke check — succeeded: process reached input-idle, displayed `ScanVault — Megascans Library Viewer`, and closed through the normal main-window path. No rescan started.

Automated coverage includes validation, tags, physical folder filtering, duplicate paths, preview priorities, parser variations, malformed/unrelated JSON, recursive discovery, cancellation, settings, scan aggregation, SQLite schema/upsert/stale removal/rollback, command state, folder selection, and preview state.

## Manual validation not performed

A real Megascans library was intentionally not scanned. Consequently the following remain user-environment checks: several-thousand-item scrolling performance, inaccessible ACL directory behavior, hover/popup screen-bound behavior, preview of real/corrupt images, visible progress during a long scan, cancel during a real scan, restart against a populated real index, and add/remove asset rescan behavior. Automated fixtures cover the underlying policies and persistence safety, but are not a substitute for those UI/library checks.

## Generated artifacts cleanup

Twelve `bin/` and `obj/` directories were removed after validation so CRG would not index generated XAML sources. They are fully recoverable with the documented build command. `.code-review-graph/` and `graphify-out/` remain local ignored graph state.

## Result

The smallest coherent MLV-1 architecture and first usable ScanVault implementation are complete. No source Megascans file was copied, modified, moved, or deleted. Remaining risk is concentrated in real-library/manual WPF behavior, not in the validated deterministic policies or transactional index guarantees.