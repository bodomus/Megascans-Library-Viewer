# MLV-10 implementation report

## Summary

Implemented read-only two-asset comparison directly on `master`. The feature keeps the existing single-card selection, adds an explicit two-slot tray, and opens a cancellable, virtualized five-tab Asset Comparison window backed only by the immutable indexed `AssetSummary` / `AssetContentInventory` snapshot.

## Workflow baseline

- Workflow level: Level 2.
- Repository root: `J:/Projects/UE_Projects/Megascans Library Viewer`.
- Branch: `master`.
- Initial commit: `93b5cd9c277fc3d6201755ab2b85de922532fc8d`.
- Initial working tree: clean.
- YouTrack: MLV-10 changed from Open to In Progress before implementation.

## Selection UX

- The asset `ListBox` remains `SelectionMode="Single"`; search, sort, keyboard focus, preview, card recycling, and selection preservation are unchanged.
- `Add selected`, the card context action `Add to comparison`, and list-scoped `Ctrl+Shift+C` fill the tray.
- First distinct asset fills left; second fills right; a third uses deterministic FIFO (`left = old right`, `right = new`).
- Adding the same normalized asset ID is rejected as a no-op with status text.
- `Compare assets` is enabled only for two different identities.
- `Replace left/right` closes the snapshot, preserves the other tray slot, and enters an explicit replacement state.

## Comparison model and normalization

Added explicit Core models for `Equal`, `Different`, `OnlyLeft`, `OnlyRight`, `Unknown`, `NotApplicable`, and `Ambiguous`, plus distinct value kinds for Present, Missing, Unknown, Not applicable, and Ambiguous. No reflection-based comparer is used.

- strings are trimmed and case-normalized only for semantic identities;
- display names retain display-value semantics;
- numeric values are nullable-aware; zero is not Unknown;
- resolution compares typed width/height;
- paths are relative to each asset root, normalized to `/`, and never use the absolute path as the logical identity;
- collection alignment is order-independent and stably sorted;
- duplicate normalized keys retain every occurrence and mark the group Ambiguous;
- file size and `LastWriteTimeUtc` are explicitly Unknown because the current index does not persist them;
- all comparisons use indexed metadata/inventory and perform no source payload reads.

## Matching keys

- variants: normalized variant identifier, with LOD/format collection as the compared value;
- LODs: numeric LOD identifier, with normalized variant/format collection as the compared value;
- textures: texture-set kind + normalized map type; resolution and format are compared details;
- files: category + normalized set/map or variant/LOD + format/extension; unclassified files additionally use normalized relative logical name;
- file issue flags: stable issue codes associated through normalized relative paths;
- issues: stable `AssetContentIssueCode`; normalized message and related relative paths are compared details.

## UI and lifecycle

- Added `AssetComparisonWindow` with Overview, Variants and LODs, Texture Sets, Files, and Issues tabs.
- Added restrained left-border status emphasis, explicit text result labels/tooltips, tab navigation, predictable initial focus, and Esc close.
- Added global `Show differences only`; it hides only Equal rows and preserves Unknown/Missing/Ambiguous rows.
- Added Swap, Refresh, Replace left/right, Open asset, Open folder, and Content inventory actions using existing callbacks/services.
- Row lists use WPF recycling virtualization. CPU row construction runs through `Task.Run`; previews load asynchronously via the existing bounded image loader.
- Closing the window cancels active comparison/preview loading and disposes the session.

## Snapshot and stale behavior

The window keeps the immutable opening snapshot. After a successful Rescan, every open comparison session is marked stale. Refresh resolves both IDs against the new in-memory indexed snapshot. Removed sides receive independent “no longer present” errors; no Rescan or write is initiated by comparison.

## Structured logging and metrics

Added opened, loaded, failed, refreshed, and swapped events. Information events include asset IDs, logical file counts, difference count, and duration; they do not include absolute paths.

`AssetComparisonViewModel` exposes left/right file count, load duration, comparison duration, and total render-ready duration for each window. Automated large-inventory validation used 2,000 logical files per side and ran two deterministic comparisons without filesystem reads in 95 ms total test-case time. This is a test-host measurement, not a UI rendering SLA.

## Changed files

- Core: `AssetComparisonModels.cs`, `AssetComparisonPolicy.cs`.
- App: `AssetComparisonWindow.xaml(.cs)`, `AssetComparisonViewModel.cs`, `ComparisonApplicationLog.cs`, `MainWindow.xaml(.cs)`, `MainViewModel.cs`, `AssetCardViewModel.cs`.
- Tests: `AssetComparisonPolicyTests.cs`, `AssetComparisonViewModelTests.cs`, `ViewModelTests.cs`, `MainWindowTests.cs`.
- Docs/artifacts: `README.md`, `Docs/asset-comparison.md`, `Tickets/MLV-10.md`, and `Task/MLV-10/*`.

## Graph and source validation

### Pre-change

- Graphify focused query identified MainViewModel/card selection, Content Inventory, AssetSummary/AssetContentInventory, SqliteAssetIndex, preview loading, and tests.
- `graphify update .`: 1,574 nodes, 3,547 edges, 64 communities.
- CRG initially reported a different branch and was rebuilt: 111 files, 919 nodes, 2,118 edges.

### Post-change

- `code-review-graph update --repo . --brief` indexed 34 changed files, 158 nodes, and 393 edges. Its optional console panel hit a CP1251 Unicode rendering exception after the update; graph status remained healthy (833 nodes, 2,109 edges, 111 files, current `master`).
- Re-running read-only impact with UTF-8 reported 7 tracked changed files, 23 changed symbols, no affected recorded flow, risk 0.50, and named MainWindow entry points. This Git-based view cannot count untracked new tests, so its “test gaps” are graph-tool noise rather than source evidence.
- `graphify update .`: 1,791 nodes, 4,035 edges, 66 communities.
- Focused post-query found AssetComparisonPolicy/ViewModel/Window, all row builders, MainViewModel tray entry point, and Core/App tests.
- `graphify affected "AssetComparisonPolicy" --depth 3` linked the policy directly to comparison loading/filtering and all focused Core tests. Generic `.LoadAsync()` proximity to export/history was classified as graph noise; source contains no comparison dependency in those subsystems.
- Direct `rg`, source inspection, compilation, WPF realization, and tests confirmed active constructor/event/command bindings, cancellation ownership, stale notification, snapshot resolution, and absence of SQLite/library writes.

## Validation results

- `dotnet restore ScanVault.sln` — passed (all six projects restored/up to date).
- `dotnet build ScanVault.sln --configuration Release --no-restore` — passed, 0 warnings, 0 errors.
- `dotnet test ScanVault.sln --configuration Release --no-build` — passed: Core 56, App 29, Infrastructure 41; total 126/126.
- `dotnet format ScanVault.sln --verify-no-changes --no-restore` — exit 0; Roslyn reported non-fatal workspace-load warnings only.
- `git diff --check` — passed; Git emitted only configured LF-to-CRLF conversion notices.
- Focused large inventory test — passed: 2,000 files per side, two comparisons, 95 ms test-case duration.
- WPF realization test — passed: main tray binding and five-tab comparison window instantiate on an STA thread.
- Same-asset rejection, equal/different, missing/unknown/not-applicable, order independence, duplicate ambiguity, differences-only, FIFO selection, swap, missing refresh, stale refresh, and cancellation — covered by passing automated tests.

## Risks and unverified assumptions

- Per-file size and last-write time remain Unknown until a future index schema stores them; MLV-10 intentionally performs no live filesystem query.
- Existing inventory has no explicit issue severity/category field and no texture-to-variant association; comparison shows the stable available code/details without inventing data.
- Interactive validation against the user's real Megascans library was not performed, to avoid depending on or touching user assets. Deterministic fixtures and WPF STA realization were used instead.
- GitHub Actions was not observed because the requested direct-`master` work was not pushed and no PR was created.
- No scan ran, so there were no duplicate scan groups or winner/skipped asset paths to report.

