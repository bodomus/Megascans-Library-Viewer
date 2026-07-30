# MLV-8 Saved filters and smart collections

Source: https://bodomus.youtrack.cloud/agiles/183-15/current?issue=MLV-8

## Summary
Add saved filters and dynamic smart collections for ScanVault.

## Key requirements
- Save current search/filter/folder/sort combination as a user smart collection.
- Collections store conditions, not asset IDs, and recalculate from the current index after Rescan/index changes.
- Built-in read-only collections: All Assets, Complete Assets, Assets With Issues, Missing Mesh, Missing LODs, Atlas Assets, Billboard Assets.
- Recently Added may be included only if scan history from MLV-9 is correctly integrated; do not fake it from timestamps.
- Criteria v1: search text, asset type, folder scope, content flags, completeness, issues, min/max resolution, and other stable current UI filters where supported.
- Folder scopes: Entire library, Current folder at execution time, Specific saved folder. Specific saved folder stores a normalized relative path inside the library root and missing folder remains visible with warning.
- AND match mode only.
- User actions: create, apply, rename/edit, update conditions, duplicate, delete, reorder.
- Names are required, trimmed, case-insensitive unique per user scope, and length-limited.
- Counts update from current index without blocking UI and invalidate after index/definition changes.
- Active collection has Manual/Modified behavior when UI criteria change.
- Persistence must use stable definitions with DefinitionVersion; unknown/corrupted data must not crash the app.
- Do not duplicate filter/completeness logic. Do not modify Megascans library files.

## Workflow
Follow PRE_TICKET_WORKFLOW: source validation, CRG, Graphify if possible, investigation, implementation plan, validation, review report.
