# MLV-7 — Asset content inventory and completeness

Source: https://bodomus.youtrack.cloud/issue/MLV-7/Asset-content-inventory-and-completeness

## Goal

Turn ScanVault from a metadata viewer into a read-only analyzer of legacy Megascans asset folders. For every indexed asset, discover mesh variants, LODs and formats; texture maps and resolutions; Atlas and Billboard sets; missing, duplicate or ambiguous content; and an explicit completeness state.

The scan must never rename, move, delete, rewrite, decode in full, import, or repair source assets.

## Mandatory workflow

This is a Level 2 structural ticket. Resolve the repository root, read `AGENTS.md` and `.codex/PRE_TICKET_WORKFLOW.md`, run Graphify and code-review-graph preflight, validate graph candidates against source/tests, create `Task/MLV-7/investigation.md` and `Task/MLV-7/implementation-plan.md`, then implement. After implementation update CRG, inspect impact, run validation, refresh Graphify, and create the implementation and review reports.

## Discovery and inventory

- Tolerate bracketed/unbracketed `Textures`, `Atlas`, `Billboard`, `VarN` folders, case differences, flat layouts, additional nesting, missing optional folders, and alternate separators.
- Recognize `.fbx` and `.abc` meshes. Parse `VarN` and `LODN` conservatively and case-insensitively, preserving original paths. Support more than three variants and four LODs; report unclassified meshes separately.
- Recognize `jpg`, `jpeg`, `png`, `tif`, `tiff`, `exr`, and `tga` textures without full image decoding.
- Normalize map aliases while retaining raw names: BaseColor/Diffuse → Albedo, Alpha → Opacity, AmbientOcclusion → AO, Height → Displacement. Do not merge distinct maps.
- Recognize Albedo, Normal, Roughness, Gloss, Specular, Opacity, Translucency, AO, Cavity, Displacement, Bump, and Brush maps.
- Parse filename resolutions including 1K/2K/4K/8K and 512/2048/4096 when present.
- Classify sets as Atlas, Billboard, General, or Unknown with priority: folder context, filename, JSON reference, conservative fallback. A sibling Billboard folder must not reclassify unrelated textures.
- Preserve all duplicate candidates and report duplicate variant+LOD+format, map+resolution+format, conflicting aliases, case-only duplicates, malformed names, and multiple JSON candidates deterministically.

## Domain model and confirmed completeness policy

Introduce explicit structured inventory entities for variants/LODs, texture sets/components, unclassified files, issues, and `Complete`, `Usable`, `Partial`, `MissingCriticalFiles`, `Ambiguous`, `Unknown` states. Keep Core independent of WPF, SQLite, and Unreal.

Confirmed by the user on 2026-07-28:

- 3D Asset Complete: FBX + LOD0 + Albedo + (Normal or Bump) + (Roughness or Gloss).
- 3D Plant Complete: the 3D Asset requirements plus Opacity.
- Atlas Complete: Albedo + Opacity + (Normal or Bump) + (Roughness or Gloss).
- Surface Complete: Albedo + (Normal or Bump) + (Roughness or Gloss).
- Brush Complete: Brush map.
- ABC without FBX is inventory content and may be `Usable`, but never `Complete`.
- Billboard is optional for usability. JPG may satisfy a component when EXR is absent. Roughness and Gloss are alternatives; Normal and Bump are alternatives.
- A real unresolved duplicate/conflict makes the asset `Ambiguous` and takes priority over other states.
- Filters are persisted consistently with current sort settings.

`Usable` means the primary visual content exists but some Complete requirement is absent. `Partial` means recognized content exists but cannot satisfy the usable profile. `MissingCriticalFiles` means no critical primary content (for example no mesh for a 3D asset, no Albedo, no Opacity for foliage, or a referenced file is absent). `Unknown` is used when no known asset-kind profile applies.

## Persistence and scan behavior

- Migrate SQLite transactionally and keep old indexes readable until the supported migration succeeds.
- Persist structured inventory by stable asset ID; never store file contents.
- Replace inventory in the same full-scan transaction as catalog rows; stale rows disappear only after a successful scan. Cancellation/failure preserves the previous catalog and inventory.
- Provide indexed predicates for asset ID, completeness, has FBX, has Billboard, has LODs, map type, and variant count.
- Inventory asynchronously with bounded concurrency, cancellation, deterministic results, inaccessible-directory tolerance, and no UI-thread traversal.
- Add a separate inventory progress phase and summary counts for inventoried assets, meshes, textures, ambiguous assets, and missing-critical assets.

## UI, filtering, sorting, and search

- Cards show only 3–5 useful text badges such as FBX, LOD4, VAR3, ATLAS, BILLBOARD, or 4K and a clear incomplete warning.
- Hover popup shows concise counts/summaries, completeness, and issue count without dumping filenames.
- Add a read-only “Show content inventory” view grouped into variants/LODs, texture sets, files, and issues. Paths are copyable and containing folders can be opened; no editor or renderer is added.
- Add composable filters: Has FBX, Has LODs, Has Billboard, Has Atlas, Complete, Incomplete, Ambiguous.
- Add sorts: completeness, variant count, LOD count, texture-set count.
- Extend search to filenames, map names, completeness, issue text, variants, and LOD labels.
- Preserve existing folder filtering, search, deterministic sorting, and selection preservation.

## Tests and documentation

Use sanitized tiny fixture trees only for automated tests. Cover filename parsing, aliases, folder classification, all confirmed completeness profiles, duplicates, unknown types, migrations, replacement/stale removal/cancellation, query predicates, badges, popup/details grouping, filter/sort composition, selection, and issue count.

Update `README.md`, `Docs/architecture.md`, `Docs/index-format.md`, and add `Docs/content-inventory.md`. Record measured behavior on read-only test data and do not claim interactive UI validation unless it was actually performed.

## Acceptance criteria

- Structured read-only mesh/texture inventory is deterministic and conservative.
- Variants, arbitrary LODs, FBX/ABC, normalized maps, resolutions, Atlas, and Billboard are represented with original paths retained.
- Duplicate and ambiguous candidates are retained and explained.
- Completeness follows the confirmed per-kind policy.
- SQLite migration and full replacement are transactional; cancellation/failure preserves the old index.
- Cards, hover summary, detailed view, filters, sorts, and search expose inventory without UI blocking.
- Release restore/build/test and formatting validation pass.
- CRG and Graphify are refreshed and reviewed.
- Required investigation, plan, implementation, documentation, and review artifacts exist.

## Explicit non-goals

No Unreal import, material creation, LOD generation/assembly, Billboard generation, mesh rendering/conversion, deep FBX parsing, image editing, thumbnails from meshes, file repair/rename/move/delete, Fab API, cloud sync, installer, release workflow, or cosmetic redesign.
