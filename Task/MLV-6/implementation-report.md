# MLV-6 — Implementation report

## Outcome

Implemented the About / Diagnostics experience and an explicit read-before-write SQLite compatibility state machine. ScanVault now distinguishes compatible, migratable, normalization-outdated, newer unsupported, missing, and corrupted indexes without parsing UI text. Newer/corrupted files block unsafe writes and are preserved.

## Delivered changes

- Core:
  - added immutable compatibility, persisted-scan, index-diagnostics, and diagnostics-snapshot models;
  - added deterministic `DiagnosticsFormatter` with 23 fixed-order fields, invariant formatting, and one `Unavailable` token;
  - extended `IAssetIndex` with compatibility inspection and diagnostics access.
- Infrastructure:
  - added read-only, non-pooled inspection before writable SQLite setup;
  - validates integrity, required tables, schema marker, schema version, and normalization version;
  - retained transactional v1→v2 migration and explicit `RequiresRescan` transition;
  - revalidates at the write boundary and blocks newer/corrupted databases;
  - commits successful scan duration/status/counters atomically with catalog replacement;
  - reads legacy schema-v2 `ScanResult` JSON as a compatibility fallback;
  - added structured compatibility, migration, blocked-write, and corruption logging.
- App:
  - added an About / Diagnostics toolbar entry, dialog, service, and view model;
  - exposes build/runtime/OS/library/index/scan/path/UI context without broad environment collection;
  - copies stable report text and handles clipboard failure without closing the dialog;
  - shows concise startup state and disables Rescan when writes are unsafe;
  - records live success/cancellation/failure attempt data for the open process.
- Documentation:
  - updated README, architecture, and SQLite format documentation;
  - added `Docs/diagnostics.md` with state table and manual recovery guidance.

## State transitions and safety

- Missing startup performs no database write and leaves the file absent.
- The first explicit Rescan may create an empty current schema before transactional catalog replacement; cancellation thereafter can leave a valid empty database, never a partial replacement.
- Schema v1 is the only automatic structural migration and remains transactional.
- Older normalization stays readable and requests Rescan.
- Newer schema/normalization and corrupted input are never opened writable; byte-preservation tests verify failed replacement does not alter the file.
- No Megascans source asset is read by tests, copied, modified, moved, or deleted.

## Post-change graph review

CRG was refreshed from the staged MLV-6 change:

- 29 files analyzed;
- 484 nodes / 1,026 edges / 72 C# files;
- 85 changed symbols; risk score 0.60;
- static test-gap output names composition/log/window symbols, but direct source validation and executable tests cover compatibility, formatter, view-model commands/status, clipboard behavior, and WPF binding/layout.

Graphify was refreshed because project boundaries and dependencies changed:

- 893 nodes / 1,862 edges / 41 communities;
- affected traversal confirms `IndexCompatibilityInfo` flows through `IAssetIndex`, `SqliteAssetIndex`, `MainViewModel`, and the new compatibility tests;
- `DiagnosticsSnapshot` flows through the capture service, formatter, view model, WPF smoke test, and formatter/clipboard tests.

The pre-existing untracked `.graphifyignore` was preserved exactly and excluded from the ticket. Because it ignores Markdown, Graphify refreshed code/project structure but not the new documentation. Graph results were validated against source and tests.

## Validation

Executed from repository root:

```powershell
dotnet restore ScanVault.sln --disable-parallel --disable-build-servers --force-evaluate --maxcpucount:1 --nodeReuse:false
dotnet build ScanVault.sln --configuration Release --no-restore --disable-build-servers --maxcpucount:1 --nodeReuse:false -p:UseSharedCompilation=false
dotnet test ScanVault.sln --configuration Release --no-build --no-restore --disable-build-servers --maxcpucount:1 --nodeReuse:false -p:UseSharedCompilation=false
dotnet format ScanVault.sln --verify-no-changes --no-restore
git diff --cached --check
```

Results:

- restore: passed after allowing NuGet access for package audit;
- Release build: passed, 0 warnings, 0 errors;
- tests: 74 passed, 0 failed (Core 31, Infrastructure 28, App 15);
- formatting: passed;
- staged diff check: passed.

A first default-parallel App test attempt left idle MSBuild workers in the restricted environment. Only workers created by those attempts were terminated; one-node validation then passed. A sandboxed restore attempt failed only because NuGet vulnerability data was unreachable and passed when rerun with network permission. A sandboxed `dotnet format` attempt failed only because Roslyn could not access its named pipe and passed with local build-host permission.

## Manual validation and residual risks

Interactive desktop validation was not claimed. WPF creation, resource lookup, bindings, 23 diagnostic rows, and title were exercised by the STA layout smoke test. Remaining manual checks are visual sizing on a real desktop and an actual OS clipboard operation; both paths use existing desktop abstractions and clipboard failure is covered.

No file-log provider exists, so no log-directory field is invented. Technical exceptions remain structured logs; the visible report contains no stack traces.

## Copies and preserved data

- Copies of Megascans source files: none.
- Paths of source copies: not applicable; no source assets were copied.
- Source Megascans files changed/moved/deleted: none.
- Test databases/directories: created only under unique system temporary directories and disposed by tests.
- Preserved unrelated file: `J:\Projects\UE_Projects\Megascans Library Viewer\.graphifyignore` (pre-existing, untracked, unchanged, not staged).
- Local ticket specification: `J:\Projects\UE_Projects\Megascans Library Viewer\Tickets\MLV-6.md`; the exact file is attached to YouTrack during completion.