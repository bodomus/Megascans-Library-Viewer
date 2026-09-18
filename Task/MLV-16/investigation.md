# MLV-16 Investigation

## Workflow baseline

- Level: 2 (cross-layer WPF feature with Core policy and ViewModel/UI changes).
- Repository root: `J:/Projects/UE_Projects/Megascans Library Viewer`.
- Branch: `codex/MLV-16`, created from synchronized `master`.
- Initial commit: `57eef301969f477c3a59dfebc797d3bc618b4c6a`.
- Initial worktree: clean.

## Graph evidence

- Graphify command: `graphify check-update .` followed by focused `graphify query` for global search, index, MainViewModel, tree, filters, asset cards, IDs, tags, file names and paths.
- Graphify identified `MainViewModel.RefreshVisibleAssets`, `AssetFiltering`, `AssetSummary`, `AssetCardViewModel`, `MainWindow`, `IAssetIndex.GetAssetsAsync`, and related tests as the owning surface.
- CRG was rebuilt with `code-review-graph build --repo .`: 151 files, 1486 nodes, 3801 edges; status after build reports branch `codex/MLV-16` at commit `57eef301969f`.

## Source validation

- `IAssetIndex.GetAssetsAsync` loads the persisted read model at startup; no new scan or DB query is required per search.
- `MainViewModel` stores the complete immutable `allAssets` snapshot and currently filters it synchronously in `RefreshVisibleAssets`.
- Existing `SearchText` is a local filter composed after `SelectedFolderPath` and `InventoryFilter`; it cannot satisfy global-scope semantics.
- Existing `AssetFiltering.MatchesSearch` covers many normalized fields but only substring matching and does not return the matched field.
- `AssetSummary` already contains ID, name, canonical/raw type, folder/JSON paths, categories, typed tags, biome, region, referenced paths and content inventory (variants, meshes, texture components, issues/readiness).
- `VirtualizingWrapPanel` already limits visual realization, although the ViewModel still creates one lightweight card per result.
- WPF bindings mutate `ObservableCollection` on the dispatcher. Search computation may run off-thread, but result publication must resume on the captured UI context.
- Selected asset identity uses `(ID, JSON path)` and can be restored after view rebuilds.
- Tree selection is code-behind driven; Show in Library needs an explicit navigation request so the visual tree and ViewModel stay aligned.

## Risks and decisions

- Keep the existing local `SearchText` and introduce separate global-search state so smart collections and saved local filters remain unchanged.
- Global mode ignores selected folder, local search and persisted inventory filters. Its own visible type filter is applied after global matching.
- Preserve folder, local filters and sort values during global mode; clearing the global query naturally restores them.
- Use escaped regex only for wildcard queries, with a finite timeout and a query-length guard. Wildcard matching is whole-field so `VAR_` does not match `VAR10`; plain queries remain substring searches.
- Use cancellation plus monotonically increasing request generation so stale completion cannot publish.
- No schema or index-format change is required.

