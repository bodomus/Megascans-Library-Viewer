# MLV-6 — Investigation

## Workflow and repository state

- Workflow level: 2 structural feature.
- Repository root: `J:\Projects\UE_Projects\Megascans Library Viewer`.
- Branch: `master`.
- Initial commit: `0bd36f54bfccab8467eb6dc3079d9780487dd9ff`.
- Initial worktree: one unrelated untracked `.graphifyignore`; it must remain untouched and excluded from MLV-6.
- Ticket: `MLV-6` — Diagnostics, About information, and index compatibility.
- YouTrack fields: Feature / In Progress / Root / PreRelease / assignee ChatGPT / estimate 4h.

## Graph evidence

### Graphify

Confirmed syntax with `graphify --help`, inspected `graphify-out/`, then ran:

```powershell
graphify query "MLV-6 diagnostics about index compatibility schema version normalization version last scan metadata SQLite startup status clipboard paths MainViewModel tests" --budget 6500 --graph graphify-out/graph.json
```

The graph located the active App composition root, MLV-5 build information, `MainViewModel`, `ApplicationLog`, `ScanVaultPaths`, `IAssetIndex`, `SqliteAssetIndex`, schema-v1 migration, normalization marker, `scan_state`, scan orchestration, clipboard service, and related App/Infrastructure tests. Direct source inspection confirmed these candidates.

Graphify was refreshed after MLV-5 and currently includes 1121 nodes / 1842 edges / 67 communities. The pre-existing untracked `.graphifyignore` excludes Markdown and contains unrelated project patterns; it is preserved and treated as a limitation for any later refresh. Source and tests remain authoritative.

### Code review graph

Confirmed syntax with `code-review-graph --help`, then ran:

```powershell
$env:PYTHONUTF8='1'
code-review-graph update --base HEAD~1 --repo . --brief
code-review-graph status --repo .
```

CRG is current at `0bd36f54bfcc` on `master` with 410 nodes / 823 edges / 63 C# files. The direct change surface spans Core diagnostics contracts/models/formatter, Infrastructure SQLite inspection and persisted scan state, and App diagnostics UI/view models. Existing scanner/parser/image behavior is adjacent but does not require modification.

## Existing behavior

### Index initialization and compatibility

- `SqliteAssetIndex.InitializeAsync` always creates the parent directory and opens `ReadWriteCreate` before inspecting `schema_info`.
- It executes `PRAGMA journal_mode=WAL`, which can write before compatibility is known.
- Missing databases are immediately created and become indistinguishable from a compatible empty index.
- Schema v1 migrates transactionally to v2 and populated rows retain normalization marker 1.
- Any schema other than 1 or 2 throws; newer schema and corruption are not represented explicitly.
- `MainViewModel.InitializeAsync` is called inside App startup. An exception currently terminates the whole application through the outer startup error handler.
- `ReplaceLibraryAsync` has no explicit safety state; it calls initialization and then writes.

### Scan metadata

- Schema v2 already has singleton `scan_state(library_root, completed_at_utc, result_json)`.
- It is updated in the same transaction as full asset replacement and normalization promotion.
- The current JSON is the pre-commit draft: added/updated/removed are zero and elapsed time excludes index work.
- No reader exposes the persisted row to App or tests.
- Main-window status contains only the current session message.

### Application/UI diagnostics

- MLV-5 provides immutable `ApplicationBuildInfo` with every required build/runtime value.
- `ScanVaultPaths` provides database, settings, and thumbnail-cache paths. No file log provider exists; log directory is therefore not claimed.
- Settings, current sort, current selected folder, indexed count, and configured library root are already owned by `MainViewModel`.
- There is no menu. The existing toolbar has Settings, Rescan, and Cancel, so a compact About / Diagnostics button is consistent with ticket scope.
- Clipboard is abstracted by `IAssetInteractionService`; `DesktopAssetInteractionService` is the only WPF clipboard implementation.

## Required ownership and dependency direction

- Core owns explicit compatibility/scan-attempt/diagnostics records and deterministic text formatting.
- Infrastructure owns read-only SQLite validation, known migration, safe write gate, persisted successful-scan metadata, and per-user paths.
- App owns capture of build/live UI state, diagnostics view model, copy command, and WPF dialog.
- Direction remains `App -> Infrastructure -> Core`; Core gains no WPF or SQLite dependency.

## Selected compatibility state machine

1. `Missing`: database file absent. Startup performs no filesystem write; reads return an empty catalog; full Rescan is allowed; it creates an empty schema v2 before the transactional catalog replacement.
2. `RequiresMigration`: existing schema v1 with the documented migration path. Read-only inspection reports this state before mutation. Normal initialization performs the existing transactional migration, then re-inspects.
3. `RequiresRescan`: schema v2 is readable but normalization is older. Existing rows remain readable and a successful full scan promotes the marker atomically.
4. `Compatible`: schema and normalization are current and integrity/schema checks pass.
5. `NewerVersionUnsupported`: schema or normalization marker is newer than supported. Reads/writes through the index are blocked, no PRAGMA/write/migration runs, and the file remains unchanged.
6. `Corrupted`: file exists but cannot pass SQLite/schema validation. Reads/writes are blocked; no delete, rename, replace, or downgrade occurs.

Unknown older schema values and invalid/missing required schema structures are treated as corrupted rather than guessed. State is derived from database facts, never UI text.

## Read/write and lifecycle constraints

- Inspection uses a pooled-off, read-only SQLite connection and disposes readers/connections deterministically.
- `PRAGMA quick_check(1)` and schema reads run asynchronously off the WPF call chain; no `.Result`/`.Wait()` is introduced.
- Missing/newer/corrupted states do not create directories or open a write connection.
- The write path revalidates compatibility before beginning the full replacement transaction.
- Only schema v1 is migrated, within the existing transaction.
- Corruption exceptions are logged technically while UI status stays concise.
- Full scan cancellation still rolls back assets, scan state, and normalization marker together.

## Diagnostics data design

- `IndexCompatibilityInfo`: state, schema/normalization values, readable/write-safe flags, Rescan flag, concise guidance.
- `PersistedScanMetadata`: successful timestamp, duration, status, add/update/remove/skipped/inaccessible counts, stable result summary.
- `IndexDiagnostics`: compatibility, asset count, persisted scan metadata.
- `DiagnosticsSnapshot`: build/runtime, configured library/UI state, paths, index details, and most recent scan attempt.
- `DiagnosticsFormatter`: fixed field order, invariant formatting, one `Unavailable` token, no environment enumeration and no stack traces.

The existing `scan_state` table remains schema v2. New successful scans store the richer record in `result_json`; the reader accepts legacy `ScanResult` JSON as a backward-compatible fallback. No structural migration is needed.

## UI and command design

- Add a toolbar `About / Diagnostics` button.
- Opening captures fresh index diagnostics asynchronously, then opens a compact owner-centred dialog.
- The dialog shows immutable label/value rows and a Copy diagnostics command.
- Clipboard exceptions are caught in `DiagnosticsViewModel`, logged, and shown as a concise status; the dialog remains open.
- Main status and Rescan availability derive from explicit compatibility state. Rescan is allowed for Missing/Compatible/RequiresRescan and blocked for NewerVersionUnsupported/Corrupted.

## Tests to add/update

- read-only inspection for all six states;
- known v1 migration and outdated normalization;
- newer schema and newer normalization write blocking with byte-for-byte file preservation;
- corrupted file detection/write blocking/no deletion;
- Missing startup creates no database and first safe replacement creates it;
- persisted successful scan metadata and legacy JSON fallback;
- formatter stable order/unavailable values/no unrelated environment data;
- diagnostics clipboard success/failure;
- startup action messages and Rescan command state;
- WPF diagnostics layout/binding smoke test;
- existing transaction/cancellation/migration tests adjusted without weakening assertions.

## Risks and exclusions

- SQLite `quick_check(1)` validates the first reported integrity problem; it is a startup diagnostic, not repair.
- External replacement of the database while ScanVault is running is not a supported coordination scenario; write preparation still re-inspects immediately before opening writable mode.
- No file logger currently exists, so Diagnostics will not invent a log directory.
- No automatic backup/restore, repair, downgrade, replacement, upload, telemetry, update check, or release behavior is added.
- Interactive clipboard and visual checks will not be claimed unless actually executed.
