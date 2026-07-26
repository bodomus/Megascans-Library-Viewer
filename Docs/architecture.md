# ScanVault architecture

## Boundaries

```text
ScanVault.App -> ScanVault.Infrastructure -> ScanVault.Core
      |-----------------------------------> ScanVault.Core
```

`Core` has no WPF or SQLite dependency. `Infrastructure` implements Core contracts. `App` is the WPF composition root and contains presentation-only behavior.

## Scan flow

1. The user saves a valid library root and explicitly starts Rescan.
2. `MainViewModel` prevents concurrent scans and exposes cancellation/progress.
3. `LibraryScanService` asks `FileSystemScanner` to discover JSON files on a worker thread.
4. `MegascansMetadataParser` parses each file independently and resolves local preview candidates without decoding images.
5. `DuplicateAssetResolver` groups IDs case-insensitively; the lexicographically smallest normalized full JSON path wins and all copies are recorded.
6. `SqliteAssetIndex.ReplaceLibraryAsync` upserts, replaces tag relations, removes stale rows, and stores scan state in one transaction.
7. Only a successful commit replaces visible view-model data. Cancellation or failure preserves the previous index.

## WPF state and images

The application loads the existing SQLite index at startup and never starts a scan implicitly. Commands expose settings, rescan, cancellation, selection, and preview state. `VirtualizingWrapPanel` realizes only visible card containers. `BoundedImageLoader` reads bytes asynchronously, decodes with `BitmapCacheOption.OnLoad`, freezes images for thread-safe presentation, and keeps an explicit 128 MiB LRU bound. Card requests are invalidated on recycling; source files are not held open.

Hover popup behavior is owned by each asset card with a 425 ms delay and cancellation of stale requests. The preview is an in-window overlay, not another top-level system window.

## Reliability invariants

- no source Megascans file is modified or copied;
- reparse-point directories are not traversed;
- one malformed/unrelated JSON file cannot abort the scan;
- cancellation is checked between traversal and per-asset work;
- stale index deletion occurs only within a successful full-scan transaction;
- settings changes require explicit save and explicit rescan;
- Windows paths use ordinal-ignore-case identity and deterministic ordering;
- errors are logged structurally and the UI shows concise messages.

## Dependency injection

`App.xaml.cs` creates the service provider, registers Infrastructure implementations, image loading, view models, and windows, and disposes the provider during application shutdown. Long-running operations are owned/cancelled by view models rather than a global mutable service locator.