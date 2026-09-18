# MLV-16 Implementation Report

## Summary

Implemented a separate global search mode over the last successfully loaded `AssetSummary` index snapshot. No filesystem rescan, SQLite query, schema migration, or index-format change occurs per search.

## Architecture

- `GlobalAssetSearchPolicy` in Core validates queries, compiles only the supported `*` and `_` wildcards into escaped timeout-bounded regex, enumerates searchable indexed fields, returns the first deterministic match reason, and classifies result types.
- `GlobalAssetSearchService` runs the Core policy on a worker thread.
- `MainViewModel` owns transient global-search state, 250 ms debounce, cancellation, generation-based stale-result rejection, errors, result counts, type filtering, Find related and Show in Library.
- Global mode ignores the selected physical folder, local `SearchText`, and persisted inventory filters. Those values are retained and resume when the global query is cleared.
- `MainWindow` provides the dedicated global-search strip and synchronizes Show in Library with the physical tree.
- Existing `VirtualizingWrapPanel` remains the result-items panel.

## Search fields

Search uses only the loaded index model:

- display name;
- Asset ID;
- canonical and raw asset type;
- asset folder and metadata JSON path;
- biome and region;
- categories and every typed tag;
- referenced content paths;
- variant names;
- mesh file names and paths;
- texture-set kind, texture file names/paths and map types;
- unclassified indexed file paths.

The result card shows its library-relative location and, in global mode, the matched field/value.

## Functional behavior

- Plain queries are case-insensitive sequences of complete Unicode letter/digit tokens. A token boundary is the start/end of a field or any non-letter/non-digit character, including `_`, `-`, spaces, `.`, `/`, and `\`.
- Wildcard queries are whole-field matches: `*` means zero or more characters and `_` exactly one.
- All other regex characters are escaped literally; matching has a finite timeout and queries are limited to 512 characters.
- Empty/whitespace query exits global mode instead of returning every item.
- Result types: All, Mesh, Material, Texture, Atlas/Billboard and Other.
- Search runs on typing after debounce and immediately through Enter/Search.
- Clear restores the retained local folder/search/inventory-filter/sort state.
- Show in Library clears global mode, selects the asset by JSON-path identity and requests tree-node selection.
- Find related starts a global search using the asset's existing Asset ID. This required no database change.

## Changed files

- `src/ScanVault.Core/Policies/GlobalAssetSearchPolicy.cs`
- `src/ScanVault.App/Services/GlobalAssetSearchService.cs`
- `src/ScanVault.App/ViewModels/MainViewModel.cs`
- `src/ScanVault.App/ViewModels/AssetCardViewModel.cs`
- `src/ScanVault.App/MainWindow.xaml`
- `src/ScanVault.App/MainWindow.xaml.cs`
- `src/ScanVault.App/App.xaml.cs`
- `src/ScanVault.App/ApplicationLog.cs`
- `tests/ScanVault.Core.Tests/GlobalAssetSearchPolicyTests.cs`
- `tests/ScanVault.App.Tests/ViewModelTests.cs`
- `tests/ScanVault.App.Tests/ApplicationBuildInfoTests.cs`
- `Docs/architecture.md`
- ticket/workflow artifacts under `Tickets/`, `Task/MLV-16/`, and `review/`.

The build-info test had an unrelated stale expectation (`0.2.0`) while the authoritative `Directory.Build.props` on clean master is `0.5.0`; it was synchronized and classified with required test comments.

## Graph validation

- Preflight CRG full build: 151 files, 1486 nodes, 3801 edges on `codex/MLV-16` / `57eef301969f`.
- Post-change CRG update/status: 151 files, 1302 nodes, 3806 edges. `detect-changes --base master --brief` reports 32 changed symbols/classes, no affected flows and risk 0.50. Its coarse test-gap list flags WPF/composition/logger nodes; direct tests cover policy and ViewModel behavior, and Release compilation covers XAML/DI signatures.
- Initial CRG brief rendering hit a Windows cp1251 `UnicodeEncodeError` after completing the update. Re-running with `PYTHONIOENCODING=utf-8` produced the full report.
- Graphify was refreshed after the new service/DI relationship: 2611 nodes, 6206 edges, 111 communities. The focused query resolves the intended Core policy → App service → MainViewModel → MainWindow/card/test surface.

## Validation

- `dotnet restore ScanVault.sln` — succeeded.
- `dotnet build ScanVault.sln --configuration Release --no-restore -m:1 -nodeReuse:false` — succeeded, 0 warnings, 0 errors.
- `dotnet test tests/ScanVault.Core.Tests/ScanVault.Core.Tests.csproj --configuration Release --no-build -m:1` — 105 passed.
- `dotnet test tests/ScanVault.Infrastructure.Tests/ScanVault.Infrastructure.Tests.csproj --configuration Release --no-build -m:1` — 65 passed.
- `dotnet test tests/ScanVault.App.Tests/ScanVault.App.Tests.csproj --configuration Release --no-build -m:1` — 53 passed before the review follow-up.
- `dotnet build ScanVault.sln --configuration Release --no-restore -p:BuildInParallel=false -m:1` — succeeded, 0 warnings, 0 errors after the review follow-up.
- `dotnet test ScanVault.sln --configuration Release --no-build -m:1 -p:BuildInParallel=false` — Core 105, Infrastructure 65, App 56; total 226 passed, 0 failed, 0 skipped.
- `git diff --check` — no whitespace errors (only Git's expected LF→CRLF notices).

The first aggregate `dotnet test ScanVault.sln` attempt stalled without test output in the local runner, so the same complete project set was executed sequentially and passed. A later build attempt was blocked by the manually launched application executable; after terminating only that known PID, the final build and tests passed.

## Manual UI validation and screenshots

The Release executable was launched, but in this execution environment it remained a responding process without a main-window handle and was unavailable to Computer Use. It was terminated after it blocked the binary. No screenshot was fabricated, and visual/manual behavior is not claimed as passed. The two requested screenshots (`*Wooden*Twig*` and Mesh-only) remain a manual acceptance item on an interactive Windows desktop with the user's indexed library.

## Remaining risks

- Visual layout and real-library screenshots need one interactive desktop pass.
- Result cards remain one ViewModel per match, while WPF visual containers are virtualized. For exceptionally large match-all queries this may warrant later paging or incremental ViewModel materialization.
- Type classification intentionally uses current canonical/raw type text plus indexed content signals; unusual future type names fall into Other.

## Code-review follow-up

The MLV-16 review findings were addressed on `codex/MLV-16`:

- `ShowInLibrary` now clears restored local `SearchText` and `InventoryFilter` before it selects the result's folder and card, so a locally hidden asset is always revealed.
- A successful Rescan now invalidates the current global-search generation and immediately reruns the active query against the newly loaded `allAssets` snapshot. This prevents old or removed assets from surviving in the result view.
- `RefreshVisibleAssets` builds one case-insensitive `JsonPath` → `GlobalAssetSearchMatch` lookup for a global result refresh. `GlobalSearchDescription` now uses that lookup rather than linearly scanning every match for every result card.

New regression tests cover Show in Library under restored text and inventory filters, plus a delayed old search that completes after Rescan. The test proves that the replacement-index result remains selected and the stale completion is ignored.

Post-review CRG update indexed 154 files, 1,332 nodes and 3,860 edges on `codex/MLV-16` at `c6785c36907b`; its diff analysis found no affected flows. Graphify was not refreshed because this follow-up does not change architecture, project boundaries, DI composition or entry points.

## Word-aware search follow-up

The normal-query matcher now extracts Unicode letter/digit tokens from the query and matches the same consecutive tokens in a field, separated by one or more non-letter/non-digit characters. Consequently, `Table` finds `Wooden Table` and `Dining_Table_4K`, while it does not find `vegetable` or `tabletop`. A query without any letter/digit token yields no result. This implementation preserves the existing timeout and `NonBacktracking` regex protections.

Queries containing `*` or `_` still use the unchanged whole-field wildcard matcher. Therefore `*Table*` finds `vegetable`, and `VAR_` continues to match `VAR1` but not `VAR10`.

Added Core policy coverage for token boundaries, case-insensitive matching, partial-word rejection, consecutive multiword matching, and explicit wildcard substring behavior. Added a ViewModel regression proving that an asset whose only potential match is the `vegetable` tag is not published for `Table`.

Validation after this follow-up:

- `dotnet restore ScanVault.sln` — succeeded.
- `dotnet build ScanVault.sln --configuration Release --no-restore -p:BuildInParallel=false -m:1` — succeeded, 0 warnings, 0 errors.
- `dotnet test ScanVault.sln --configuration Release --no-build -p:BuildInParallel=false -m:1` — Core 110 and Infrastructure 65 passed.
- `dotnet test tests/ScanVault.App.Tests/ScanVault.App.Tests.csproj --configuration Release --no-build --no-restore -p:BuildInParallel=false -m:1` — App 57 passed. Across all three test projects: 232 passed, 0 failed, 0 skipped.
- `git diff --check` — no whitespace errors (only expected LF→CRLF notices).

Post-change CRG incremental update completed successfully (1,337 indexed rows); the coarse graph report lists no affected flows. Graphify was not refreshed because the follow-up changes matching semantics and tests only, without changing architecture or project boundaries.
