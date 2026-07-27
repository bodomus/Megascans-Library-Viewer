# ScanVault

ScanVault is a Windows desktop viewer and local indexer for legacy Quixel Megascans libraries. It reads JSON metadata and local preview images without changing or copying source assets.

## Current features

- explicit per-user library-root setting;
- cancellable recursive scan with deterministic traversal;
- schema-aware legacy Megascans parsing and canonical metadata normalization;
- transactional, versioned SQLite index with automatic v1 migration and corrective-rescan marker;
- deterministic duplicate-ID resolution and reporting;
- physical folder tree with descendant-inclusive counts and filtering;
- case-insensitive catalog search and eight persistent deterministic sort modes;
- virtualized thumbnail grid with asynchronous bounded image cache;
- persistent single-card selection, delayed normalized hover details, and context actions;
- keyboard navigation with `Enter` preview, `Esc` close, and list-scoped `Ctrl+C` folder copy;
- existing-index load at startup with no automatic rescan.

## Prerequisites

- Windows 10/11;
- .NET SDK 10.0.302 or a compatible stable patch selected by `global.json`;
- Windows Desktop Runtime 10.

## Build and test

```powershell
dotnet restore ScanVault.sln
dotnet build ScanVault.sln --configuration Release --no-restore
dotnet test ScanVault.sln --configuration Release --no-build
```

Run from source:

```powershell
dotnet run --project src/ScanVault.App/ScanVault.App.csproj
```

## User data

- settings: `%AppData%\ScanVault\settings.json`;
- SQLite index: `%LocalAppData%\ScanVault\scanvault.db`;
- thumbnail cache location reserved at `%LocalAppData%\ScanVault\thumbnails`.

These paths are outside both the repository and the scanned library. Existing version 1 databases migrate automatically; a populated older index remains usable and displays a concise request to run Rescan so source metadata can be normalized.

## Architecture

`ScanVault.Core` owns models, contracts, and deterministic normalization/catalog policies. `ScanVault.Infrastructure` owns filesystem discovery, schema-aware JSON parsing, settings, scan orchestration, and SQLite. `ScanVault.App` owns WPF composition, views, view models, commands, virtualization, image presentation, clipboard, and Explorer interaction. See `Docs/architecture.md`, `Docs/index-format.md`, and `Docs/versioning-and-ci.md`.

## Version and CI

The product version is defined once in `Directory.Build.props`. Local builds use a `dev` suffix; GitHub Actions injects a CI run suffix and short commit SHA without changing tracked files. CI validates every push and pull request on Windows and does not publish releases. The complete policy and recommended `master` protection are in `Docs/versioning-and-ci.md`.

## Repository workflow

Non-trivial tickets follow `.codex/PRE_TICKET_WORKFLOW.md`: Graphify for architecture, CRG for structural impact, direct source validation, post-change CRG update, executable build/tests, and ticket artifacts under `Task/` and `review/`.

## Known limitations

- Windows-only WPF application;
- no Unreal/Fab integration, import, editing, moving, or deleting assets;
- no virtual grouping trees, texture-map tabs, zoom/pan, installer, or updater;
- metadata support is intentionally tolerant and may omit unknown legacy fields;
- advanced query syntax and multi-selection are not supported.
