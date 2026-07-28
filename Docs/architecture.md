# ScanVault architecture

## Boundaries

```text
ScanVault.App -> ScanVault.Infrastructure -> ScanVault.Core
      |-----------------------------------> ScanVault.Core
```

`Core` has no WPF or SQLite dependency. `Infrastructure` implements Core contracts. `App` is the WPF composition root and contains presentation-only behavior.

## Metadata and scan flow

1. The user saves a valid library root and explicitly starts Rescan.
2. `MainViewModel` prevents concurrent scans and exposes cancellation/progress.
3. `LibraryScanService` asks `FileSystemScanner` to discover JSON files on a worker thread.
4. `MegascansMetadataParser` reads known schema locations without an unrestricted recursive `type` lookup. `MetadataNormalizer` applies explicit asset-type precedence, optional-value cleanup, structured resolution selection, physical-size normalization, and typed tag normalization.
5. `DuplicateAssetResolver` groups IDs case-insensitively; the lexicographically smallest normalized full JSON path wins and all copies are recorded.
6. `SqliteAssetIndex.ReplaceLibraryAsync` upserts canonical values, replaces tag relations, removes stale rows, promotes the normalization marker, and stores scan state in one transaction.
7. Only a successful commit replaces visible view-model data. Cancellation or failure preserves the previous index and its normalization marker.

The normalized flow is:

```text
Raw Megascans JSON
        -> schema-aware parser
        -> Core normalization policies
        -> canonical AssetSummary / SQLite index
        -> filtering and deterministic sorting
        -> view-model display formatting
        -> WPF layout
```

## Asset-content inventory flow

After duplicate-ID resolution, `LibraryScanService` inventories only the deterministic winners. A bounded `Parallel.ForEachAsync` worker pool (maximum four assets) calls `AssetContentInventoryService`; each worker walks only its asset subtree, skips reparse points, records inaccessible directories, and passes file names/relative paths to the pure Core `AssetContentAnalyzer`. Mesh or image payloads are never opened.

The analyzer uses complete case-insensitive `VarN`, `LODN`, map, resolution, Atlas, and Billboard tokens. It preserves every original full path, groups variants and texture sets, retains unclassified files, and emits explicit issues rather than guessing conflicts. JSON content-like references can supply set context and missing-reference evidence. The confirmed completeness profile is type-specific and lives in Core.

Inventory returns to the original deterministic asset array before `SqliteAssetIndex.ReplaceLibraryAsync`. Metadata, full structured inventory JSON, indexed summary projections, map-type projections, scan state, stale-row deletion, and the normalization marker are committed together. Cancellation or any failure before commit leaves the previous metadata and content inventory intact.

`AssetFiltering` and `AssetSorting` compose physical folder, plain search, persisted inventory flags, and twelve deterministic sort modes in memory. Cards precompute at most five small badges and concise hover strings. `ContentInventoryWindow` is a separate bounded read-only view; WPF code-behind owns only window lifecycle while view models own grouping and commands.
## Catalog state and navigation

Filtering, search, and sorting compose in memory over the indexed `AssetSummary` read model. Search covers name, exact ID, canonical type, categories, typed tags, biome, region, mesh/texture filenames, map types, completeness, issues, variants, and LOD labels. `AssetSorting` owns twelve stable logic modes and always uses ID then folder path as tie-breakers. The chosen enum value is persisted with the per-user settings; display labels are presentation data only.

`AssetFiltering.BuildFolderTree` derives physical navigation and descendant-inclusive counts from indexed paths, never from a synchronous UI-thread filesystem walk. A selected card is restored after filter/sort rebuilds using `(ID, JSON path)` identity and clears when that identity is no longer visible.

## WPF state and images

The application loads the existing SQLite index at startup and never starts a scan implicitly. Commands expose settings, rescan, cancellation, single selection, preview, Explorer, and clipboard actions. `VirtualizingWrapPanel` realizes only visible card containers. `BoundedImageLoader` reads bytes asynchronously, decodes with `BitmapCacheOption.OnLoad`, freezes images for thread-safe presentation, and keeps an explicit 128 MiB LRU bound. Card requests are invalidated on recycling; source files are not held open.

Hover popup behavior is owned by each asset card with a 425 ms delay and cancellation of stale requests. Optional normalized rows are collapsed. The popup does not take focus and is width/height bounded. The preview is an in-window overlay; closing it restores focus to the asset list. Arrow keys use native `ListBox` navigation, `Enter` opens the selected preview, `Esc` closes it, and list-scoped `Ctrl+C` copies the selected folder path without overriding TextBox copy.

## Reliability invariants

- no source Megascans file is modified or copied;
- reparse-point directories are not traversed;
- one malformed/unrelated JSON file cannot abort the scan;
- texture-component types never classify an asset;
- cancellation is checked between traversal and per-asset work;
- stale index deletion and normalization-version promotion occur only in a successful full-scan transaction;
- settings changes require explicit save and explicit rescan;
- Windows paths use ordinal-ignore-case identity and deterministic ordering;
- shell launch uses structured `ProcessStartInfo` arguments rather than command concatenation;
- errors are logged structurally and the UI shows concise messages.

## Diagnostics and compatibility flow

At startup, `SqliteAssetIndex` opens an existing database read-only and derives an explicit compatibility value before any writable connection is allowed. A supported v1 structural migration is transactional. An older normalization marker leaves rows readable and requests an explicit Rescan. Missing indexes remain absent until Rescan, while newer unsupported or corrupted files block the write command and are preserved.

`Core` owns the compatibility, persisted-scan, and diagnostics records plus deterministic text formatting. `Infrastructure` owns SQLite validation, the write gate, migrations, and atomic scan metadata. `App` combines those facts with generated build identity and current view-model state. `DiagnosticsWindow` binds immutable label/value rows, and clipboard failure is handled by `DiagnosticsViewModel`; code-behind only opens or closes windows.

Successful replacement commits catalog rows, the current normalization marker, and last-scan counters in one transaction. Diagnostics can therefore distinguish live failed/cancelled attempts from the last successful persisted scan without coupling history storage to WPF.
## Dependency injection

`App.xaml.cs` creates the service provider, registers Infrastructure implementations, image loading, desktop asset interactions, view models, and windows, and disposes the provider during application shutdown. Long-running operations are owned/cancelled by view models rather than a global mutable service locator.

## Build identity and startup diagnostics

`Directory.Build.props` is the repository-wide version authority. It generates consistent product, assembly, file, and informational versions for all projects. `global.json` selects the stable SDK patch policy used both locally and in CI.

At build time, CI supplies its run suffix and short commit SHA as MSBuild properties. It never writes a version source file. `ScanVault.App` reads only generated assembly attributes through immutable `ApplicationBuildInfo`; it never starts Git or performs filesystem work to discover build identity at runtime.

The App composition root registers that build information once. `MainViewModel` exposes a compact product-version title without the SHA, while the structured startup event records product and informational versions, commit, configuration, runtime, OS, and process architecture. Missing optional metadata has explicit fallbacks and cannot prevent startup.

The Windows GitHub Actions workflow validates restore, Release build, tests, formatting, and whitespace for pushes and pull requests. Release publishing, packaging, repository writes, and branch-setting mutations are outside this workflow. See `versioning-and-ci.md` for exact version semantics and branch-protection guidance.
