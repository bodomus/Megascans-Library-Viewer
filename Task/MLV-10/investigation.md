# MLV-10 investigation

## Workflow classification and baseline

- Level: 2 (new cross-cutting Core/App feature and WPF entry point).
- Repository: `J:/Projects/UE_Projects/Megascans Library Viewer`.
- Branch: `master`.
- Initial commit: `93b5cd9c277fc3d6201755ab2b85de922532fc8d`.
- Initial worktree: clean.
- Ticket state was changed from Open to In Progress before implementation.

## Graph preflight

- `graphify --help` confirmed the installed syntax.
- Focused query: `graphify query "Asset cards selection MainViewModel Asset Details Content Inventory files texture sets variants LOD issues SQLite repository tests" --budget 5000 --graph graphify-out/graph.json`.
- `graphify update .` refreshed the graph: 1,574 nodes, 3,547 edges, 64 communities. It identified `MainViewModel`, `AssetCardViewModel`, `ContentInventoryViewModel`, `AssetSummary`, `AssetContentAnalyzer`, `IAssetIndex`, `SqliteAssetIndex`, and related tests as the main surface.
- CRG initially reported a graph built on `codex/mlv-11`; `code-review-graph build --repo .` rebuilt it for current `master`: 111 files, 919 nodes, 2,118 edges.
- The installed CRG CLI exposes build/status/change-impact commands but no direct symbol-query CLI. Exact dependency findings below were validated with exhaustive `rg` and source inspection.

## Existing behavior

### Selection and navigation

- `MainWindow.xaml` uses a virtualized `ListBox` with `SelectionMode="Single"` and two-way `SelectedCard` binding.
- `MainViewModel.SelectedCard` only drives preview and copy-folder command states.
- Enter opens preview; Ctrl+C copies the selected folder. Double-click opens preview. Right-click first selects the card.
- `RefreshVisibleAssets` disposes and rebuilds card view models while preserving single selection by immutable asset ID plus JSON path.
- There is no multi-select, comparison tray, or comparison command.

### Details and inventory

- Card hover shows compact metadata. Double-click uses `PreviewViewModel` and the bounded image loader.
- `ContentInventoryRequested` creates `ContentInventoryViewModel` from the existing immutable `AssetSummary`; `MainWindow` opens a modal `ContentInventoryWindow`.
- Existing interaction commands are centralized through `IAssetInteractionService` and card/content view models; these should be reused for folder and content-inventory actions.

### Domain and data loading

- `AssetSummary` contains normalized metadata and an immutable `AssetContentInventory`.
- Inventory contains mesh variants/LOD/format/path, texture set kind/resolution/components, unclassified files, completeness, and stable issue codes.
- `SqliteAssetIndex.GetAssetsAsync` deserializes the complete `inventory_json`; `MainViewModel.InitializeAsync` and successful Rescan load the whole indexed snapshot once. Comparison therefore needs no library scan, binary reads, or N+1 query.
- Indexed inventory does not currently store file size or per-file last-write timestamp. These values must be represented as Unknown rather than reading binary files or changing the index schema in this ticket.

### Normalization and ambiguity

- Asset types, texture map types, texture set kinds, mesh formats, variant names, numeric LODs, and issue codes already have normalized domain representations.
- Paths in inventory may be absolute; comparison display and keys must normalize them relative to each asset root and use `/` separators.
- Duplicate logical mesh/texture keys are already reported as issues. Comparison must retain every duplicate and mark ambiguous groups rather than collapsing them.

### WPF, async, and lifecycle

- Card virtualization and bounded/cancellable image loading must remain unchanged.
- CPU row construction can run on a worker thread because inputs and output records are immutable.
- Preview loads must remain asynchronous and cancellable when the comparison window closes.
- A comparison window should retain its opening snapshot. A completed Rescan should mark it stale; Refresh should resolve both IDs against the new in-memory index snapshot. Missing sides must be isolated.

## Ownership and blast radius

- Core owns explicit comparison result models, normalized keys, row construction, filtering-independent output, and summary counts.
- App owns the two-slot selection tray, commands, window/view model, preview loading, lifecycle, accessibility, existing navigation callbacks, and structured UI logging.
- Infrastructure and SQLite schema require no change because current indexed snapshots already contain the needed normalized inventory; absent file size/timestamp remain Unknown.
- Direct impact: new Core comparison policy/models, `MainViewModel`, `AssetCardViewModel`, `MainWindow`, new comparison window/view model/logging, Core/App tests, README/docs.
- Adjacent regression surface: card selection/rebuild, preview, content inventory, Rescan refresh, virtualization, export, search/sort, and smart collections.

## Investigation conclusions

Use an explicit two-slot comparison tray instead of WPF multi-select. Adding a card fills left then right; a third distinct card deterministically evicts the oldest (`left = right`, `right = new`). This keeps keyboard and single-card selection unchanged and makes both chosen assets visible. The comparison window receives an immutable snapshot, builds rows asynchronously, loads previews separately, and can refresh after MainViewModel marks it stale following Rescan.

