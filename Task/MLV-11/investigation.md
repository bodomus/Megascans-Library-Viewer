# MLV-11 investigation

## Baseline

- Workflow level: 2 (new cross-project feature and WPF entry point).
- Repository: `J:/Projects/UE_Projects/Megascans Library Viewer`.
- Branch: `codex/mlv-11`, created from `master` at `a4c2e26045a1c42d8a8614907526bb98553ec9c9`.
- Initial working tree contained the unrelated untracked `Task/MLV-8/implementation-report.md`; it must remain untouched.
- Ticket state was changed from Open to In Progress.

## Graph evidence

### Graphify

- `graphify --help` confirmed the installed query/update syntax.
- Existing `graphify-out/graph.json` is readable.
- Focused query: `graphify query "MLV-11 export reports current view query filter pipeline smart collections scan history inventory completeness WPF dialogs background tasks logging SQLite schema report writers tests" --budget 6000`.
- The query located `MainViewModel`, `AssetFiltering`, `AssetSummary`, `AssetContentInventory`, `SmartCollectionPolicy`, `ScanHistoryViewModel`, `SqliteAssetIndex`, the history partial, DI, logging, and relevant tests.
- Graphify is navigation evidence only; the findings below were verified directly in source.

### code-review-graph

- `code-review-graph status` initially reported 617 nodes, 1,432 edges, 82 files and warned that the graph belonged to `codex/MLV-8-smart-collections`.
- `code-review-graph build --repo .` rebuilt the graph on this branch: 94 files, 778 nodes and 1,715 edges.
- Exact dependency and entry-point checks were validated with `rg` and source inspection because the installed CLI exposes build/status/impact commands but no interactive symbol-query subcommand.

## Current behavior and ownership

### Selection and query pipeline

- `MainViewModel` loads the current SQLite read model once through `IAssetIndex.GetAssetsAsync` into `allAssets`.
- `RefreshVisibleAssets` applies folder scope, `AssetFiltering.MatchesInventoryFilter`, `AssetFiltering.MatchesSearch`, then `AssetSorting.Apply`.
- `Assets` therefore preserves the exact current-view order and can be snapshotted without reflecting over WPF columns or reimplementing filter semantics.
- Selection is single-card only. MLV-11 will explicitly support the current selected asset and will not introduce a hidden multi-selection model.

### MLV-8 smart collections

- MLV-8 is present on `master`: `SmartCollectionRecord`, versioned `SmartCollectionDefinition`, built-ins, `SmartCollectionPolicy`, JSON persistence, editor UI, and tests exist.
- A smart-collection report can snapshot the selected record and evaluate it with the existing `SmartCollectionPolicy.Matches` semantics. Its metadata can include name, description, definition version and conditions.

### MLV-9 scan history

- MLV-9 is present on `master`: schema version 4, `scan_runs`, snapshots and changes, `ScanRunSummary`, `ScanChangeSummary`, and a read-only history window exist.
- `IAssetIndex.GetScanChangesAsync` is category/limit based. The export data source must combine categories and use the persisted completed run rather than rescan the library.
- Non-completed runs must be rejected before writing.

### Content inventory and completeness

- `AssetSummary.Content` contains variants/meshes, texture sets/components, unclassified files, stable `AssetContentIssueCode` values, completeness, and summary counts.
- The persisted read model has paths and classifications but not a reliable size/last-write value for every inventory entry. Export DTO fields for unavailable values will remain nullable rather than reading binary files or inventing data.
- Issue rows can use the stable enum name as `IssueCode`; UI text is not the contract.

### WPF and background work

- `App` is the composition root. `MainWindow` owns window lifecycle and confirmation dialogs while view models own state and commands.
- Existing long operations use `AsyncRelayCommand`, `CancellationTokenSource`, `IProgress<T>` and bounded UI updates.
- Writers must use asynchronous file APIs and periodic progress reporting; no synchronous filesystem/serialization work will be introduced on the dispatcher.

### Dialogs and file selection

- There is no existing save-file abstraction. Existing WPF dialogs are small owned windows with code-behind limited to window lifecycle.
- The export dialog will own `SaveFileDialog` and overwrite confirmation; the export view model will own profile/scope/format/options, validation, estimate, execution and cancellation.

### Logging and diagnostics

- App logging uses source-generated `ApplicationLog` events; Infrastructure has its own source-generated events.
- Export orchestration belongs to App, so started/completed/cancelled/failed events will be added to `ApplicationLog` without logging a full destination path.
- Diagnostics currently has no persisted export state. Adding persistent diagnostics storage would broaden scope; the first version will expose live/last export state in the export dialog and document this limitation.

### SQLite and safety

- Current schema version is 4 and normalization version is 3.
- Export is read-only and needs no schema migration. It consumes the current `IAssetIndex` read contracts only.
- Writers must write a sibling temporary file, flush/close it, then move it into place. Cancellation/failure deletes temporary artifacts; an existing destination is only replaced after explicit UI confirmation.
- CSV metadata will use the preferred companion `<report>.metadata.json` file.

## Blast radius

- Direct: Core report contracts/DTOs/profile mapping; Infrastructure format writers and export service; App DI, main toolbar entry point, export dialog/view model, logging.
- Adjacent: `MainViewModel` snapshot access to the existing current-view order and selected smart collection/asset; scan-history reads.
- Tests: Core mapping/escaping helpers, Infrastructure writer/file-safety integration, App view-model state.
- Not affected: scanner traversal, parser, SQLite write transaction, image loader/cache, library assets, settings schema.

## Risks and decisions

- Large exports must not materialize an additional complete report. Writers will enumerate source rows and stream output; the existing SQLite read model is already materialized by application startup.
- Markdown is human-readable and may be large, but remains streamed and escaped.
- CSV companion metadata introduces a second atomic output. Both are staged first; cleanup covers both staging files.
- Single selection is the documented Selected Assets behavior until the application gains true multi-selection.
- Manual Excel validation may not be automatable in the current environment; byte-level BOM and parser-oriented escaping tests are authoritative automated evidence and any remaining manual gap will be reported.

