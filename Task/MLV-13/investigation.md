# MLV-13 Investigation

## Context

- Ticket: `MLV-13 Unreal Engine readiness`
- Branch: `master`
- Repository root: `J:/Projects/UE_Projects/Megascans Library Viewer`
- Starting commit: `70f3dc022cfe62857bf5beb1fb6add59484d30de`
- Local ticket copy: `Tickets/MLV-13.md`
- YouTrack state was moved to `In Progress`.

## Preflight

- Read `AGENTS.md` and `.codex/PRE_TICKET_WORKFLOW.md`.
- Used `graphify-repository-analysis` for repository orientation.
- Used `code-review-graph-analysis` for exact dependency and impact context.
- `code-review-graph status` showed stale graph state from an older commit.
- Ran `code-review-graph update --brief`; it completed and rebuilt FTS with 1047 rows.
- Graphify local output exists but is older. The previous ticket documented a Windows access-denied refresh failure, so Graphify findings are treated as orientation only and are validated against source.

## Source Findings

- Asset content inventory is represented by `AssetContentInventory` with mesh variants, texture sets, unclassified files, completeness and content issues.
- Current normalized asset types are `3D Asset`, `3D Plant`, `Atlas`, `Surface`, `Decal`, and `Brush`.
- Texture normalization already maps common Megascans names to albedo, normal, roughness, gloss, opacity, displacement, bump, ambient occlusion and related map types.
- Mesh inventory currently supports `.fbx` and `.abc`, with variants and LOD numbers parsed from filenames.
- Existing completeness is intentionally generic and not Unreal-specific. MLV-13 should add a separate rule-based readiness layer rather than overload `AssetCompletenessStatus`.
- Common filtering flows through `AssetFiltering`, then `MainViewModel`; smart collections have their own policy model but reuse the same asset summary and content inventory.
- Persistence uses SQLite schema version 6. Asset rows store inventory JSON and indexed inventory counters, but no readiness columns.
- History fingerprints currently include metadata and inventory JSON. Readiness changes need to be recorded by adding readiness to the snapshot and a dedicated change flag.
- Reports, comparison, diagnostics, and asset card view models all project from `AssetSummary`, so adding readiness there gives the cleanest integration path.

## Real Examples Available

No user Megascans library was scanned or modified for this investigation. The available project examples are deterministic tests and the actual parser/normalizer rules:

- `AssetContentAnalyzer` handles `3D Asset`, `3D Plant`, `Atlas`, `Surface`, and `Brush`.
- `MetadataNormalizer` additionally recognizes `Decal`.
- Existing tests create synthetic `3D Plant`, `3D Asset`, `Atlas`, and `Surface` inventories, which are suitable fixtures for order-independent rule tests.

## Risks

- UI filter space is already compact; readiness filters should use the common pipeline without crowding details.
- Smart collection schema versioning must not break existing saved v1 definitions.
- Persistence migration should mark old readiness as stale instead of forcing a destructive index rebuild.
- Readiness timestamps are intentionally non-deterministic, but rule outcomes and reason order must remain deterministic.
