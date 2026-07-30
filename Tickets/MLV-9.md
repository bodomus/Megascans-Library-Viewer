# MLV-9 — Scan history and change detection

Source: https://bodomus.youtrack.cloud/issue/MLV-9

## Summary

Add ScanVault scan history and change detection between consecutive successful compatible scans of the same library.

After `Rescan`, persist a scan run, compare it to the previous completed baseline for the same library, and show a summary:

```text
Added:      18
Changed:     7
Removed:     2
Unchanged: 9431
```

The user must be able to open Scan History, inspect runs and change categories, and navigate from a change row to an existing asset. Removed assets must not offer navigation.

## Required implementation areas

- `ScanRun` persistence with `Running`, `Completed`, `Cancelled`, `Failed`.
- Library identity based on normalized absolute root path.
- Snapshot/fingerprint persistence that survives current `assets` table replacement.
- Deterministic asset identity using Megascans asset ID first, normalized relative JSON path fallback.
- Fingerprint versioning, using indexed metadata, content inventory, and physical file properties: normalized relative path, size, last-write UTC.
- Change categories: `Added`, `Changed`, `Removed`, `Unchanged`.
- Initial baseline behavior.
- Retention: keep last 20 completed runs per library; keep bounded failed/cancelled history.
- SQLite schema migration without deleting existing indexes.
- Post-scan summary and `Scan History` UI.
- Diagnostics/docs/tests/review artifacts.

Full ticket text is stored in YouTrack and attached there as `MLV-9-Scan-history-and-change-detection.md`.
