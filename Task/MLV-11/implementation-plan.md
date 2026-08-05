# MLV-11 implementation plan

1. Add stable report enums, request/metadata/progress records and explicit row DTOs in Core. Keep Core independent of WPF, SQLite and concrete serialization libraries.
2. Add Core mapping/profile policy that reuses indexed `AssetSummary`, stable issue codes and existing smart-collection/scan-history records. Use nullable values where the index has no fact.
3. Add Infrastructure `IReportWriter` implementations for CSV, JSON and Markdown plus an orchestrating `IReportExportService` that stages sibling temporary files, streams rows asynchronously, propagates cancellation and atomically publishes completed output.
4. Use a CSV companion `.metadata.json`; emit UTF-8 BOM, comma delimiter, invariant values and RFC-4180-style quoting. JSON gets explicit DTO envelopes and schema version. Markdown gets metadata/summary and escaped streamed tables.
5. Add an App export view model and owned WPF dialog. Profiles: Asset Catalog, Asset Inventory, Issues Report, Completeness Report, Scan Changes and Smart Collection Result. Scopes: Entire Library, Current View and Selected Asset. Selected Assets intentionally means the current single selected card because the application has no multiple selection model.
6. Snapshot Entire Library from the current `allAssets`, Current View from the already-filtered/sorted card collection, Selected Asset from `SelectedCard`, and Smart Collection from the selected versioned record using `SmartCollectionPolicy`. Load only completed scan runs and reject stale/non-completed selections.
7. Add destination selection, conditional options, estimate, overwrite confirmation, progress phases, processed/written counters, elapsed time, cancel, success/failure state and source-generated structured log events.
8. Wire DI and a main-toolbar `Export Report` entry point. Do not start a scan, touch previews, modify SQLite, or inspect binary asset contents.
9. Add unit/integration/view-model tests for schema version, filenames, mapping, CSV BOM/escaping/invariant output, JSON envelope, Markdown escaping, cancellation/temp cleanup, overwrite preservation, profile validation and dialog state.
10. Update README and architecture/export documentation. Run targeted tests, Release restore/build/all tests, format verification and `git diff --check`.
11. Rebuild code-review-graph, inspect change impact, refresh Graphify because new cross-project report boundaries and a new WPF entry point are architectural changes, and create implementation/review reports.

