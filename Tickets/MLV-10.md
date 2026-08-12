# MLV-10 — Asset comparison

Canonical ticket: https://bodomus.youtrack.cloud/issue/MLV-10

## Summary

Add a read-only Asset Comparison workflow for exactly two assets from the current ScanVault library. The comparison must use indexed metadata and inventory, preserve the existing single-selection card UX, remain responsive, and never rescan or modify the library, metadata, or SQLite index.

## Required workflow and UI

- Make the two selected assets and the states “select first”, “select second”, and “ready” explicit.
- Reject comparing an asset with itself. A deterministic third selection must either be rejected or replace an existing side.
- Provide an **Asset Comparison** window with `Overview`, `Variants and LODs`, `Texture Sets`, `Files`, and `Issues` tabs.
- Provide `Show differences only`, `Swap`, `Replace left`, `Replace right`, and existing open asset/folder/content-inventory actions.
- Use text labels in addition to restrained color emphasis. Support keyboard navigation, Esc, and a compare shortcut.
- Virtualize large row lists and show loading, missing, stale, no-differences, and per-side error states.

## Comparison semantics

Use explicit normalized domain values and these outcomes: `Equal`, `Different`, `OnlyLeft`, `OnlyRight`, `Unknown`, `NotApplicable`, and `Ambiguous`. Missing, unknown, and not applicable are distinct.

Compare metadata, type, resolution, texel density, completeness, counts, FBX/ABC, Atlas/Billboard, variants, LODs, texture maps, logical files, and issues. Match variants and LODs by normalized identifiers, texture maps by normalized set/map properties, files by a normalized logical key rather than absolute path, and issues by stable issue code. Collection ordering must not affect results and duplicate/ambiguous keys must remain visible.

## Data and performance constraints

- Reuse `AssetSummary` / `AssetContentInventory` and existing interaction, preview, and inventory flows.
- Load/build both sides asynchronously with cancellation; do not read mesh or texture payloads.
- Limit work to the two selected identities and avoid N+1 queries.
- Keep a stable opening snapshot; after Rescan show stale state and allow Refresh.
- Log opened, loaded, failed, refreshed, and swapped events with IDs, file counts, difference count, and duration without Information-level absolute paths.
- Measure left/right file count, load duration, comparison duration, and total render-ready duration.

## Acceptance and validation

Selection count and same-asset rules, normalized comparison states, all five tabs, differences-only filtering, swap/replace/open actions, stale refresh, cancellation, ambiguity, large inventory, and regression of Content Inventory/search/sort/smart collections/history/export must be tested. Run restore, Release build, all tests, format verification, `git diff --check`, post-change CRG impact analysis, and refresh Graphify when architectural relationships change.

> Asset Comparison uses indexed metadata and inventory. It does not compare binary mesh or texture contents.

