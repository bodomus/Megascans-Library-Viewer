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

## Catalog state and navigation

Filtering, search, and sorting compose in memory over the indexed `AssetSummary` read model. Search covers name, exact ID, canonical type, categories, typed tags, biome, and region. `AssetSorting` owns the eight stable logic modes and always uses ID then folder path as tie-breakers. The chosen enum value is persisted with the per-user settings; display labels are presentation data only.

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

## Dependency injection

`App.xaml.cs` creates the service provider, registers Infrastructure implementations, image loading, desktop asset interactions, view models, and windows, and disposes the provider during application shutdown. Long-running operations are owned/cancelled by view models rather than a global mutable service locator.
