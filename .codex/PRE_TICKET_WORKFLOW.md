# Pre-ticket workflow

This workflow governs non-trivial ScanVault changes. Graphs assist navigation; source, tests, build output, runtime behavior, and maintained documentation are authoritative.

## Levels

- Level 0: spelling, formatting, comments, or metadata only. Read instructions and validate the edit; graph preflight is optional.
- Level 1: narrow change in a known component. Check status, update/query CRG, inspect source and tests, and use Graphify only when architectural context matters.
- Level 2: feature, multi-project change, refactor, new subsystem/entry point, broad investigation, or review. Run full Graphify and CRG preflight, create investigation and plan artifacts, and perform post-change graph validation.

When uncertain, choose Level 2.

## Mandatory order

1. Resolve root with `git rev-parse --show-toplevel`.
2. Read `AGENTS.md`, this workflow, nested instructions, and the ticket.
3. Record branch, commit, and `git status --short`; preserve all existing changes.
4. Classify the task level.
5. Verify Graphify availability, health, coverage, and freshness; create/refresh only through confirmed commands.
6. Verify CRG availability and freshness; build/update only through confirmed commands.
7. Query the owning subsystem, entry points, concrete symbols, dependants, tests, and expected impact.
8. Validate important findings with `rg`, source, project files, configuration, and tests.
9. For Level 2 create `Task/<TICKET>/investigation.md` and `implementation-plan.md`.
10. Implement the smallest coherent change.
11. Update CRG and inspect changed symbols, review context, disconnected code, and blast radius.
12. Run targeted tests, then solution Release build/tests and appropriate runtime checks.
13. Refresh Graphify when architecture, project boundaries, entry points, or important cross-project relationships changed.
14. Create `Task/<TICKET>/implementation-report.md` and `review/review-<TICKET>.md`.

## ScanVault investigation checklist

Answer before implementation:

1. What behavior exists and what is requested?
2. Which project owns the behavior and is dependency direction preserved?
3. What WPF lifecycle, dispatcher, binding, popup, or virtualization constraints apply?
4. Which work must stay off the UI thread?
5. How are cancellation, disposal, stale async results, and simultaneous operations handled?
6. Can the change lose a previous SQLite index, leave a transaction open, or hold a connection/file handle?
7. How are inaccessible folders, malformed/unrelated JSON, reparse points, duplicate IDs, missing images, and invalid settings handled?
8. Which tests cover the behavior and which are missing?

## Source validation

Graph connections are not runtime proof. Verify actual constructor/DI registration, command binding, event handler, awaited call chain, state write/read, transaction boundary, file enumeration, image decode and cache ownership. Use exhaustive `rg` searches for absence claims. Source wins on disagreement.

## Implementation rules

- Keep Core independent of WPF and SQLite.
- Infrastructure depends on Core; App composes both.
- Avoid synchronous filesystem, JSON, SQLite, or image work on the dispatcher.
- Use cancellation tokens consistently; do not publish partially completed scan results.
- Preserve the previous index unless a full transaction commits.
- Decode images with no persistent source lock and keep cache bounds explicit.
- Use generated temporary fixtures, not a user's library, in automated tests.

## Failure handling

If Graphify or CRG is unavailable, record the exact confirmed command and concise failure, continue with the other tool and direct source analysis, and report degraded evidence. Never fabricate graph results or silently install/change backends, models, or credentials.

## Required report contents

Record ticket and summary, workflow level, repository root/branch/initial commit, initial working tree, Graphify/CRG commands and status, investigation, changes, source validation, post-change impact, exact build/test/manual commands and results, duplicate groups with winner and every skipped full path, remaining risks, and unverified assumptions.