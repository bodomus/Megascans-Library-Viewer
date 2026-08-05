# Export Reports

ScanVault exports read-only reports from the current SQLite index. It does not rescan the library, load previews, read binary FBX/ABC/texture contents, copy assets, or modify index rows.

## Entry point and profiles

Open **Export Report** from the main toolbar. The first version provides these built-in profiles:

- **Asset Catalog** — one stable catalog row per asset;
- **Asset Inventory** — one flat row per indexed mesh, texture, or unclassified file;
- **Issues Report** — one row per issue with a stable `IssueCode`;
- **Completeness Report** — catalog rows restricted to selected completeness states;
- **Scan Changes** — persisted changes from a selected completed MLV-9 scan run;
- **Smart Collection Result** — the evaluated result and versioned conditions of a selected MLV-8 collection.

Scopes are **Entire Library**, **Current View**, and **Selected Asset**. Current View snapshots the already filtered and sorted card collection, so it has the same folder, search, inventory-filter, smart-collection, and sort semantics as the UI. Scan Changes and Smart Collection Result use their own source selectors. ScanVault currently has single selection, so Selected Asset exports only the current card; MLV-11 does not introduce a hidden multi-selection model.

## Formats and stable contract

The report schema is versioned independently as `ReportSchemaVersion = 1`. Export code maps indexed domain models into explicit DTOs; it never serializes view models, WPF columns, or SQLite rows directly. Unavailable indexed facts, such as a reliable per-file size or per-file last-write time for legacy inventory entries, remain empty/null.

### CSV

- UTF-8 with BOM for Windows Excel compatibility;
- fixed comma delimiter, independent of current culture;
- stable machine-readable headers;
- invariant numbers and ISO 8601 UTC dates;
- RFC-4180-style quoting for commas, quotes, CR/LF, semicolons and Unicode values;
- streamed rows;
- optional companion `<report>.csv.metadata.json` metadata file.

### JSON

- UTF-8 object with `reportSchemaVersion`, `metadata`, and `rows`;
- camel-case stable DTO property names;
- optional pretty printing;
- streamed row writes rather than a complete in-memory report model.

### Markdown

- title, optional metadata, summary, and a table;
- pipes, backticks, backslashes, and line breaks are escaped;
- streamed rows; very large inventories are valid but CSV or JSON is normally more practical.

## Metadata and path privacy

Metadata includes report/format/scope, generation time, application and commit identity, compatible index versions, filter/sort summaries, asset count, and row count. Scan-run fingerprint metadata and smart-collection name/description/definition are included when applicable.

Relative paths are the default. Absolute asset/file paths plus library identity/root are emitted only when **Include absolute paths** is explicitly enabled.

## File safety, progress, and cancellation

The writer creates a uniquely named sibling temporary file, streams and flushes the complete report, then publishes it under the final name. Existing output is replaced only after the WPF confirmation prompt. Cancellation or serialization/write failure removes staging files and never publishes a partial report under the destination name. A previously existing destination remains intact until finalization.

The dialog reports Preparing query, Reading assets, Writing report, Finalizing, processed assets, written rows, and elapsed time. Cancellation tokens flow through scan-history reads and every writer. Progress is throttled to batches of 128 rows to avoid flooding the dispatcher.

## Large datasets and limitations

Synthetic validation covers 10,000 catalog assets, 100,000 inventory rows, 10,000 JSON assets, and 5,000 Markdown issues. The export reuses the application's current immutable indexed read model; it does not add another complete report-sized collection. Scan changes are fetched from the persisted index and do not establish a new scan baseline.

The first version does not provide XLSX, PDF, HTML, custom profile design, scheduled/CLI export, import, cloud/email publication, preview export, asset archives, or binary hashes. Diagnostics does not persist the destination path; current and last-operation state is shown in the export dialog and structured logs omit the full destination path.
