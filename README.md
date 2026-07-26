# ScanVault

ScanVault is a Windows desktop viewer and local indexer for legacy Quixel Megascans libraries. It reads JSON metadata and local preview images without changing or copying source assets.

## Current features

- explicit per-user library-root setting;
- cancellable recursive scan with deterministic traversal;
- tolerant legacy Megascans JSON parsing;
- transactional, versioned SQLite index;
- deterministic duplicate-ID resolution and reporting;
- physical folder tree and descendant filtering;
- virtualized thumbnail grid with asynchronous bounded image cache;
- delayed hover metadata popup;
- in-application large preview closed by button, backdrop, or `Esc`;
- existing-index load at startup with no automatic rescan.

## Prerequisites

- Windows 10/11;
- .NET SDK 10.0.301 or compatible stable 10.0 feature band;
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

These paths are outside both the repository and the scanned library.

## Architecture

`ScanVault.Core` owns models, contracts, and deterministic policies. `ScanVault.Infrastructure` owns filesystem discovery, JSON parsing, settings, scan orchestration, and SQLite. `ScanVault.App` owns WPF composition, views, view models, commands, virtualization, and image presentation. See `Docs/architecture.md` and `Docs/index-format.md`.

## Repository workflow

Non-trivial tickets follow `.codex/PRE_TICKET_WORKFLOW.md`: Graphify for architecture, CRG for structural impact, direct source validation, post-change CRG update, executable build/tests, and ticket artifacts under `Task/` and `review/`.

## Known limitations

- Windows-only WPF application;
- no Unreal/Fab integration, import, editing, moving, or deleting assets;
- no virtual grouping trees, texture-map tabs, zoom/pan, installer, or updater;
- metadata support is intentionally tolerant and may omit unknown legacy fields;
- full UI automation is not included in MLV-1.