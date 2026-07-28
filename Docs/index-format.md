# ScanVault SQLite index format

The per-user database is `%LocalAppData%\ScanVault\scanvault.db`. Schema version 3 and normalization version 3 are recorded in `schema_info`. Connections are non-pooled and use the patched Windows system SQLite provider.

## Upgrade path

Version 1 first migrates transactionally to version 2 by adding normalization/raw-type/structured-resolution fields. Version 2 then migrates transactionally to version 3 by adding inventory storage and indexes. Existing populated rows receive an empty `Unknown` inventory and retain an older normalization marker, so they remain readable while the UI requests an explicit Rescan. Empty databases can become current immediately.

A successful full Rescan promotes normalization to 3 in the same transaction as catalog and inventory replacement. Cancellation or failure keeps the previous rows, inventory, scan metadata, and marker. No migration deletes or replaces the database manually.

## Read-before-write compatibility

Existing files are inspected through a private read-only connection before WAL or any writable pragma. Integrity, marker count, supported schema, required base tables, version-specific inventory table, and normalization are validated. Schema 1 or 2 follows the known migration chain; current schema with old normalization is readable and requires Rescan; newer/corrupted indexes are preserved and writes remain blocked.

## Tables

### `schema_info`

One structural `version` and one parser/domain `normalization_version`.

### `assets`

The existing canonical metadata columns remain. Version 3 adds:

- `inventory_json` — complete structured variants, LOD entries, texture sets/components, original paths, unclassified files, completeness, and issues;
- `completeness`, `has_fbx`, `has_lods`, `has_billboard`, `has_atlas` — indexed filter projections;
- `variant_count`, `lod_count`, `texture_set_count` — indexed/sort summary projections.

Indexes cover catalog fields plus completeness, FBX, LOD, Billboard, Atlas, and variant count. The JSON contains names and paths only, never file bytes.

### `asset_inventory_maps`

A normalized projection of `(asset_id, map_type, set_kind, resolution, format, path)` with a cascading asset foreign key. The `(map_type, asset_id)` index supports future SQL map predicates without parsing JSON. Full candidate/issue fidelity remains in `inventory_json`.

### `tags` / `asset_tags`

Normalized typed tags and their cascading many-to-many links.

### `scan_state`

The committed library root, completion timestamp, and stable successful-scan metadata. Older serialized `ScanResult` payloads remain readable.

## Full-scan transaction

A temporary `current_scan_ids` table tracks deterministic duplicate-ID winners. Each asset metadata row, inventory JSON, map projection, and tag relation is replaced inside one transaction. Stale assets cascade to inventory maps, orphan tags are removed, scan state is stored, and the normalization marker is promoted immediately before commit. Any error or cancellation rolls everything back.

## Identity and duplicates

Asset IDs and Windows path comparisons are case-insensitive. Duplicate asset IDs use the documented lexicographically smallest normalized JSON path. Content duplicates are never silently selected: all candidates remain in inventory JSON and produce an explanatory ambiguity issue. Source files are never changed.
