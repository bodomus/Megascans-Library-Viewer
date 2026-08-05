# Review MLV-11 — Export reports

## Result

MLV-11 is implemented on `codex/mlv-11`. ScanVault now exports the current index as CSV, JSON, or Markdown through a responsive WPF dialog with built-in profiles, exact Current View semantics, MLV-8/MLV-9 integration, progress, cancellation, safe staging and explicit relative/absolute path policy.

## Review highlights

- Dependency direction remains valid: Core owns contracts/DTOs/policy; Infrastructure owns writers/file publication; App owns WPF interaction.
- Export is read-only: no rescan, SQLite write, preview load, binary asset read, or source-library mutation is reachable from the export path.
- Format contracts are explicit and versioned (`ReportSchemaVersion = 1`).
- CSV uses UTF-8 BOM, invariant formatting, stable headers, robust quoting and companion metadata.
- JSON and Markdown are streamed from explicit rows, not ViewModels.
- Existing destination publication is deferred until the complete staging output is flushed; cancellation and failure tests verify cleanup/preservation.
- Current View reuses the already applied folder/search/filter/smart-collection/sort result.
- Scan Changes requires a completed run; Smart Collection Result preserves versioned conditions and current evaluated results.

## Validation

- Release build: passed, 0 warnings/errors.
- Full tests: 116 passed, 0 failed.
- Format verify: passed.
- `git diff --check`: passed.
- Synthetic performance: 10k catalog CSV, 100k inventory CSV, 10k JSON and 5k-issue Markdown passed; sampled peak managed memory 66.8 MB; cancellation latency 1.99 ms.
- Graphify refreshed successfully and includes the new architecture.
- CRG tracked-file impact completed; new untracked files are a documented CLI coverage limitation until staging.

## Remaining manual checks

Excel desktop and a visual Markdown renderer were not automated, and GitHub Actions was not run because the branch was not pushed. Automated byte/parser/escaping validation and the complete local CI-equivalent suite passed. See `Task/MLV-11/implementation-report.md` for exact commands, measurements, limitations and graph evidence.
