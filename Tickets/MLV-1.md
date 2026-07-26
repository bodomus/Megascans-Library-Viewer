# MLV-1 — Bootstrap ScanVault and implement the first usable Megascans library viewer

Source: https://bodomus.youtrack.cloud/issue/MLV-1

## Goal

Create a Windows-only C#/.NET WPF/MVVM desktop application that lets the user explicitly configure a legacy Megascans library root, recursively scans tolerant JSON metadata, transactionally builds a per-user SQLite index, and browses indexed assets through a physical folder tree, virtualized thumbnail grid, hover metadata popup, and in-application large preview.

## Required behavior

- Latest stable installed .NET SDK and non-preview C#; WPF, MVVM, Microsoft DI/logging, SQLite, xUnit.
- Settings live outside the repository; changing the root requires explicit save and rescan.
- Startup loads an existing index and never automatically scans.
- Recursive traversal is deterministic, cancellable, avoids reparse loops, and reports inaccessible folders.
- Malformed/unrelated JSON and missing fields are skipped or represented safely without aborting the complete scan.
- Extract asset identity/name/type, categories, biome/region, physical size, resolution, texel density, average color, typed tags, source/folder paths, image paths, and last-write time.
- Resolve thumbnail as metadata thumb, metadata preview, known local ID pattern, then placeholder.
- Resolve large preview as best preview, retina, bake, thumb, then placeholder; never decode large texture maps for the grid.
- SQLite has explicit schema version, indexed queries, assets/tags/relations/scan state, transactional upsert and stale deletion, and rollback preservation.
- The folder tree contains only indexed ancestors and filters the grid to a selected folder plus descendants.
- The grid virtualizes, loads images asynchronously, invalidates recycled requests, bounds memory, and does not retain file locks.
- Hover appears after about 350–500 ms without focus stealing/stale flicker; preview preserves aspect and closes by `Esc`, close button, or backdrop.
- UI prevents simultaneous scans, reports phases/counts, supports cancellation, and remains responsive.
- Structured logging covers startup, configuration, scan lifecycle, parse/access errors, database lifecycle/failures, and image failures.

## Confirmed project decisions

1. Ticket and artifact identifier is `MLV-1`; obsolete `SCAN-1` references are corrected.
2. Duplicate-ID winner is the lexicographically smallest normalized full JSON path under Windows ordinal-ignore-case comparison. Reports must list the winner and the full path of every skipped copy.
3. Stale assets are removed only inside a successful full-scan transaction; failure/cancellation preserves the previous index.
4. Required repository skills live only in `.agents/skills/`; obsolete top-level `skills/` is removed.
5. Git branch remains `master` by project convention.
6. Code is under `src/`; completion reports are under `review/review-<TICKET>.md`.

## Deliverables

`ScanVault.sln`, production projects under `src/`, tests under `tests/`, adapted `AGENTS.md`, workflow and both graph skills, `README.md`, architecture/index docs, investigation/plan/implementation report under `Task/MLV-1/`, and `review/review-MLV-1.md`.

## Non-goals

No Unreal/Fab integration, importing, material/LOD/Billboard work, asset editing/moving/deleting, virtual grouping trees, texture-map tabs, similarity search, cloud sync, botanical inference, plugin system, installer/updater, or mesh-derived thumbnails.

## Acceptance

Release restore/build/tests pass with nullable analysis enabled; deterministic tests cover policies, parser, scanner, cancellation, preview resolution, schema/upsert/removal/rollback, settings and view-model state; Graphify and CRG are refreshed and inspected; limitations/manual validation are reported honestly; source Megascans files and machine-specific paths are never committed or modified.