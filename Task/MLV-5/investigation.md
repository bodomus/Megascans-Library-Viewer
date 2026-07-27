# MLV-5 — Investigation

## Workflow and repository state

- Level: 2 structural task.
- Repository root: `J:\Projects\UE_Projects\Megascans Library Viewer`.
- Branch: `master`.
- Initial commit: `37cd56d112250118e1c8ec140469e995aea8c6c9`.
- Initial worktree: clean.
- Ticket: `MLV-5` — Application versioning and GitHub Actions CI.
- Local ticket: `Tickets/MLV-5.md` downloaded from the existing YouTrack attachment.

## Graph evidence

### Graphify

Command:

```powershell
graphify query "MLV-5 application version assembly informational version commit SHA startup logging window title Directory.Build.props global.json GitHub Actions CI tests" --budget 5000 --graph graphify-out\graph.json
```

The current graph oriented the investigation to the App composition root, `MainWindow.xaml`, `MainViewModel`, `ApplicationLog`, all project files, test projects, and maintained documentation. MLV-5 was not yet in the graph when queried, so direct source inspection is authoritative.

### Code review graph

Commands:

```powershell
$env:PYTHONUTF8='1'
code-review-graph update --base HEAD~1 --repo . --brief
code-review-graph status --repo .
```

Status after updating through the previous committed ticket:

- 396 nodes;
- 791 edges;
- 61 C# files;
- built on `master` at `37cd56d11225`.

The exact MLV-5 change surface is primarily build configuration plus App startup/window-title entry points and App tests. No Core or Infrastructure runtime behavior needs to change.

## Existing behavior

### Version sources

- `Directory.Build.props` already centralizes language/analyzer settings, `Deterministic=true`, and conditional `ContinuousIntegrationBuild`.
- It does not define product, assembly, file, or informational versions.
- `dotnet msbuild ... -getProperty` reports the SDK defaults: `VersionPrefix=1.0.0`, `Version=1.0.0`, and no explicit assembly/file/informational property. This is an implicit SDK default, not a repository version policy.
- No project contains a conflicting version and no third-party versioning package is installed.
- Recommended initial product version `0.2.0` is therefore appropriate and does not break an explicit repository-defined version line.

### SDK selection

- Existing `global.json` pins `10.0.301`, `rollForward=latestFeature`, `allowPrerelease=false`.
- Installed SDK selected by the repository is `10.0.302`.
- MLV-4 Release restore/build/tests succeeded on `10.0.302`.
- MLV-5 requires `latestPatch`; the selected baseline should be updated to the already validated stable `10.0.302` with prereleases disabled.

### Application shell and logging

- `MainWindow.xaml` hardcodes only the product name: `ScanVault — Megascans Library Viewer`; there is no hardcoded version.
- `MainViewModel` owns shell-visible state and is the natural source for a bound `WindowTitle`.
- `App.OnStartup` owns DI and startup logging.
- `ApplicationLog.Starting` currently logs only a fixed message and has no build/runtime fields.
- Runtime Git execution is absent and must remain absent.

### CI

- `.github/workflows` does not exist.
- Remote `origin` points to `github.com/bodomus/Megascans-Library-Viewer`.
- Repository-supported commands are documented in README and have passed locally.
- Official action documentation checked on 2026-07-27 supports `actions/checkout@v6`, `actions/setup-dotnet@v5` with `global-json-file`, and `actions/upload-artifact@v7`.

## Ownership and dependency direction

- MSBuild/global SDK/workflow files remain at repository level.
- A build-information helper belongs to `ScanVault.App` because it reads CLR assembly/runtime metadata for presentation and startup logging.
- `MainViewModel` may depend on the App helper without changing project dependency direction.
- Core remains independent of WPF/build metadata; Infrastructure remains unchanged.

## Runtime and lifecycle constraints

- Metadata is read once from the already loaded App assembly; no filesystem, network, process, or Git work is performed at runtime.
- The helper is immutable and registered as a singleton before `MainViewModel` construction.
- Startup logging remains synchronous and cheap because it only formats in-memory metadata.
- No dispatcher, cancellation, SQLite, filesystem scan, image lifetime, or virtualization behavior is affected.

## Selected design

1. `Directory.Build.props` becomes the sole product-version source with `VersionPrefix=0.2.0`.
2. Local builds default to suffix `dev`; CI injects `ci.<run_number>` and a seven-character commit SHA through MSBuild properties without tracked-file edits.
3. Explicit assembly/file versions derive from `VersionPrefix`; informational version derives from prefix/suffix and optional commit metadata.
4. SDK-generated source-revision suffixing is disabled so the repository-owned informational version is deterministic and never duplicated.
5. Assembly metadata stores product version, commit SHA, and build configuration.
6. `ApplicationBuildInfo` reads assembly metadata with pure fallback logic, exposes the compact window title, and captures runtime/OS/architecture values.
7. `MainWindow.Title` binds to `MainViewModel.WindowTitle`; SHA is not displayed in the title.
8. One structured startup event logs every required field.
9. Windows GitHub Actions CI uses the global JSON, injects metadata only into Build, runs all required validations, cancels superseded runs, and uploads TRX diagnostics on failure.

## Test gaps to close

- product/informational version parsing and fallback;
- missing commit metadata;
- title contains product version but not SHA;
- MainViewModel exposes injected window title;
- XAML title binding uses view-model data;
- centralized generated assembly metadata is present;
- persisted current Git SHA is never asserted.

## Risks and exclusions

- Hosted GitHub Actions cannot be claimed as passing until a push triggers it.
- Local YAML validation can prove syntax only, not runner/action availability.
- Branch protection is documentation-only by ticket scope.
- No release, package, installer, signing, publishing, telemetry, or GitHub settings mutation is authorized.
