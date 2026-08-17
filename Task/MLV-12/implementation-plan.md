# MLV-12 Implementation Plan

1. Add Core duplicate-analysis contracts and deterministic classification policy.
2. Add `IDuplicateAnalysisService` and duplicate result persistence methods to `IAssetIndex`.
3. Migrate SQLite to schema v5 with file hash cache and duplicate analysis tables.
4. Implement infrastructure service that generates candidates from indexed assets, hashes candidate files with cached streaming SHA-256, classifies groups, and persists completed runs.
5. Mark latest duplicate analysis stale when a full Rescan commits.
6. Add WPF view model/window and main-window entry point.
7. Add focused unit/integration tests for classification, hash cache, stale state, cancellation, and persistence separation.
8. Update CRG, inspect impact, run build/tests, and create implementation/review reports.
