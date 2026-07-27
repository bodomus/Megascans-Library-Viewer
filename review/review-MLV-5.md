# Review MLV-5

## Outcome

MLV-5 is implemented. ScanVault now has one repository-wide product version (`0.2.0`), stable SDK selection, build-time commit metadata, a compact versioned window title, structured startup identity, and Windows GitHub Actions CI for pushes and pull requests.

## Main changes

- `Directory.Build.props` owns product, assembly, file, and informational versions for every project.
- `global.json` selects stable SDK `10.0.302` with `latestPatch` and prereleases disabled.
- Local builds use `0.2.0-dev` and succeed without Git metadata.
- CI injects `0.2.0-ci.<run>+<short-sha>` without modifying tracked files.
- `ApplicationBuildInfo` reads generated assembly metadata only; no Git command is executed at application runtime.
- Main title displays `ScanVault — Megascans Library Viewer 0.2.0` and excludes SHA/build noise.
- Startup logging includes product/informational versions, commit, configuration, runtime, OS, and architecture as structured fields.
- `.github/workflows/ci.yml` restores, builds, tests, checks formatting/whitespace, and uploads failure diagnostics on Windows.
- CI has read-only contents permission and creates no releases or packages.

## Documentation and artifacts

- Specification: `Tickets/MLV-5.md`.
- Investigation: `Task/MLV-5/investigation.md`.
- Plan: `Task/MLV-5/implementation-plan.md`.
- Full implementation report: `Task/MLV-5/implementation-report.md`.
- Version/CI policy: `Docs/versioning-and-ci.md`.
- Updated: `README.md`, `Docs/architecture.md`.

## Validation

- Release restore: passed.
- Release build: passed, 0 warnings / 0 errors.
- Release tests: 59 passed / 0 failed / 0 skipped.
- Formatter verification: passed.
- `git diff --check`: passed.
- CI-like metadata build: `0.2.0-ci.42+0123456`, passed.
- Local metadata build without Git input: `0.2.0-dev`, passed.
- JSON/XML/YAML syntax checks: passed.
- CRG refreshed: 410 nodes / 823 edges / 63 C# files; low reported risk (`0.40`), no affected Core/Infrastructure flow.
- Graphify refreshed: 1121 nodes / 1842 edges / 67 communities.

## Copies and source safety

- Megascans source copies: none.
- Megascans source modifications: none.
- Persistent backup copies: none.
- Copy paths: not applicable; no copies were created.
- The required ticket markdown is stored at `Tickets/MLV-5.md` and will be attached to YouTrack.

## Explicit limitations

- Hosted GitHub Actions success is not claimed until the commit is pushed and GitHub runs the workflow.
- Branch protection recommendations are documented; repository settings were intentionally not changed.
- No release, publish, installer, signing, updater, or telemetry workflow was added.
