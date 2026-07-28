# MLV-7 — Implementation plan

## Objective

Add deterministic, cancellable, read-only content inventory to the existing full Rescan, persist it transactionally, and expose concise and detailed catalog navigation without changing the established dependency direction or source assets.

## Steps

1. Add Core inventory records/enums, summary predicates, filename/set/resolution parsing, duplicate detection, and the confirmed per-type completeness policy.
2. Extend Core contracts, `AssetSummary`, scan phases/results, filtering, search, sort modes, and persisted settings in backward-compatible form.
3. Add an Infrastructure inventory service that enumerates asset folders with bounded concurrency, cancellation, reparse-point/inaccessible-folder handling, deterministic ordering, and name-only analysis.
4. Extract content-like JSON references during metadata parsing and integrate inventory after duplicate-ID resolution but before the existing atomic commit.
5. Migrate SQLite schema v2 to v3 and persist summary projections plus normalized mesh/texture/issue/unclassified rows in the catalog transaction. Rehydrate full inventory on reads and retain rollback/stale-row guarantees.
6. Add structured inventory logging, scan progress, scan-result counters, compatibility/normalization handling, and safe legacy-index behavior.
7. Extend `AssetCardViewModel` with 3–5 badges, warning, hover summaries, issue count, and a detailed-inventory command.
8. Add persisted composable inventory filters and four inventory sort modes while preserving folder/search/sort composition and selection.
9. Add a bounded read-only WPF inventory window grouped by variants/LODs, texture sets/files, unclassified files, and issues, with copy/open-folder actions through the existing desktop abstraction.
10. Add sanitized Core/Infrastructure/App/WPF tests for classification, completeness, persistence/migration/rollback, badges/details, and composed catalog navigation.
11. Update README and architecture/index/content-inventory documentation; record a read-only timing observation on `tests/Megascan`.
12. Run targeted and full Release restore/build/test/format/diff validation. Update CRG, inspect impact, and refresh Graphify.
13. Create implementation/review reports, update YouTrack fields/comment, and attach the exact local `Tickets/MLV-7.md`.

## Safety gates

- Never modify, move, delete, rename, or fully decode any Megascans source file.
- No `.Result`, `.Wait()`, unbounded task creation, fire-and-forget work, or UI-thread traversal.
- No stale-row deletion or normalization promotion outside the successful full-scan transaction.
- Every ambiguity retains all candidates and an explanatory issue.
- Preserve unrelated user changes and remain on `master`.
