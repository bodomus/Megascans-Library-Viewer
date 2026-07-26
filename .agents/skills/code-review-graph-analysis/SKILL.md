---
name: code-review-graph-analysis
description: Build or update ScanVault code-review-graph and inspect exact symbols, imports, callers, callees, dependants, tests, review context, and change impact.
---

# Code review graph analysis

Run at the repository root after reading `AGENTS.md`, `.codex/PRE_TICKET_WORKFLOW.md`, and the ticket. CRG complements source inspection, analyzers, compilation, tests, and runtime checks; it replaces none of them.

## Workflow

1. Discover exact installed syntax with `code-review-graph --help` and repository configuration. Do not guess commands.
2. Inspect `.code-review-graph/`; prove freshness with a successful status/update/query rather than timestamps alone.
3. Build/update against the repository root. Exclude `.git/`, IDE state, `bin/`, `obj/`, artifacts, test results, graph databases, caches, user settings, and local SQLite indexes; include production source, tests, project/build files, migrations, fixtures, and maintained docs.
4. For the ticket or diff inspect projects/namespaces, interfaces and implementations, DI registrations, constructors, command/event entry points, callers/callees, imports, dependants, serialization/persistence contracts, async/cancellation paths, WPF boundaries, SQLite transaction/connection lifetime, and related tests.
5. Validate all important results directly in source. Confirm direction, active registration/reachability, signatures, awaited flow, transaction boundaries, dispatcher affinity, disposal, and behavioral assertions.
6. Classify impact as direct, adjacent, test-only, or graph-proximity noise. Do not modify neighbors without source justification.
7. After implementation update CRG and inspect changed symbols, review context, blast radius, expected/unexpected dependants, tests, disconnected new code, obsolete reachable paths, and dependency-direction violations.
8. Record status, exact commands, indexed coverage, key findings, source verification, tests, impact, risks, and limitations.

If CRG fails, preserve existing state, record the exact command/error, continue with Graphify plus `rg`/source/build/tests, and never fabricate relationships or impact.