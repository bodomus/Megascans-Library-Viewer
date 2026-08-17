# MLV-12 Investigation

## Repository state

- Root: `J:/Projects/UE_Projects/Megascans Library Viewer`
- Branch: `master`
- Initial commit: `4bf6f5680365637ba5197f6a6eeb7b594728f0ee`
- Initial working tree: clean.
- Workflow level: Level 2, because this adds persistence, service logic, UI, and tests.

## Graph preflight

- Graphify command: `graphify query "ScanVault asset identity content inventory fingerprints file metadata SQLite migrations duplicate detection UI entry points tests" --budget 4000`
- Graphify status: usable existing graph at `graphify-out/graph.json`, last generated 2026-08-05.
- CRG command: `code-review-graph update --brief`
- CRG status: incremental update completed for 2 files; first run hit a cp1251 display error while printing a panel, then `PYTHONIOENCODING=utf-8` `detect-changes --brief` succeeded.

## Current behavior

- `DuplicateAssetResolver` groups parsed assets by normalized `AssetSummary.Id`, picks the lexicographically smallest full JSON path as the winner, and only winner assets are persisted in `assets`.
- `ScanResult.DuplicateGroups` reports skipped JSON paths for same-ID duplicates, but skipped copies are not browsable in the normal index.
- Content inventory is stored on `AssetSummary.Content` and persisted as `inventory_json`; indexed fast fields include completeness, `has_fbx`, `has_lods`, `has_billboard`, `has_atlas`, `variant_count`, `lod_count`, and `texture_set_count`.
- MLV-9 fingerprints are scan-history fingerprints built from normalized metadata, inventory JSON, and file facts `(relative path, size, last write UTC)`.
- SQLite current schema is version 4 with migrations from v1/v2/v3 and history tables.
- MLV-10 comparison is `AssetComparisonViewModel` plus `AssetComparisonPolicy.Compare`.
- MLV-11 export is `ExportReportViewModel` and report writers; group export can hand off to these patterns later.

## Real library scope

The application indexes a user-selected filesystem root and must never rely on or mutate the user's real library in tests. Duplicate analysis must operate on the persisted/current index and hash only candidate files.

## Constraints

- Core remains independent of WPF and SQLite.
- Infrastructure owns SQLite and filesystem hashing.
- Hashing must be cancellable, streaming, bounded in parallelism, and off the dispatcher.
- Cancelled/failed duplicate runs must not replace the latest completed run.
- Different library roots must be separated by normalized library identity.
- Rescan must mark duplicate analysis stale without automatically rehashing the whole library.
