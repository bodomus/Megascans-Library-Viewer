# MLV-1 Investigation

## Ticket

**MLV-1 — Bootstrap ScanVault and implement the first usable Megascans library viewer**

The ticket is classified as a Level 2 structural task. All ticket artifact paths
use `MLV-1`; the obsolete `SCAN-1` identifier in the original text is not used.

## Repository baseline

- Repository root: `J:\Projects\UE_Projects\Megascans Library Viewer`
- Git state at the start: the directory was not a Git repository.
- Git was initialized at the user's request on 2026-07-26.
- Initial branch: `master`.
- Initial commit: none.
- Initial tracked source: none; `src/` and `reviews/` were empty.
- Existing maintained inputs: `AGENTS.md`, `.codex/`, and template skills under
  `skills/`.
- The existing workflow and agent files describe an Unreal Engine C++ plugin and
  therefore conflict with MLV-1.

## Toolchain

- Selected SDK: .NET SDK `10.0.301`, the newest stable SDK installed locally.
- Target framework: `net10.0-windows`.
- Language version: `latestMajor` (C# 14 for the selected SDK), with preview
  features disabled.
- Windows Desktop runtime `10.0.9` is installed.
- No `global.json` existed at preflight time.

## Graphify preflight

Confirmed commands:

```powershell
graphify --help
graphify query "What architecture and application platform does the repository currently define?" --budget 1200
graphify query "What preflight and validation workflow governs a Level 2 structural task?" --budget 1200
graphify explain "Unreal Engine Editor Plugin Scope"
graphify export html
```

Graphify `0.9.8` was available through the installed `graphifyy` uv tool.
No graph existed initially. The initial corpus contained 10 documents and no
source files, approximately 11,185 words. A new graph was created with 46 nodes,
49 undirected edges, and 8 communities.

Important findings:

- The repository workflow is centered on Graphify + CRG preflight and source
  validation.
- `AGENTS.md` explicitly identifies the repository as an Unreal editor plugin.
- The supplied C# skill material is largely ASP.NET Core, Blazor, and EF Core
  oriented and is not a WPF architecture specification.
- No ScanVault application architecture or runtime entry point exists yet.

Integrity limitation:

- The diagnostic reported 5 `undirected_same_endpoint_collapsed_edges` while
  converting 54 extracted edges to 49 graph edges.
- There were no missing endpoints, dangling endpoints, self-loops, or exact
  duplicate edges.
- Graph results are used only as navigation evidence.

Source validation:

- Direct inspection of `AGENTS.md` confirms the Unreal-specific scope.
- Direct inspection of `.codex/PRE_TICKET_WORKFLOW.md` confirms Unreal lifecycle,
  Slate, mapper, host-project, and editor-test assumptions.
- Direct `rg`/filesystem inspection confirms there are no source projects,
  tests, solution files, or build scripts to validate.

## CRG preflight

Confirmed commands:

```powershell
code-review-graph --help
code-review-graph build --help
code-review-graph status --repo .
code-review-graph build --repo .
```

The installed CRG initialized schema version 9 and completed a full build.
Result: 0 files, 0 nodes, 0 edges, 0 communities, and 0 flows. This is expected
for the empty bootstrap repository. It cannot provide caller/callee or blast
radius evidence until implementation exists.

## Current behavior

There is no application, scanner, index, settings store, UI, or test suite. The
repository contains only generic and Unreal-oriented workflow scaffolding.

## Expected behavior

Create a Windows-only WPF application that starts from an existing per-user
SQLite index, allows explicit settings and rescans, recursively indexes tolerant
Megascans metadata, and provides a responsive folder tree, virtualized thumbnail
grid, hover details, and in-application preview.

## Architectural gap

The complete application boundary is missing. The smallest coherent solution
needs three production projects and three test projects:

- `ScanVault.Core`: domain contracts and deterministic policies;
- `ScanVault.Infrastructure`: filesystem, JSON, SQLite, settings, and image path
  implementations;
- `ScanVault.App`: WPF composition root, view models, commands, views, and
  UI-thread/image integration;
- Core, Infrastructure, and App/ViewModel xUnit tests using generated temporary fixtures.

## Expected change surface

This bootstrap intentionally affects the entire new solution. Dependency
direction will remain:

```text
ScanVault.App -> ScanVault.Core
ScanVault.App -> ScanVault.Infrastructure
ScanVault.Infrastructure -> ScanVault.Core
ScanVault.Core -> no WPF or SQLite dependency
```

## Principal risks

- preserving the previous valid SQLite index on cancellation or failure;
- deterministic duplicate-ID handling;
- avoiding reparse loops and inaccessible-directory failures;
- tolerant parsing of variable legacy JSON;
- UI-thread affinity and stale async image requests;
- file-handle lifetime and bounded image memory;
- WPF virtualization behavior with thousands of assets;
- package restore availability in the restricted environment;
- inability to claim manual UI behavior without launching and exercising WPF.

## Missing tests

All tests are currently missing. The implementation must add deterministic core,
infrastructure, persistence, scanner, parser, resolver, and view-model tests.

## Confirmed decisions

1. For duplicate Megascans IDs, the lexicographically smallest normalized full
   JSON path wins using Windows ordinal-ignore-case comparison. Every duplicate
   group is reported with the winner path and the full paths of all skipped copies.
2. Assets absent after a successful full scan are deleted from the SQLite index
   in the same transaction. Cancellation or failure preserves the previous index.
3. Required skills move to `.agents/skills/`; the obsolete `skills/` tree is
   removed to avoid duplicate Unreal/ASP.NET/Blazor guidance.
4. The Git branch remains `master` by project convention.

