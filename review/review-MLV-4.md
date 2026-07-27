# Review MLV-4

## Outcome

MLV-4 (`SCAN-2`) is implemented. ScanVault now normalizes legacy Megascans metadata before persistence/display, upgrades existing SQLite indexes without manual deletion, and provides practical catalog selection, sorting, search, folder counts, context actions, and keyboard navigation.

## Main corrections

- `normal`, `specular`, and other component/map types are excluded from asset classification.
- Asset type uses documented deterministic precedence and canonical display values.
- Maximum resolution is a structured width/height value selected across all maps; `4096x4096` displays as `4K (4096 × 4096)` and non-square dimensions are preserved.
- Physical size and texel density are normalized invariantly.
- `undefined`, `null`, `N/A`, empty values, and accidental `unknown` placeholders are omitted from optional UI rows.
- Categories and typed tag groups are trimmed/deduplicated with stable order; original asset ID casing is preserved and displayed as `ID:`.

## Index compatibility

SQLite schema v2 adds structured resolution and raw type columns plus a separate normalization marker. Populated v1 indexes migrate automatically but remain marked for corrective Rescan. A successful replacement promotes the marker atomically; cancellation or failure keeps the previous rows and marker usable.

## UI and catalog navigation

- Clear persistent single-card selection and keyboard focus styling.
- Card hierarchy: preview → name → type/category → compact resolution/ID.
- Conditional, bounded hover popup with the existing cancellable 425 ms delay.
- Eight persistent enum-backed sort modes.
- Search across all required normalized fields.
- Descendant-inclusive folder counts from the indexed read model.
- Context menu for preview, Explorer, ID/folder/JSON copy.
- Arrow navigation, `Enter`, `Esc`, and asset-list-only `Ctrl+C`.

## Files and documentation

- Specification: `Tickets/MLV-4.md`.
- Investigation: `Task/MLV-4/investigation.md`.
- Plan: `Task/MLV-4/implementation-plan.md`.
- Full implementation/validation report: `Task/MLV-4/implementation-report.md`.
- Maintained docs updated: `README.md`, `Docs/architecture.md`, `Docs/index-format.md`.

## Validation result

- Release restore: passed.
- Release build: passed, 0 warnings / 0 errors.
- Release tests: 55 passed / 0 failed / 0 skipped.
- Formatter verification: passed.
- `git diff --check`: passed.
- Read-only production scan/parser smoke test on `tests/Megascan`: passed; fixture remained clean.
- CRG refreshed and impact reviewed: 325 nodes / 659 edges.
- Graphify refreshed because architectural relationships changed: 1016 nodes / 1724 edges / 66 communities.

## Copies and source safety

- Megascans source copies: none.
- Megascans source modifications: none.
- Persistent backup copies: none.
- Smoke-test SQLite/cache existed only in an automatically deleted OS temporary directory.

## Remaining acceptance check

Interactive GUI behavior was not claimed without running the application against the user's external per-user settings/database. A short visual pass remains for live hover feel, multi-monitor popup placement, actual clipboard/Explorer actions, all sort selections, and restart persistence.
