# MLV-4 — Implementation plan

## 1. Canonical Core model and policies

1. Add `ImageResolution` with width, height, maximum dimension, and deterministic formatting.
2. Add an enum-backed `AssetSortMode`; display labels remain separate from logic keys.
3. Extend `LibrarySettings` with the persisted sort mode using `Name A–Z` as the backward-compatible default.
4. Add one metadata-normalization policy for optional strings, asset-type mapping/precedence inputs, stable lists, physical size, texel density, and resolution parsing.
5. Preserve tag groups while trimming placeholders and stable case-insensitive duplicates.
6. Add descendant-inclusive counts to `FolderNode` and deterministic sort/filter policy with missing values last and ID/folder tie-breakers.

## 2. Parser correction

1. Replace recursive generic `type` extraction with explicit top-level and `semanticTags.asset_type` reads.
2. Resolve canonical asset type by the ticket priority and reject component/map types.
3. Normalize categories independently and preserve stable source order.
4. Collect resolution only from explicit maximum fields and map/component collections; log malformed candidates.
5. Normalize physical size, texel density, optional strings, and grouped tags from known schema variants.
6. Preserve exact asset ID and optional raw asset-type source for diagnostics.

## 3. Existing-index upgrade

1. Migrate SQLite schema v1 to v2 transactionally.
2. Add `raw_asset_type`, `resolution_width`, and `resolution_height` while retaining scalar `max_resolution` for compatible fallback reads.
3. Add a normalization version to `schema_info`.
4. Keep migrated v1 rows readable and expose `RequiresNormalizationRescan` through `IAssetIndex`.
5. Advance the normalization version only inside a successful full replacement transaction.
6. Show a concise startup/rescan-required message without deleting or automatically replacing the old index.

## 4. Catalog interaction

1. Persist an enum-backed sort selector beside search and compose sort with folder/search filtering.
2. Search name, ID, type, categories, grouped tags, biome, and region.
3. Preserve selected asset by stable ID plus JSON path during sort/filter refresh; clear it when absent.
4. Display folder descendant counts from indexed assets.
5. Redesign card hierarchy as name, type/categories, and compact resolution/explicit ID.
6. Make single click select; use double-click/context action/Enter to open preview.
7. Add selected and keyboard-focus visuals.
8. Keep delayed hover cancellation and render only meaningful optional rows within constrained popup dimensions.

## 5. Context actions and keyboard

1. Add an App-layer desktop interaction service for Clipboard and Explorer.
2. Add Open preview, Open folder, Copy ID, Copy folder path, and Copy JSON path context actions.
3. Catch failures, log structured details, and publish concise status text.
4. Keep arrow navigation in the focused `ListBox`, bind Enter to preview, scope Ctrl+C to the card list, preserve normal copy in search, and restore card-list focus after preview closes.

## 6. Tests, documentation, and validation

1. Add deterministic unit tests for normalization, resolution, physical size, tags, sorting, search composition, selection preservation, folder counts, settings persistence, context commands, and schema upgrade/cancellation.
2. Extend WPF template regression coverage for the redesigned card.
3. Update `README.md`, `Docs/architecture.md`, and `Docs/index-format.md`.
4. Run targeted tests, formatter verification, full Release restore/build/test, and a read-only manual scan against `tests/Megascan` without changing source data.
5. Update CRG and inspect blast radius; refresh Graphify because the domain/persistence/read-model relationships change.
6. Produce `Task/MLV-4/implementation-report.md` and `review/review-MLV-4.md`, update YouTrack, attach the correctly named local specification, and commit only MLV-4 files.

