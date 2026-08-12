# Review MLV-10 — Asset comparison

MLV-10 is implemented on `master` as a read-only, explicit two-slot comparison workflow.

## Delivered

- Single-card navigation remains intact; comparison uses a separate visible tray.
- Exactly two different assets are required; a third distinct choice replaces FIFO.
- Asset Comparison provides Overview, Variants and LODs, Texture Sets, Files, and Issues tabs.
- Explicit Equal/Different/Only left/Only right/Unknown/Not applicable/Ambiguous semantics are shown as text plus restrained visual emphasis.
- Variants, LODs, textures, logical files, issue flags, and issues use normalized stable keys; order does not matter and duplicates remain Ambiguous.
- Differences-only, Swap, Replace, Refresh, Open asset/folder/inventory, Esc, and `Ctrl+Shift+C` are implemented.
- Opening snapshot remains stable; successful Rescan marks it stale, and Refresh isolates removed sides.
- Comparison is cancellable, virtualized, does not read binary content, and does not write the library or SQLite index.
- README and `Docs/asset-comparison.md` document behavior and limits.

## Verification

- Release restore/build: passed, 0 build warnings/errors.
- Tests: 126/126 passed (Core 56, App 29, Infrastructure 41).
- Format verification: passed (exit 0; non-fatal workspace warnings).
- `git diff --check`: passed.
- 2,000 logical files per side, two comparisons: 95 ms focused test case.
- Post-change CRG updated; Graphify refreshed to 1,791 nodes / 4,035 edges and confirmed policy-to-ViewModel/test reachability.

## Known limits

File size/mtime and issue severity are not present in the current index and are displayed/treated as Unknown rather than inferred from live files. No binary, pixel, geometry, hash, score, recommendation, merge, or report export is included. Real-library interactive testing and GitHub Actions remain unverified because no user asset access, push, or PR was performed.
