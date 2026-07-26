---
name: graphify-repository-analysis
description: Build, refresh, inspect, and query the ScanVault Graphify knowledge graph for architecture, project boundaries, semantic relationships, and cross-file discovery.
---

# Graphify repository analysis

Run at the repository root after reading `AGENTS.md`, `.codex/PRE_TICKET_WORKFLOW.md`, and the ticket. Graphify is supporting navigation evidence; source and executable validation are authoritative.

## Workflow

1. Confirm installed syntax with `graphify --help`; do not invent commands or change backend/model settings without permission.
2. Inspect `graphify-out/` and run a focused query to assess usability and coverage.
3. Rebuild or refresh only when absent, unreadable, query failures occur, relevant files/symbols are missing, or architecture/project boundaries changed.
4. Exclude `.git/`, `.idea/`, `.vs/`, `bin/`, `obj/`, `artifacts/`, `TestResults/`, `.coverage/`, `graphify-out/`, `.code-review-graph/`, `.scanvault/`, local databases, caches, and user settings. Keep source, project files, build configuration, migrations, fixtures, tests, and maintained docs.
5. Query concrete ticket vocabulary: projects, namespaces, interfaces, services, view models, commands, WPF windows/controls, settings, scanner, parser, SQLite index, image loader, cache, and tests.
6. Identify owning subsystem, project boundaries, entry points, important relationships, tests, and likely change surface.
7. Open source to verify every finding that affects implementation. A graph path is connectivity, never proof of runtime flow.
8. Record graph status, exact commands, focused queries, useful findings, source validation, discrepancies, and limitations.

Refresh after implementation when the solution structure, DI composition root, subsystem boundaries, entry points, or important cross-project relationships changed. Do not refresh blindly for small local edits.

If Graphify fails, preserve existing artifacts, report the confirmed command/error, continue with CRG plus `rg`/source/build/tests, and never fabricate findings.