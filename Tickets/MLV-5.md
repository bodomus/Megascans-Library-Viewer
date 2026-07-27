# MLV-5 — Application versioning and GitHub Actions CI

## Status
Ready for implementation.

## Project
**ScanVault — Megascans Library Viewer**

## Summary
ScanVault needs reproducible versioning, deterministic SDK selection, and continuous integration on every push and pull request.

This ticket introduces:
- centralized application versioning;
- stable SDK pinning;
- build metadata with commit SHA;
- version visibility in the application and logs;
- GitHub Actions CI for Windows/WPF;
- build, test, formatting, and whitespace validation;
- branch-protection guidance.

This ticket must not create releases or publish end-user packages.

## Mandatory workflow
This is a **Level 2 structural task**.

Before implementation, Codex must:
1. Resolve the repository root.
2. Read `AGENTS.md`.
3. Read `.codex/PRE_TICKET_WORKFLOW.md`.
4. Execute `$graphify-repository-analysis`.
5. Execute `$code-review-graph-analysis`.
6. Inspect project files, build settings, startup logging, window title, tests, and documentation.
7. Validate graph findings against source.
8. Create:
   - `Task/MLV-5/investigation.md`;
   - `Task/MLV-5/implementation-plan.md`.
9. Implement only after the plan is complete.
10. Update CRG after implementation.
11. Inspect blast radius.
12. Run validation.
13. Refresh Graphify only if architecture or project boundaries changed.
14. Create `Task/MLV-5/implementation-report.md`.

Do not invent Graphify or CRG commands. Preserve unrelated user changes.

## Goal
Provide one authoritative version source and ensure every push and pull request is validated automatically on GitHub.

The result must identify:
- application version;
- producing commit;
- supported .NET SDK line;
- restore/build/test/format status.

## Versioning policy
Use Semantic Versioning:

```text
MAJOR.MINOR.PATCH
```

Recommended initial version:

```text
0.2.0
```

If the repository already defines a version, preserve continuity and document the final decision.

## Centralized version source
Create one authoritative version definition, preferably in:

```text
Directory.Build.props
```

Requirements:
- no conflicting per-project versions;
- no manual duplication;
- deterministic builds;
- local builds remain clear;
- CI appends build metadata without editing tracked files;
- no automatic version commits.

Recommended properties include:
- `VersionPrefix`;
- `VersionSuffix`;
- `AssemblyVersion`;
- `FileVersion`;
- `ContinuousIntegrationBuild`;
- `Deterministic`.

## Informational version and commit SHA
Expose a human-readable informational version, for example:

```text
0.2.0-dev+abc1234
0.2.0-ci.42+abc1234
```

Requirements:
- include short commit SHA when available;
- local builds without Git metadata still succeed;
- inject commit metadata at build time;
- do not execute Git synchronously at application runtime;
- avoid third-party versioning packages unless clearly justified.

Document the difference between:
- product version;
- assembly version;
- file version;
- informational version.

## SDK pinning
Add `global.json`.

Requirements:
- stable SDK already validated for the solution;
- `rollForward: latestPatch`, unless investigation justifies another compatible policy;
- `allowPrerelease: false`;
- no machine-specific paths;
- README documents required SDK;
- CI uses the same SDK line.

Codex must record the selected SDK in the implementation report.

## Application UI
Display compact product version information in the existing application shell.

Example:

```text
ScanVault — Megascans Library Viewer 0.2.0
```

Requirements:
- read from build metadata;
- no duplicated hardcoded version;
- do not display noisy SHA in the main title;
- degrade gracefully when metadata is unavailable.

A full About window is deferred to MLV-6.

## Startup logging
Log one structured startup event containing:

```text
ApplicationVersion
InformationalVersion
CommitSha
BuildConfiguration
RuntimeVersion
OperatingSystem
ProcessArchitecture
```

Do not add telemetry or external reporting.

## GitHub Actions CI
Create:

```text
.github/workflows/ci.yml
```

### Triggers
Run on:

```yaml
on:
  push:
  pull_request:
```

### Runner
Use:

```yaml
runs-on: windows-latest
```

### Required steps
1. Checkout repository.
2. Set up pinned .NET SDK.
3. Restore.
4. Build Release.
5. Run tests.
6. Verify formatting.
7. Run `git diff --check` or equivalent.
8. Upload useful test diagnostics on failure where practical.

Use repository-supported exact commands, equivalent to:

```powershell
dotnet restore ScanVault.sln
dotnet build ScanVault.sln --configuration Release --no-restore
dotnet test ScanVault.sln --configuration Release --no-build
dotnet format ScanVault.sln --verify-no-changes --no-restore
git diff --check
```

### CI requirements
- fail on restore/build/test/format errors;
- no release creation;
- no repository mutation;
- no generated version commits;
- no secrets;
- clear workflow/job names;
- dependency caching only if simple and reliable;
- avoid stale runs for superseded pushes where practical.

### CI build metadata
Provide a CI suffix such as:

```text
ci.<run_number>
```

Requirements:
- do not alter `VersionPrefix`;
- do not write generated version files;
- include commit SHA in informational version;
- preserve deterministic build behavior.

## Branch protection guidance
Update documentation with recommendations for `master`:
- require CI status check;
- block merge when CI fails;
- prefer pull requests;
- optionally require branch to be current before merge.

Do not change GitHub repository settings automatically.

## Tests
Add or update tests for:
- product-version service/helper;
- informational-version fallback;
- unavailable commit metadata;
- main-window/view-model version display;
- startup version fields where practical;
- absence of duplicated hardcoded UI version logic.

Do not assert a specific current Git SHA.

## Documentation
Update:

```text
README.md
Docs/architecture.md
Docs/versioning-and-ci.md
```

Document:
- version policy;
- authoritative version location;
- local and CI build behavior;
- SDK policy;
- CI commands;
- branch protection;
- explicit absence of release publishing.

## Acceptance criteria
- [ ] One authoritative product-version source exists.
- [ ] No conflicting per-project versions remain.
- [ ] `global.json` pins the stable SDK line.
- [ ] Application displays version from build metadata.
- [ ] Startup log contains structured version/build/runtime information.
- [ ] Informational version includes commit metadata when available.
- [ ] Local builds succeed without Git metadata.
- [ ] CI runs on push and pull request.
- [ ] CI uses Windows.
- [ ] CI restores, builds, tests, verifies formatting, and checks whitespace.
- [ ] CI creates no releases and commits no generated files.
- [ ] CI build metadata requires no tracked-file edits.
- [ ] Documentation is updated.
- [ ] Tests pass.
- [ ] Release build succeeds.
- [ ] CRG is updated and impact reviewed.
- [ ] Graphify is refreshed only if justified.
- [ ] Investigation, plan, and implementation report exist.

## Explicit non-goals
Do not implement:
- release workflow;
- GitHub Release;
- installer;
- self-contained or single-file publish;
- code signing;
- auto-update;
- branch-protection API changes;
- diagnostics window;
- index compatibility UI;
- asset inventory;
- cosmetic redesign.

## Required deliverables

```text
Directory.Build.props
global.json
.github/workflows/ci.yml
Task/MLV-5/investigation.md
Task/MLV-5/implementation-plan.md
Task/MLV-5/implementation-report.md
README.md
Docs/architecture.md
Docs/versioning-and-ci.md
```

## Validation
Run repository-supported equivalents of:

```powershell
dotnet --info
dotnet restore ScanVault.sln
dotnet build ScanVault.sln --configuration Release --no-restore
dotnet test ScanVault.sln --configuration Release --no-build
dotnet format ScanVault.sln --verify-no-changes --no-restore
git diff --check
```

Validate workflow YAML locally where possible.

Do not claim a hosted GitHub Actions run passed until it actually runs on GitHub.

## Definition of done
ScanVault has centralized versioning, reproducible SDK selection, visible version metadata, and a GitHub Actions CI workflow validating every push and pull request without publishing releases.
