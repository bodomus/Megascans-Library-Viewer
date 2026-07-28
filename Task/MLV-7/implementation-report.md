# MLV-7 — Implementation report

## Outcome

Implemented deterministic, read-only asset content inventory across Core, Infrastructure, SQLite, and WPF. The full Rescan now inventories mesh variants/LODs and texture sets, records ambiguity and missing content, computes the confirmed per-kind completeness status, persists the result transactionally, and exposes it through catalog filters, sorts, search, cards, hover summaries, and a detail window.

## Main changes

- Added immutable Core inventory models, filename/map/set/resolution analysis, duplicate preservation, issue reporting, and confirmed completeness policy.
- Added cancellable bounded filesystem inventory after duplicate-ID winner selection; source files are enumerated by name and never modified or fully decoded.
- Extended metadata parsing with content-like JSON references and missing-reference diagnostics.
- Migrated SQLite to schema/normalization version 3 with lossless `inventory_json`, indexed scalar projections, normalized map projections, transactional replacement, and safe legacy rescan behavior.
- Added scan inventory phase/counters/logging and retained cancellation/rollback guarantees.
- Added persisted composable filters, four content sorts, inventory-aware search, card badges/warnings, concise hover summaries, and a read-only detail window with guarded copy/open-folder actions.
- Updated README, architecture, index-format, and dedicated content-inventory documentation.

## Validation

Executed from repository root:

- `dotnet restore ScanVault.sln` — passed.
- `dotnet format ScanVault.sln --verify-no-changes --no-restore` — passed.
- `dotnet build ScanVault.sln --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false` — passed, 0 warnings, 0 errors.
- `dotnet test ScanVault.sln --configuration Release --no-build -m:1` — passed, 93/93 tests (Core 45, Infrastructure 30, App 18).
- `git diff --check` — passed; only Git line-ending conversion notices were emitted.
- WPF layout smoke test realizes the main and content-inventory windows successfully.

Interactive manual WPF behavior was not executed and is not claimed.

## Read-only benchmark

Reproducible harness: `Task/MLV-7/Benchmark/Benchmark.csproj`.
Input: `tests/Megascan` (read-only). Temporary database was outside the asset tree and deleted after the run.

- source files: 378
- indexed assets: 10
- meshes: 116
- textures: 239
- ambiguous assets: 4
- missing-critical assets: 5
- scan elapsed: 215.7 ms
- wall clock: 219.0 ms
- temporary database size: 446,464 bytes

The figures describe this local fixture and environment; they are not a production performance guarantee.

## Graph review

Preflight used Graphify for subsystem orientation and code-review-graph for concrete dependencies. Post-change Graphify was refreshed to 1,046 nodes / 2,190 edges / 38 communities and its affected-path query linked analyzer, scanner, persistence, UI, and tests. After staging new sources, CRG was refreshed to 564 nodes / 1,233 edges / 82 C# files. Change detection classified the cross-layer change as high review risk (0.75), prioritizing SQLite compatibility/replacement, parsing, badges, and window wiring; these paths are covered by the successful migration, inventory, policy, view-model, and WPF smoke tests.

## Risks and limitations

- Classification is deliberately conservative; unresolved real duplicates/conflicts produce `Ambiguous` and retain every candidate.
- `Partial` is reserved for future recognized profiles; current confirmed profiles normally resolve to Complete, Usable, MissingCriticalFiles, Ambiguous, or Unknown.
- Existing schema-v2 indexes migrate safely but require a Rescan to populate inventory.
- No editor, renderer, repair, import, or source-file mutation was added.

## Artifacts and copies

- Ticket specification copy: `Tickets/MLV-7.md`
- Investigation: `Task/MLV-7/investigation.md`
- Plan: `Task/MLV-7/implementation-plan.md`
- Implementation report: `Task/MLV-7/implementation-report.md`
- Final review copy: `review/review-MLV-7.md`
- Content documentation: `Docs/content-inventory.md`
## CI encoding follow-up — 2026-07-28

GitHub Actions exposed four `U+FFFD` replacement characters introduced by a Windows code-page conversion during MLV-7 editing. The failing diagnostics-title assertion expected the corrupted character while the WPF window correctly produced an em dash.

Corrected all four tracked occurrences with ASCII-safe C# Unicode escapes: the diagnostics title test (`\u2014`), unclassified-file separator (`\u2014`), unknown resolution placeholder (`\u2014`), and inventory progress ellipsis (`\u2026`). Extended `ContentInventoryViewModelTests` to cover the em-dash rendering. A repository-wide scan of tracked text files found no remaining `U+FFFD` characters.

Validation after the fix: App tests 18/18; full solution tests 93/93; Release build 0 warnings / 0 errors; restore, formatter verification, and `git diff --check` passed. Graph review confirmed a direct App/test-only correction with no architecture or persistence impact.
