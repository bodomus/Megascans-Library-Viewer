# MLV-7 вЂ” Investigation

## Workflow and repository state

- Workflow level: 2 structural feature.
- Repository root: `J:\Projects\UE_Projects\Megascans Library Viewer`.
- Branch: `master`.
- Initial commit: `7707bfdc839c`.
- Initial worktree: clean. The user-owned `.graphifyignore` is tracked by the initial commit and remains unchanged.
- YouTrack: Feature / In Progress / Root / PreRelease / assignee ChatGPT / estimate 4h.

## Graph evidence

Graphify was queried for inventory, variant/LOD, texture, scanner, SQLite, card, hover, filtering, sorting, and test concepts. It identified `LibraryScanService`, `FileSystemScanner`, `AssetSummary`, `SqliteAssetIndex`, `AssetFiltering`, `AssetSorting`, `MainViewModel`, `AssetCardViewModel`, `MainWindow.xaml`, and their tests as the direct surface. Source inspection confirmed the candidates.

CRG was updated at `7707bfdc839c` and is current with 484 nodes, 1026 edges, and 72 C# files. The concrete blast radius crosses Core models/policies/contracts, Infrastructure parsing/scanning/persistence, App view models/WPF, and all three test projects. Graph output is a candidate generator; source, tests, build output, and runtime evidence remain authoritative.

## Existing behavior

- The parser creates one immutable `AssetSummary` per recognized JSON and normalizes metadata only.
- `LibraryScanService` discovers JSON, parses sequentially, resolves duplicate IDs, then replaces the catalog transactionally.
- `FileSystemScanner` performs deterministic asynchronous JSON traversal, skips reparse points, and reports inaccessible directories.
- SQLite schema/normalization version 2 persists assets, tags, and scan state. No content tables or inventory query projections exist.
- Search uses metadata fields; sorting has eight deterministic modes; filtering is physical-folder-only.
- Cards and the bounded hover popup expose metadata and previews. Context actions cover preview/folder/path only.
- Settings persist library root and sort mode.
- Tests cover metadata normalization, scanner/parser/index behavior, view-model composition, and WPF smoke checks, but not asset contents.

## Read-only evidence from `tests/Megascan`

The supplied library contains `3D`, `3dplant`, `atlas`, and `brush` roots. Observed patterns include flat `*_LOD0.fbx` meshes, plant `Var1` through at least `Var7`, LOD0 through LOD4, paired FBX/ABC files, `Textures/Atlas`, and maps such as Albedo, AO, Bump, Cavity, Displacement, Gloss, Normal, Opacity, Roughness, Specular, Translucency, and Brush. Filenames use 4K/8K tokens and mixed asset-name prefixes. This validates arbitrary numeric variant/LOD parsing and folder-first set classification. These source assets are read-only evidence, not automated fixtures.

## Selected architecture

- Core owns immutable inventory records/enums, filename classification, completeness policy, predicates, search, and sorting.
- Infrastructure owns bounded filesystem enumeration, JSON-reference extraction, logging, and transactional SQLite serialization/projections.
- App owns compact presentation summaries, badges, persisted filter state, detailed read-only view, and desktop copy/open actions.
- Dependency direction remains `App -> Infrastructure -> Core`.

Inventory is attached to `AssetSummary` as a defaulted init property to retain constructor compatibility. The scanner inventories only duplicate-ID winners. A bounded worker pool produces a deterministically ordered result array before the existing single replacement transaction.

## Parsing and duplicate policy

- Split names and relative path segments case-insensitively on non-alphanumeric separators.
- Variant/LOD tokens must match complete `VarN`/`LODN` tokens; multiple conflicting tokens are not guessed.
- Folder context outranks filename for Atlas/Billboard; otherwise known texture maps become General, and unrecognized image files become Unknown/unclassified.
- Map aliases normalize to one semantic enum while every original path and raw token remains available.
- Duplicate keys preserve every entry and create an ambiguity issue. No hidden file winner is selected.
- Missing metadata references create explicit issues only for content-like paths; previews and the asset JSON itself are excluded.

## Confirmed completeness policy

The user confirmed the exact policy recorded in `Tickets/MLV-7.md`: FBX/LOD0 and type-specific texture alternatives define Complete; ABC-only may be Usable; plant/Atlas require Opacity; Billboard is optional; ambiguity has priority. Filters persist with settings.

## Persistence design

Schema v3 adds full structured inventory JSON to each stable asset row, indexed scalar summary projections, and a normalized map-type projection with a cascading foreign key. This keeps duplicate candidates/issues lossless while satisfying filter/map query indexes without a large join graph. Migration creates empty Unknown inventory for existing assets and leaves normalization behind current so a Rescan is explicitly required. Full replacement deletes stale projections through asset cascade and promotes normalization only inside the successful transaction.

## Risks and controls

- Asset subtrees may contain many files: enumerate names only, bound workers, avoid image/mesh content reads, and check cancellation frequently.
- Nested or malformed layouts may conflict: classify conservatively and expose ambiguity/unclassified records.
- SQLite model expansion can regress compatibility: keep known migrations transactional and add migration/restart/rollback tests.
- UI density can regress virtualization: precompute small summary strings/badges in view models and keep details in a separate bounded window.
- Manual WPF interaction will not be claimed unless actually executed.
