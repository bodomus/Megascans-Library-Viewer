# ScanVault SQLite index format

The per-user database is `%LocalAppData%\ScanVault\scanvault.db`. Schema version 1 is recorded in `schema_info`. ScanVault uses Microsoft.Data.Sqlite.Core with the patched Windows system SQLite provider; pooling is disabled so disposed application/test connections do not retain file handles.

## Tables

### `schema_info`

One `version INTEGER` row. Unsupported versions fail fast instead of being interpreted silently.

### `assets`

Primary key `id` (case-insensitive) plus library root, name/type, physical asset and JSON paths, thumbnail/preview paths, biome, region, physical size, maximum resolution, texel density, average color, categories/tags JSON, and source last-write timestamp.

Indexes support library root, physical folder, name, type, biome, and region lookups. `json_path` is unique.

### `tags`

Normalized tag values keyed by `(kind, value)` with case-insensitive value identity.

### `asset_tags`

Many-to-many links from assets to tags with cascading foreign keys. An index on `tag_id` supports future tag filtering.

### `scan_state`

Single row containing the committed library root, completion timestamp, and serialized scan result.

## Full-scan transaction

A temporary `current_scan_ids` table is created inside the transaction. Each resolved winner is upserted and its tag links replaced. Assets not present in the temporary table are counted and deleted only before the same transaction commits. Orphan tags are removed and `scan_state` is replaced. Any parse-stage failure, cancellation, SQL failure, or pre-commit cancellation rolls the transaction back, leaving the previous usable index intact.

## Duplicate IDs

ID identity is case-insensitive. For every duplicate group, the winner is the lexicographically smallest normalized full JSON path using Windows ordinal-ignore-case comparison. Scan results retain the winner and every skipped full path. No physical files are changed.

## Future compatibility

Schema 1 keeps asset identity and tags separate so texture components, LODs/variants, and virtual groupings can be added by explicit future schema migrations without changing source library data.