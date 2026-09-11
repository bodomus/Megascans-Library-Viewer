# Review MLV-16 — Global library search

## Outcome

MLV-16 is implemented on branch `codex/MLV-16` from clean `master` commit `57eef301969f477c3a59dfebc797d3bc618b4c6a`.

The application now has a dedicated global-search mode that searches the loaded index across folders and local filters, supports safe `*`/`_` masks, exposes deterministic match reasons and relative locations, filters results by major asset type, rejects stale async results, reports errors without closing the app, restores local view state on Clear, and supports Show in Library and Find related by Asset ID.

## Review notes

- Dependency direction remains valid: Core owns pure matching policy; App owns async execution, DI, WPF state and presentation.
- No SQLite/index-format change and no per-query filesystem access.
- Cancellation/generation checks prevent stale publication.
- Regex input is escaped, length-limited and timeout-bounded.
- Observable collections are published after awaited worker execution on the captured UI context.
- Show in Library uses JSON-path identity to disambiguate duplicate IDs.
- Existing navigation, smart collections, sorting, card actions and inventory filters are preserved.
- Find related is implemented using the existing Asset ID and needs no follow-up schema ticket.

## Evidence

- Release build: passed with 0 warnings and 0 errors.
- Tests: Core 105, Infrastructure 65, App 53; total 223 passed.
- CRG updated; risk 0.50, no affected flows reported. Apparent test gaps are coarse graph results for WPF/DI/logging nodes and were checked against source, XAML compilation and direct tests.
- Graphify refreshed and resolves the new policy/service/ViewModel/UI/test path.

## Acceptance gap

Interactive WPF screenshot capture could not be completed: the launched process exposed no main-window handle to this environment. The required real-library screenshots were not fabricated and remain to be captured manually.

## Files

See `Task/MLV-16/implementation-report.md` for the complete changed-file list, commands, findings and remaining risks.

