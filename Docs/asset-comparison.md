# Asset Comparison

Asset Comparison is a read-only, two-asset view built from the current SQLite index snapshot. It compares normalized metadata and the existing `AssetContentInventory`; it never starts a Rescan, changes metadata, writes to the index, or reads FBX, ABC, or texture payloads.

## Selecting two assets

The main window keeps its normal single-card selection. The comparison tray is independent:

1. Select a card and choose **Add selected**, or use **Add to comparison** in its context menu.
2. Add a second, different asset. The tray changes to **Ready to compare**.
3. Choose **Compare assets** or press `Ctrl+Shift+C`.

While the asset list has focus, `Ctrl+Shift+C` adds the current card until two slots are full, then opens the comparison. Adding an already selected identity is a no-op with an explicit status message. Adding a third distinct asset is deterministic FIFO replacement: the previous right side becomes left and the new asset becomes right.

The comparison window's **Replace left** and **Replace right** actions close the snapshot, preserve the other tray slot, and return the main window to an explicit replacement state.

## Result semantics

Every row includes a text result; color is only secondary emphasis.

- `Equal` - normalized domain values match.
- `Different` - both values are known and differ.
- `Only left` / `Only right` - the logical item is missing from one side.
- `Unknown` - the index does not contain enough information to compare the value.
- `Not applicable` - the property does not apply to that asset type, such as LOD count for a surface.
- `Ambiguous` - duplicate or conflicting normalized keys prevent a unique alignment. Every duplicate remains visible.

Missing, Unknown, and Not applicable are intentionally different. **Show differences only** hides only `Equal` rows.

## Tabs and matching rules

- **Overview** compares IDs, names, type, biome, region, logical folder name, resolution, texel density, completeness, counts, FBX/ABC, Atlas, and Billboard.
- **Variants and LODs** aligns case-normalized variant identifiers and numeric LOD identifiers, with mesh formats included in the semantic value.
- **Texture Sets** aligns normalized texture-set kind and map type, then compares resolution and format.
- **Files** uses logical inventory keys (category, set/map, variant, LOD, and extension/format), not absolute paths. Display paths are relative to each asset root.
- **Issues** aligns by stable `AssetContentIssueCode`; message/path differences are details, not the primary identity.

Collection input order does not affect alignment. Duplicate normalized keys are not collapsed.

## Snapshot and Refresh

The window keeps the immutable indexed snapshot captured when it opens. If a successful Rescan replaces the in-memory index, the window displays a stale indicator and does not change unexpectedly. **Refresh** resolves both asset IDs against the new snapshot. A removed side gets its own "no longer present" state; the other side remains identified.

Closing the window cancels row/preview loading. Row construction runs away from the WPF dispatcher, previews load asynchronously through the bounded image loader, and the lists use WPF recycling virtualization.

## First-version limits

Asset Comparison uses indexed metadata and inventory. It does not compare binary mesh or texture contents. There is no pixel comparison, geometry/UV/material inspection, hashing, scoring, automatic "best" recommendation, merge, deletion, or export.

The current index does not persist per-file size or per-file last-write timestamps. Those properties are therefore Unknown; comparison does not query the source filesystem to fill them.
