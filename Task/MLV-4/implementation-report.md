# MLV-4 — Implementation report

## Identity

- YouTrack: `MLV-4`
- Source ticket label: `SCAN-2`
- Confirmed mapping: `SCAN-2` = `MLV-4`
- Project: Megascans Library Viewer (`MLV`)
- Assignee: ChatGPT

## Implemented result

### Canonical metadata

- Added one `MetadataNormalizer` policy for optional values, asset types, category/value lists, resolution, physical size, and texel density.
- Replaced unrestricted recursive parser lookups with known-schema reads and documented type precedence.
- Texture components (`normal`, `specular`, `roughness`, `albedo`, `opacity`, `displacement`, `bump`, `gloss`, `translucency`) cannot classify an asset.
- Added structured `ImageResolution` with square/non-square display formatting and deterministic maximum selection across component/map resolutions.
- Preserved optional raw asset type and typed tag groups.
- Placeholder values are normalized to `null`; the deliberate `Unknown` asset-type fallback remains distinct.

### Existing-index upgrade

- Advanced SQLite structural schema from v1 to v2.
- Added independent normalization marker v2 and `IAssetIndex.RequiresNormalizationRescan`.
- v1 migration adds `raw_asset_type`, `resolution_width`, and `resolution_height` while retaining readable legacy scalar resolution.
- A populated legacy index stays readable and is marked for explicit corrective Rescan.
- The normalization marker is promoted only inside the successful full-replacement transaction.
- Cancellation/failure preserves old rows and the old marker; manual database deletion is not required.

### Catalog UI and navigation

- Redesigned cards to show name, canonical type/categories, and compact resolution plus explicit `ID:`.
- Added persistent selected/focused card styling and identity preservation by `(ID, JSON path)` through sort/filter rebuilds.
- Hover popup keeps the 425 ms cancellable delay, omits unavailable optional rows, wraps paths, and has bounded dimensions.
- Added all eight enum-backed sort modes; preference persists independently of unsaved library-root edits.
- Search now covers name, ID, type, categories, typed tags, biome, and region.
- Folder tree shows descendant-inclusive counts derived from indexed paths.
- Added context actions: preview, Explorer, copy ID, copy folder, copy JSON path.
- Added native arrow navigation, `Enter` preview, `Esc` close, list-scoped `Ctrl+C` folder copy, and focus restoration after preview close.
- Explorer uses `ProcessStartInfo.ArgumentList`; no shell command concatenation is used.

### Documentation

- Updated `README.md`.
- Updated `Docs/architecture.md`.
- Updated `Docs/index-format.md`.

## Graph analysis

### Preflight

- Graphify focused query located parser, SQLite, settings, filtering, WPF card/popup, and related tests.
- CRG preflight indexed 294 nodes / 566 edges / 50 C# files.
- Important graph candidates were verified in source before edits.

### Post-change

- `code-review-graph update --base HEAD~1 --repo . --brief`
  - 25 files updated;
  - 325 nodes / 659 edges / 50 C# files after update;
  - reported risk score 0.60.
- `code-review-graph detect-changes --base HEAD --repo . --brief`
  - 24 tracked changed files at that point;
  - 82 changed symbols/classes;
  - UI entry points reported as untested by CRG, but source verification plus WPF STA layout and view-model tests cover the relevant reachable behavior.
- A mistakenly attempted unsupported `code-review-graph review` command failed at argument parsing and made no changes. Installed syntax was then checked with `--help`, and supported `detect-changes` was used.
- Architecture and important DI/subsystem relationships changed, so Graphify was refreshed:
  - `graphify update .` initially failed in the restricted sandbox with `WinError 5`;
  - the same confirmed command succeeded with approval;
  - refreshed graph: 1016 nodes / 1724 edges / 66 communities;
  - focused post-change query found the new normalizer, resolution model, schema migration, catalog policies, UI events, and associated tests.

## Validation

Working directory for every repository command:

`J:\Projects\UE_Projects\Megascans Library Viewer`

| Command | Result |
|---|---|
| `dotnet restore ScanVault.sln` | Passed; all 6 projects restored |
| `dotnet build ScanVault.sln --configuration Release --no-restore -m:1 /nodeReuse:false /p:UseSharedCompilation=false --verbosity:minimal` | Passed; 0 warnings, 0 errors |
| `dotnet test ScanVault.sln --configuration Release --no-build -m:1 /nodeReuse:false --verbosity:minimal` | Passed; 55 tests, 0 failed, 0 skipped |
| `dotnet format ScanVault.sln --verify-no-changes --no-restore --verbosity minimal` | Passed |
| `git diff --check` | Passed after removing one extra EOF blank line |

Permanent test totals:

- Core: 29 passed;
- Infrastructure: 20 passed;
- App/WPF: 6 passed;
- Total: 55 passed.

The tests cover requested normalization cases, resolution forms/display, physical size, type precedence and component rejection, placeholder cleanup, stable ID, tag/category deduplication, all sort modes and tie-breakers, search/filter/sort composition, selection preservation, folder totals, context copy commands, settings persistence, v1 migration, rescan-required state, atomic marker promotion, and cancellation preservation.

## Read-only real-fixture smoke validation

A transient, uncommitted harness scanned:

`J:\Projects\UE_Projects\Megascans Library Viewer\tests\Megascan`

The source fixture was opened read-only by the production scanner/parser. The SQLite database and cache were created under the test framework's unique OS temporary directory and deleted during disposal. The harness passed checks that:

- the resulting catalog was non-empty;
- indexed count matched scan result;
- component types did not appear as asset types;
- malformed giant resolution values were absent;
- literal `undefined` was absent from normalized optional fields.

The transient harness file was deleted after the run. `tests/Megascan` remained Git-clean.

## Copies and backups

- Source Megascans assets copied: none.
- Source Megascans assets modified: none.
- Persistent backup copies created: none.
- Temporary SQLite/cache: OS test temporary directory, automatically deleted; no surviving path.
- Ticket specification saved locally at `Tickets/MLV-4.md`.

## Not claimed / remaining manual checks

The application GUI was not launched against the user's real per-user settings/database, to avoid mutating that external state without a dedicated interactive session. Therefore these visual/interactive checks are not claimed as executed:

- subjective card readability and rapid-hover flicker on the user's display;
- clicking every sort option in the live GUI;
- actual Windows Clipboard and Explorer launch;
- restart persistence through a full interactive app restart;
- popup work-area placement across multi-monitor layouts.

Automated policy, parser, persistence, view-model, command, and WPF layout tests pass; the items above remain a short user acceptance pass.

## Risks

- Legacy Megascans schema variants outside the sanitized tests and current `tests/Megascan` sample may be intentionally omitted rather than guessed.
- WPF popup work-area behavior ultimately depends on Windows placement rules and should be visually checked on the target monitor configuration.
- The v1 compatibility columns remain by design; a future schema ticket can remove them only after the compatibility window is explicitly closed.
