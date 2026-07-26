# Review MLV-1

## Verdict

**Implemented and validated with documented manual-environment limitations.**

ScanVault now has a clean .NET 10 WPF/MVVM architecture, deterministic legacy Megascans scanner/parser, transactional per-user SQLite index, physical folder navigation, virtualized/asynchronous image UI, explicit settings/rescan/cancellation, tests, documentation, and adapted Graphify/CRG workflow.

## Important confirmed behavior

- Branch remains `master` by explicit project decision.
- Startup reads the existing index and does not rescan automatically.
- Filesystem, JSON, SQLite scan work, and image decoding do not run synchronously on the WPF UI thread.
- Full-scan writes and stale deletion are one transaction; cancellation/failure rolls back to the previous usable index.
- Source asset files are never copied or modified; decoded images do not retain source-file locks.
- Simultaneous scans are blocked by command/state logic.
- Duplicate IDs are resolved deterministically and reported with full paths.

## Duplicate copies — mandatory path record

No actual user library was scanned, therefore **actual duplicate-copy paths: none observed**.

Verified automated fixture paths:

- winner: `A:\TMP\ScanVault\duplicates\A\same-id.json`;
- skipped copy: `A:\TMP\ScanVault\duplicates\B\same-id.json`;
- skipped copy: `A:\TMP\ScanVault\duplicates\C\same-id.json`.

Selection rule: lexicographically smallest normalized full path under Windows ordinal-ignore-case comparison. The scan orchestration returns the winner plus every skipped path and sends only the winner to SQLite.

## Validation evidence

- Release restore: passed.
- NuGet transitive vulnerability audit: no vulnerable packages.
- Release build: passed, 6 projects, 0 warnings/errors.
- Tests: passed, 22/22 (Core 8, Infrastructure 11, App 3).
- Formatting verification: passed.
- WPF launch smoke: passed; main window reached input-idle with the expected title and exited normally without an automatic scan.
- Final Graphify: 646 nodes, 1127 edges, 36 communities; integrity diagnostics clean.
- Final CRG: 49 source files, 283 nodes, 543 edges; generated `obj/*.g.cs` excluded by cleanup.

## Paths and deliverables

- source: `J:\Projects\UE_Projects\Megascans Library Viewer\src`;
- tests: `J:\Projects\UE_Projects\Megascans Library Viewer\tests`;
- ticket copy: `J:\Projects\UE_Projects\Megascans Library Viewer\Tickets\MLV-1.md`;
- investigation: `J:\Projects\UE_Projects\Megascans Library Viewer\Task\MLV-1\investigation.md`;
- plan: `J:\Projects\UE_Projects\Megascans Library Viewer\Task\MLV-1\implementation-plan.md`;
- detailed implementation report: `J:\Projects\UE_Projects\Megascans Library Viewer\Task\MLV-1\implementation-report.md`;
- this report: `J:\Projects\UE_Projects\Megascans Library Viewer\review\review-MLV-1.md`.

## Remaining manual checks

A populated real Megascans library was not used. Real-data scanning, large-library scrolling, hover behavior across monitor bounds, corrupt real images, long-scan cancellation, restart with a populated index, and actual inaccessible ACL directories remain manual checks. This limitation is explicit and no unexecuted item is claimed as passed.