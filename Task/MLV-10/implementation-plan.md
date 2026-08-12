# MLV-10 implementation plan

1. Add explicit Core comparison models and an `AssetComparisonPolicy` with typed comparison states, normalized relative paths/logical keys, stable order-independent alignment, ambiguity retention, and summary counts.
2. Add focused Core unit tests for equal/different/missing/unknown/not-applicable, normalization, variants/LODs/textures/files/issues, duplicate keys, same-asset rejection, filtering, and summary counts.
3. Add an App comparison view model that builds rows off the dispatcher, supports cancellation, per-side missing/error state, differences-only filtering, swap, refresh, replace callbacks, existing open/folder/inventory actions, preview loading, and duration/file-count metrics.
4. Preserve the current single-selection list and add a visible two-slot comparison tray. Card/context and keyboard actions fill the tray; a third distinct choice uses FIFO replacement; duplicate identity is ignored with a clear status.
5. Add a virtualized, accessible `AssetComparisonWindow` with Overview, Variants and LODs, Texture Sets, Files, and Issues tabs, explicit textual states, restrained status styling, Swap/Replace/Refresh/Open actions, Esc close, and predictable initial focus.
6. Mark open comparison snapshots stale after successful Rescan and refresh both sides from the new in-memory index without rescanning or querying the whole library again.
7. Add structured comparison lifecycle logging and App/WPF tests for selection state, third selection, command gating, swap, replace, differences-only, stale/refresh, cancellation, and five-tab realization.
8. Update README and comparison documentation, then update CRG, inspect impact, refresh Graphify because new Core/App relationships and entry points are introduced, and run targeted plus full Release validation, format verification, and `git diff --check`.

## Deliberate first-version limits

- No binary, pixel, mesh-geometry, UV, hash, or Unreal material comparison.
- No score or automatic “best asset” choice.
- File size and file last-write time are Unknown because the current index does not persist them; MLV-10 will not introduce filesystem reads or an index migration solely for those fields.
- Snapshot changes only through explicit Refresh after a stale notification.
