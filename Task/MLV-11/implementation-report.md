# MLV-11 implementation report

## Summary

Implemented read-only report export from the current ScanVault index with CSV, JSON and Markdown formats, six built-in profiles, entire/current/selected scopes, MLV-8 smart-collection results, MLV-9 completed scan changes, WPF progress/cancellation, structured logging, safe staging/finalization, documentation and deterministic tests.

## Baseline and workflow

- Workflow level: 2.
- Repository root: `J:/Projects/UE_Projects/Megascans Library Viewer`.
- Branch: `codex/mlv-11` from `master`.
- Initial commit: `a4c2e26045a1c42d8a8614907526bb98553ec9c9`.
- Initial working tree: unrelated untracked `Task/MLV-8/implementation-report.md`; preserved unchanged.
- Ticket: MLV-11, moved from Open to In Progress before implementation.
- Local ticket specification: `Tickets/MLV-11.md`; the YouTrack issue already contained `MLV-11-Export-reports.md` as an attachment.

## Preflight evidence

### Graphify

- Confirmed syntax with `graphify --help`.
- Initial focused query located `MainViewModel`, `AssetFiltering`, `SmartCollectionPolicy`, `ScanHistoryViewModel`, `SqliteAssetIndex`, inventory/completeness models, logging, DI and tests.
- Source validation confirmed the current-view pipeline is folder -> inventory filter -> search -> deterministic sort over `allAssets`.
- MLV-8 and MLV-9 were present on `master` and used directly.

### code-review-graph

- Initial status was stale on `codex/MLV-8-smart-collections`: 617 nodes, 1,432 edges, 82 files.
- Preflight full rebuild: 94 files, 778 nodes, 1,715 edges.
- Direct `rg`/source inspection verified constructors, DI, WPF click entry point, filter/sort flow, scan-history reads, cancellation and tests.

## Architecture and changed behavior

### Core

- Added report enums, schema version 1, explicit metadata/row DTOs, request/progress/result records and `IReportExportService`/`IReportWriter` contracts.
- Added `ReportProfilePolicy` for stable profile mapping, relative-path defaults, optional absolute paths, filename generation, estimates and validation.
- Explicit row DTOs are `AssetCatalogRowDto`, `AssetInventoryRowDto`, `IssueReportRowDto`, and `ScanChangeRowDto`.
- Stable issue enum names are exported as `IssueCode`; UI-localized text is not used as the identifier.
- Missing indexed facts such as per-file size/last-write time and asset indexed timestamp remain nullable.

### Infrastructure

- Added `CsvReportWriter`, `JsonReportWriter`, and `MarkdownReportWriter`.
- `ReportExportService` selects writers, creates a unique sibling staging file, streams rows, flushes, writes the CSV companion metadata, and publishes only completed output.
- `ReportFilePublisher` backs up/restores an existing CSV metadata companion if final destination publication fails.
- Cancellation/failure cleanup removes staging files. Existing final destination content is untouched until the final move.
- DI registers the three writers and export service.

### App/WPF

- Added main-toolbar **Export Report** entry point, `ExportReportWindow`, and `ExportReportViewModel`.
- Profiles: Asset Catalog, Asset Inventory, Issues Report, Completeness Report, Scan Changes, Smart Collection Result.
- Scopes: Entire Library, Current View, Selected Asset. Selected Asset deliberately means the current single card; no hidden multi-selection model was introduced.
- Current View snapshots the already filtered and sorted card order, preserving UI query semantics.
- Scan Changes loads all categories for a selected completed run and rejects non-completed runs.
- Smart Collection Result validates and evaluates the selected versioned collection definition.
- Format/profile-specific options, destination picker, estimates, overwrite confirmation, progress phases, row/asset counters, elapsed time, cancel and final status are exposed.
- Serialization/export work is started through `Task.Run`; UI-bound progress is posted in 128-row batches.
- Structured started/completed/cancelled/failed events omit the full destination path.

## Format policies

### CSV

- UTF-8 BOM.
- Fixed comma delimiter.
- Stable English machine headers.
- Invariant numbers and ISO 8601 UTC dates.
- Quotes delimiter, semicolon, quotes and CR/LF correctly.
- Streaming `StreamWriter` output.
- Companion `<destination>.metadata.json` when metadata is enabled.

### JSON

- UTF-8 explicit envelope with `reportSchemaVersion`, `metadata`, and `rows`.
- Camel-case DTO properties and optional pretty print.
- Runtime DTO rows are written incrementally with `Utf8JsonWriter`.

### Markdown

- Title, optional metadata, summary, and stable table headers, including empty reports.
- Escapes backslashes, pipes, backticks and line breaks.
- Streaming writer; CSV/JSON remain recommended for very large inventory interchange.

## Report metadata

The contract includes report/schema/format, generated UTC time, app/commit identity, database and normalization versions, optional fingerprint version, source scope, filter/sort summaries, asset and row counts, and optional smart-collection name/description/definition. Library identity/root and absolute file paths are emitted only when the user opts in.

## Performance validation

Command:

`dotnet test tests/ScanVault.Infrastructure.Tests/ScanVault.Infrastructure.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~ReportExportPerformanceTests" --logger "console;verbosity=detailed"`

Synthetic deterministic dataset results:

| Scenario | Rows | Output size | Duration |
| --- | ---: | ---: | ---: |
| Asset Catalog CSV, 10,000 assets | 10,000 | 1,504,159 bytes | 113.8669 ms |
| Asset Inventory CSV, 100,000 rows | 100,000 | 14,489,136 bytes | 473.3781 ms |
| Asset Catalog JSON, 10,000 assets | 10,000 | 4,404,285 bytes | 188.3963 ms |
| Issues Markdown, 5,000 issues | 5,000 | 964,563 bytes | 26.1087 ms |

- Peak sampled managed memory: 66,807,600 bytes.
- Cancellation latency after 256 written rows: 1.9887 ms.
- Cancellation test confirmed no final destination file was published.
- The measurements use synthetic indexed domain objects, not the user's Megascans library.

## Tests and validation

Targeted report tests cover:

- report schema/default filename/relative path policy;
- smart-collection metadata and non-completed scan rejection;
- CSV BOM, quoting, Unicode, companion metadata and empty headers;
- JSON external parsing through `System.Text.Json` and pretty output;
- Markdown escaping and structure;
- cancellation cleanup and existing-destination preservation;
- metadata companion rollback when destination finalization fails;
- export view-model conditional state/current-view snapshot/single-selection behavior;
- WPF main-window Export Report button realization;
- large synthetic datasets and cancellation latency.

Final commands and results:

- `dotnet restore ScanVault.sln` — passed; all projects up to date.
- `dotnet build ScanVault.sln --configuration Release --no-restore --verbosity minimal` — passed; 0 warnings, 0 errors.
- `dotnet test ScanVault.sln --configuration Release --no-build --verbosity minimal` — passed; Core 50, Infrastructure 41, App 25; total 116/116.
- `dotnet format ScanVault.sln --no-restore --verify-no-changes --verbosity minimal` — passed.
- `git diff --check` — passed.
- JSON parser validation — passed in automated integration test.
- CSV BOM/contract validation — passed in automated integration test.
- Markdown structure/escaping validation — passed in automated integration test.
- Cancellation/overwrite/large dataset validation — passed in automated integration tests.

## Post-change graph evidence

- `code-review-graph update --base master --repo . --brief` classified 7 tracked changed files, 11 changed symbols, risk 0.47 and pointed at `MainWindow`, `MainViewModel`, `CreateExportReportViewModelAsync` and DI as direct impact.
- Full CRG rebuild completed on `codex/mlv-11`, but the installed CLI indexes only tracked files and remained at 94 files; new untracked export files are therefore absent until staging/commit. This limitation is explicitly reported rather than treating its test-gap warnings as authoritative.
- `graphify update .` initially failed with Windows access denied in the sandbox, then succeeded with the same confirmed command outside it: 1,566 nodes, 3,523 edges, 72 communities, 126 source files processed.
- Post-refresh Graphify query found `ReportExportService`, all three writers, `ExportReportViewModel`, `ReportProfilePolicy`, requests/DTOs, DI and the report/performance tests.
- Direct source, build and tests are authoritative and confirm the dependency direction Core <- Infrastructure/App.

## Changed files

- Core: report models, contracts and profile policy.
- Infrastructure: reporting directory and DI registration.
- App: export window/view model/logging, main toolbar/code-behind and MainViewModel factory.
- Tests: Core policy, Infrastructure writer/safety/performance, App view-model and WPF entry point.
- Docs: README, architecture, `Docs/export-reports.md`, ticket/investigation/plan/report artifacts.

## Duplicate groups

No scan was run and no source library was traversed; duplicate-ID groups are not applicable to this read-only export ticket.

## Known limitations and unverified manual items

- The current index does not persist trustworthy size/last-write values for every legacy inventory entry; those DTO fields remain null.
- Selected Asset is single-selection only.
- Diagnostics does not persist export state or destination paths; the dialog shows live/last state for its lifetime.
- Excel desktop opening was not automated; BOM, quoting, invariant formatting and Unicode are verified byte-for-byte, but a human Excel smoke test remains unverified.
- A visual Markdown renderer smoke test remains unverified; generated structure/escaping is parser-oriented tested.
- Manual application interaction beyond the deterministic WPF layout test was not claimed.
- GitHub Actions was not run because no push or PR was requested; local equivalent restore/build/test/format/whitespace checks passed.

