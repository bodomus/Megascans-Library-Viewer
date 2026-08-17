# MLV-12 Duplicate detection

Source: https://bodomus.youtrack.cloud/issue/MLV-12

## Goal

Add read-only duplicate detection for indexed Megascans assets. The feature must not delete, move, or modify source library files.

## Categories

- Exact ID Duplicate
- Conflicting ID Duplicate
- Exact Content Duplicate
- Probable Duplicate
- Partial Duplicate

## Requirements

- Run duplicate analysis from a separate command/window.
- Hash only candidate files with streaming SHA-256 and a cached `HashAlgorithmVersion`.
- Reuse hashes when size and `LastWriteTimeUtc` are unchanged.
- Support cancellation and avoid UI-thread hashing.
- Persist `FileHashCache`, `DuplicateAnalysisRuns`, `DuplicateGroups`, `DuplicateGroupMembers`, and `DuplicateReasons`.
- Keep library roots separate.
- Do not let cancelled or failed runs replace the latest completed result.
- Mark completed duplicate results stale after Rescan.
- Show summary, groups, group details, filters, open asset/folder, compare pair, and export group handoffs.

## Acceptance

- Same Asset ID, conflicting same ID, exact content, probable, and partial duplicates are classified with explainable reasons.
- File order does not affect results.
- Low-confidence results are not reported as exact.
- Hash cache survives restart and invalidates by size, timestamp, or algorithm version.
- Cancellation is safe.
- The library is not modified.
- Build/tests pass.
