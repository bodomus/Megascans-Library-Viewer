# ScanVault SQLite index format

The per-user database is `%LocalAppData%\ScanVault\scanvault.db`. Schema version 2 and normalization version 2 are recorded in `schema_info`. ScanVault uses Microsoft.Data.Sqlite.Core with the patched Windows system SQLite provider; pooling is disabled so disposed application/test connections do not retain file handles.

## Upgrade from version 1

Version 1 databases migrate in a transaction. The migration adds `normalization_version`, `raw_asset_type`, `resolution_width`, and `resolution_height`; a legacy scalar resolution is copied to both structured dimensions so the old index remains readable.

A populated version 1 index receives normalization marker 1 and the UI asks for an explicit corrective Rescan. An empty version 1 index can be marked current immediately. The marker becomes 2 only in the same transaction that successfully replaces the full asset set. Failed or cancelled correction therefore leaves both the previous usable rows and the old marker intact. Users never need to delete SQLite manually.

## Tables

### `schema_info`

One row containing:

- `version INTEGER` — structural schema version;
- `normalization_version INTEGER` — parser/canonical-data version.

Unsupported structural versions fail fast instead of being interpreted silently.

### `assets`

Primary key `id` (case-insensitive) plus library root, name/canonical type, optional raw type, physical asset and JSON paths, thumbnail/preview paths, biome, region, normalized physical size, legacy scalar maximum resolution, structured width/height, texel density, average color, categories/tags JSON, and source last-write timestamp.

The legacy `max_resolution` column remains during the version 2 compatibility window. New writes store its maximum dimension as well as `resolution_width` and `resolution_height`; reads prefer structured dimensions and fall back to the legacy scalar.

Indexes support library root, physical folder, name, type, biome, and region lookups. `json_path` is unique.

### `tags`

Normalized tag values keyed by `(kind, value)` with case-insensitive value identity. Tag kind remains part of domain identity.

### `asset_tags`

Many-to-many links from assets to tags with cascading foreign keys. An index on `tag_id` supports tag filtering.

### `scan_state`

Single row containing the committed library root, completion timestamp, and serialized scan result.

## Full-scan transaction

A temporary `current_scan_ids` table is created inside the transaction. Each resolved winner is upserted and its tag links replaced. Assets not present in the temporary table are counted and deleted only before the same transaction commits. Orphan tags are removed, `scan_state` is replaced, and `normalization_version` is promoted. Any parse-stage failure, cancellation, SQL failure, or pre-commit cancellation rolls the transaction back, leaving the previous usable index intact.

## Duplicate IDs

ID identity is case-insensitive. For every duplicate group, the winner is the lexicographically smallest normalized full JSON path using Windows ordinal-ignore-case comparison. Scan results retain the winner and every skipped full path. No physical files are changed.

## Future compatibility

Structural schema and normalization/parser versions are intentionally separate. Future column migrations can preserve readable rows while a new parser version requests an explicit full reparse before corrected metadata becomes authoritative.
