# MLV-9 Investigation

## Workflow

- Level: 2 feature / multi-project persistence + UI change.
- Repository root: `J:/Projects/UE_Projects/Megascans Library Viewer`.
- Branch: `codex/MLV-9-scan-history`.
- Initial commit: `36139b6b430f8af96c1a10f0f847870f646841fa`.
- Initial working tree: clean after branch creation.
- YouTrack: `MLV-9`, moved from `Open` to `In Progress`.

## Graph preflight

- Graphify syntax confirmed with `graphify --help`.
- Existing `graphify-out/graph.json` present; focused query used:
  `graphify query "ScanVault SQLite index full scan transaction migration asset inventory WPF rescan history change detection" --budget 2500`.
- Useful Graphify findings: main flow crosses `MainViewModel.RescanAsync`, `LibraryScanService.ScanAsync`, `SqliteAssetIndex.ReplaceLibraryAsync`, `AssetSummary`, `AssetContentAnalyzer`, diagnostics and SQLite compatibility tests.
- CRG syntax confirmed with `code-review-graph --help`.
- CRG status/update used with UTF-8 stdout:
  `code-review-graph status`; `code-review-graph update --brief`.
- CRG updated on `codex/MLV-9-scan-history` at commit `36139b6b430f`; graph has 568 nodes, 1259 edges, 82 files.
- CRG initially hit a `cp1251` `UnicodeEncodeError` while printing after update; rerun with `PYTHONIOENCODING=utf-8` succeeded.

Graph results were treated as navigation only and checked against source.

## Current SQLite schema

Current schema version is `3`, normalization version is `3` in `SqliteAssetIndex`.

Current tables created by `CreateSchemaAsync`:

- `schema_info(version, normalization_version)`;
- `assets`, keyed by `id COLLATE NOCASE`, with canonical metadata, structured resolution, content inventory JSON, completeness and inventory projections;
- `tags`;
- `asset_tags`;
- `asset_inventory_maps`;
- `scan_state`, singleton last-success metadata.

Current indexes cover library root, folder, name, type, biome, region, completeness, inventory booleans/counts, map type, and tag lookup.

## Current full scan lifecycle

1. `MainViewModel.RescanAsync` validates index compatibility and settings, creates a linked CTS, reports progress, and awaits `ILibraryScanService.ScanAsync`.
2. `LibraryScanService.ScanAsync` normalizes the root using `PathPolicy.Normalize`.
3. `FileSystemScanner.DiscoverAsync` discovers JSON metadata paths.
4. `MegascansMetadataParser.ParseAsync` parses and normalizes metadata into `AssetSummary`.
5. `DuplicateAssetResolver.Resolve` keeps deterministic winners by ID and winner JSON path.
6. `AssetContentInventoryService.InventoryAsync` inventories winners with max parallelism 4, without reading mesh/texture payloads.
7. `SqliteAssetIndex.ReplaceLibraryAsync` atomically upserts current assets, replaces tag/inventory projections, deletes stale assets, updates `scan_state`, and promotes normalization version.
8. `MainViewModel` refreshes `allAssets` only after a successful scan result.

## Transaction boundary

`SqliteAssetIndex.ReplaceLibraryAsync` opens a writable connection, begins one SQLite transaction, loads previous asset identity rows, writes all current asset rows and projections, deletes stale rows, writes singleton `scan_state`, updates normalization marker, then commits. Cancellation or any exception rolls back the transaction and preserves the previous committed index.

MLV-9 must keep snapshots/change rows in this same transaction for completed scans. Running/cancelled/failed scan-run status needs separate writes before/after the scan because cancellation/failure can occur before the replacement transaction begins.

## Asset identity

Current replacement identity is `AssetSummary.Id`, case-insensitive in SQLite. Duplicate IDs are resolved before persistence; the lexicographically smallest normalized full JSON path wins and skipped duplicates are reported.

MLV-9 identity strategy:

1. Use non-empty normalized Megascans asset ID as `AssetIdentity`.
2. If an asset ID is empty or whitespace, fallback to a normalized relative JSON path under `LibraryRoot`.
3. Never use display name as identity.
4. ID collisions remain deterministic through existing `DuplicateAssetResolver` before snapshot generation.

## Library identity

No separate durable library ID exists today. Settings store only `LibraryRoot`; index rows store `library_root`.

MLV-9 will use normalized absolute root path from `PathPolicy.Normalize` as `LibraryIdentity`, trimming trailing separators consistently and relying on existing Windows ordinal-ignore-case comparisons/index collations.

## Existing Content Inventory model

`AssetContentInventory` stores variants, mesh LOD entries, texture sets/components, unclassified files, completeness, and issues. It includes full paths and relative/display file names but does not currently store file size or last-write timestamps. Fingerprint can derive file properties at persistence time from paths already present in metadata and inventory records, avoiding binary reads.

## Existing migration mechanism

- `InspectCompatibilityAsync` opens read-only and checks integrity, `schema_info`, required tables, structural version, inventory table, and normalization version.
- v1 migrates to v2 transactionally.
- v2 migrates to v3 transactionally.
- Current schema with older normalization is readable and requires explicit Rescan.
- Newer/corrupted schema blocks writes and preserves the database file.

MLV-9 will add v3 -> v4 forward migration and update `CurrentSchemaVersion` to 4.

## Cancellation and errors

- Scan cancellation is propagated through discovery, parse loop, inventory parallel loop, and replace transaction.
- `LibraryScanService` logs cancelled/failed and rethrows.
- `MainViewModel` sets user-visible cancelled/failed statuses and does not refresh visible assets on failure.
- `ReplaceLibraryAsync` rolls back on cancellation/failure.

MLV-9 must ensure failed/cancelled scan runs do not become baselines and previous completed baseline remains intact.

## Blast radius

Direct:

- Core models/contracts/policies for scan history, change kind/flags, fingerprint/change detection contracts.
- Infrastructure SQLite schema, migration, scan-run persistence, change detection and retention.
- `LibraryScanService` lifecycle to create running runs and mark cancelled/failed.
- App `MainViewModel`, `MainWindow`, new Scan History window/view model.
- Diagnostics formatter/models/docs.
- Unit/integration/ViewModel tests.

Adjacent:

- Existing scan counts and status text.
- Compatibility tests and migration fixtures.
- DI composition and app startup.
- WPF layout in top toolbar.

Out of scope:

- Binary hashing of textures/meshes.
- Editing, moving, deleting, restoring, or syncing source Megascans assets.
