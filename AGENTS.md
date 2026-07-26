# AGENTS.md

## Mandatory pre-ticket workflow

Before every non-trivial feature, bugfix, refactor, investigation, plan, or review:

1. Resolve the repository root with `git rev-parse --show-toplevel`.
2. Read `.codex/PRE_TICKET_WORKFLOW.md` and applicable ticket instructions.
3. Use `$graphify-repository-analysis` for architectural context.
4. Use `$code-review-graph-analysis` for exact dependencies and impact.
5. Validate graph findings against source and tests before implementation.
6. After implementation, update CRG, inspect impact, run validation, and refresh Graphify when architecture changed.

Level 0 spelling, formatting, comment-only, and metadata-only edits may skip graph preflight when graph context cannot affect correctness.

## Project scope

ScanVault is a Windows-only .NET 10 WPF/MVVM desktop application that indexes legacy Quixel Megascans metadata in a per-user SQLite database. Analysis must account for:

- solution/project dependency direction;
- WPF startup, shutdown, dispatcher and UI-thread affinity;
- MVVM state flow, commands, bindings, templates, popups, and virtualization;
- async filesystem, JSON, database, and image work;
- cancellation, disposal, stale requests, and resource lifetime;
- SQLite schema versions, transactions, indexes, and connection lifetime;
- inaccessible directories, malformed JSON, duplicate IDs, and deterministic traversal;
- image decoding, source-file handles, bounded caching, and memory pressure;
- per-user settings and invalid or changed library paths.

## Repository layout

- `src/` — production code (`App`, `Core`, `Infrastructure`).
- `tests/` — deterministic xUnit tests; never depend on the user's real library.
- `Docs/` — maintained architecture and persistence documentation.
- `Tickets/` — local ticket specifications.
- `Task/` — investigation, plan, and implementation artifacts.
- `review/` — completion reports named `review-<TICKET>.md`.
- `.agents/skills/` — repository-local skills.
- `graphify-out/` and `.code-review-graph/` — generated local graph state; never production source.

## Code-intelligence routing

Use Graphify for subsystem orientation and semantic/cross-file discovery. Use CRG for concrete symbols, imports, callers, callees, dependants, review context, and impact. Treat both as candidate generators. For exact behavior use `rg`, source inspection, tests, build output, and runtime evidence. Source wins when tools disagree.

## Change safety

- Preserve user changes; never reset, clean, stash, or revert them without explicit approval.
- Keep changes aligned with the ticket and avoid unrelated refactors.
- Never modify, move, or delete source Megascans assets.
- Keep user settings, cache, and SQLite indexes outside the repository and library.
- No blocking `.Result` or `.Wait()` on UI paths and no unobserved fire-and-forget work.
- Propagate cancellation and dispose streams, images, popups, and SQLite resources deterministically.
- Full scans may remove stale index rows only inside a successful transaction.
- Duplicate IDs use the documented deterministic policy and reports list winner plus every skipped full path.

## Build and validation

Use repository commands from the root. Record exact commands and results. Normal validation is:

```powershell
dotnet restore ScanVault.sln
dotnet build ScanVault.sln --configuration Release --no-restore
dotnet test ScanVault.sln --configuration Release --no-build
```

Do not claim a build, test, or manual UI behavior passed unless it was executed successfully.

## Ticket artifacts and completion

For `MLV-N`, keep specifications in `Tickets/MLV-N.md`, active artifacts in `Task/MLV-N/`, and the final report in `review/review-MLV-N.md`. A non-trivial ticket is complete only after preflight, implementation, post-change CRG impact inspection, required build/tests, documented risks, implementation report, and ticket-field update when possible.