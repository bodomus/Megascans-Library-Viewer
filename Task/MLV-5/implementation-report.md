# MLV-5 — Implementation report

## Identity

- YouTrack: `MLV-5`
- Project: Megascans Library Viewer (`MLV`)
- Branch: `master`
- Initial commit: `37cd56d112250118e1c8ec140469e995aea8c6c9`
- Assignee retained in YouTrack: `bodomus`
- Selected product version: `0.2.0`
- Selected stable SDK baseline: `10.0.302`, `latestPatch`, prereleases disabled

## Implemented result

### Central version and build identity

- `Directory.Build.props` is the sole product-version source for all six projects.
- Local builds generate `0.2.0-dev`, assembly/file version `0.2.0.0`, and require no Git metadata.
- CI supplies `VersionSuffix=ci.<run>` and a seven-character `CommitSha` through MSBuild properties without editing tracked files.
- SDK automatic source-revision suffixing is disabled to prevent duplicate or environment-dependent informational versions.
- Generated assembly metadata carries product version, optional commit SHA, and build configuration.

### Application UI and diagnostics

- Added immutable `ApplicationBuildInfo`; it reads loaded-assembly attributes only and performs no runtime Git, process, network, or filesystem access.
- Missing metadata falls back to `Unknown` / `unavailable` without preventing startup.
- Registered build information in the App composition root and injected it into `MainViewModel`.
- `MainWindow.Title` binds to the view model and displays `ScanVault — Megascans Library Viewer 0.2.0`; suffix and SHA are intentionally omitted.
- Startup event 2001 now records structured `ApplicationVersion`, `InformationalVersion`, `CommitSha`, `BuildConfiguration`, `RuntimeVersion`, `OperatingSystem`, and `ProcessArchitecture` fields.

### GitHub Actions

- Added `.github/workflows/ci.yml` for every push and pull request on `windows-latest`.
- Uses read-only repository contents permission and cancels superseded runs.
- Uses `actions/checkout@v6`, `actions/setup-dotnet@v5` with `global.json`, and `actions/upload-artifact@v7` for failure diagnostics.
- Runs restore, Release build with injected metadata, tests with TRX output, formatter verification, and whitespace validation.
- Does not use secrets, mutate version files, commit changes, publish packages, or create releases.

### Tests and documentation

- Added unit coverage for explicit metadata, informational-version derivation, unavailable metadata, short SHA behavior, generated assembly attributes, and title noise exclusion.
- Added direct view-model version/title assertions and a WPF title-binding assertion.
- Added `Docs/versioning-and-ci.md`; updated README prerequisites/policy and architecture build-identity flow.
- Documented manual `master` branch-protection recommendations without changing GitHub settings.

## Graph analysis

### Preflight

- Graphify query oriented the work to the App composition root, `ApplicationLog`, shell/view model, projects, tests, and documentation.
- CRG preflight contained 396 nodes / 791 edges / 61 C# files at commit `37cd56d11225`.
- Every candidate used for implementation was verified against source and tests.

### Post-change

- CRG final incremental update indexed 410 nodes / 823 edges / 63 C# files and analyzed 19 changed files with 22 changed symbols/classes.
- CRG reported low overall risk (`0.40`) and no affected Core/Infrastructure flows. Its generic “untested” list includes WPF startup/logging entry points and the new record as aggregate symbols, while pure metadata methods, view-model propagation, XAML title binding, compile-time logger generation, and full-solution behavior are covered by tests/build.
- Graphify was refreshed because App DI and metadata-to-UI relationships changed: 1121 nodes / 1842 edges / 67 communities.
- The focused post-change query found `ApplicationBuildInfo`, `FromAssembly`, `Create`, `App.OnStartup`, `ApplicationLog.Starting`, `MainViewModel`, the XAML window, ticket, and all four new metadata tests in the expected App-owned communities.
- `global.json` intentionally produces no Graphify AST node; JSON syntax and selected values were validated separately.

## Validation

Working directory for every command:

`J:\Projects\UE_Projects\Megascans Library Viewer`

| Command/check | Result |
|---|---|
| `dotnet restore ScanVault.sln` | Passed; all 6 projects restored/up to date |
| `dotnet build ScanVault.sln --configuration Release --no-restore` | Passed; 0 warnings, 0 errors |
| `dotnet test ScanVault.sln --configuration Release --no-build` | Passed; 59 total, 0 failed, 0 skipped |
| `dotnet format ScanVault.sln --verify-no-changes --no-restore` | Passed |
| `git diff --check` | Passed |
| `global.json` JSON parse | Passed; `10.0.302`, `latestPatch`, no prerelease |
| `Directory.Build.props` XML parse | Passed |
| `.github/workflows/ci.yml` PyYAML parse | Passed |

Permanent test totals:

- Core: 29 passed;
- Infrastructure: 20 passed;
- App/WPF: 10 passed;
- Total: 59 passed.

Build-metadata checks:

- local build generated informational version `0.2.0-dev` with no required commit attribute;
- CI-like build with `VersionSuffix=ci.42` and `CommitSha=0123456` generated `0.2.0-ci.42+0123456`;
- CI-like build generated assembly/file version `0.2.0.0` and metadata values `ProductVersion=0.2.0`, `CommitSha=0123456`, `BuildConfiguration=Release`;
- the repository contains no per-project version property conflicts.

An attempted Debug build could not replace DLLs because the user's already-running `ScanVault.App` process (PID 32028 at that time) held the Debug outputs. The process was deliberately not stopped. All required verification used the independent Release outputs and passed.

## Copies and source safety

- Source Megascans assets copied: none.
- Source Megascans assets modified: none.
- Persistent backup copies created: none.
- Copy paths: not applicable because no copies were created.
- Ticket specification saved at `Tickets/MLV-5.md`; this is a documentation artifact, not a source-asset copy.
- Build/test outputs are ordinary ignored `bin/` and `obj/` artifacts under their project directories.

## Not claimed

- A hosted GitHub Actions run has not occurred before the MLV-5 commit is pushed, so hosted CI success is not claimed.
- Branch protection was documented only; GitHub repository settings were not changed.
- No release, package, installer, signing, publishing, telemetry, or updater behavior was added.

## Remaining risk

The workflow was syntax-checked and every constituent command passed locally on Windows. The first pushed run remains the authoritative verification of GitHub runner/action availability and the configured status-check name.
