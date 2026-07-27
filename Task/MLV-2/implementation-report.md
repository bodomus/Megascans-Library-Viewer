# MLV-2 — Implementation report

## Summary

Fixed the scan failure caused by legacy Megascans JSON storing `resolution` as a string. `PreviewPathResolver` now accepts positive integer values and string values in scalar or `width x height` form. Invalid optional values are ignored instead of throwing.

## Changes

- Added type-aware, culture-invariant resolution parsing in `PreviewPathResolver`.
- Preserved numeric resolution behavior.
- Added string resolution ranking by the maximum dimension.
- Supported separators `x`, `X`, and Unicode multiplication sign.
- Treated empty, malformed, non-positive, overflowing, and unsupported values as absent.
- Changed the representative parser fixture to include a real string resolution form.
- Added resolver regression tests for `WxH` ranking and malformed values.
- Did not add a broad `InvalidOperationException` catch.
- Did not modify or add `tests/Megascan` to Git.

## Validation

### Targeted tests

```powershell
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER='1'
dotnet test tests\ScanVault.Infrastructure.Tests\ScanVault.Infrastructure.Tests.csproj --configuration Debug --no-restore -m:1 /nodeReuse:false --logger "console;verbosity=normal"
```

Result: 13/13 passed.

### Production parser against user-provided metadata

A temporary .NET 10 file-based diagnostic program referenced the production Infrastructure project and invoked `MegascansMetadataParser.ParseAsync` for every JSON below `tests/Megascan`.

```powershell
dotnet run C:\tmp\scanvault-mlv2-repro.cs
```

Result: 10 files found, 10 successful parses, 0 exceptions. The temporary program was removed after the run.

### Scan pipeline against user-provided metadata

A temporary diagnostic program invoked the production `FileSystemScanner`, `MegascansMetadataParser`, duplicate resolver, and `LibraryScanService`; a recording index prevented writes to user data.

```powershell
dotnet build-server shutdown
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER='1'
dotnet run C:\tmp\scanvault-mlv2-pipeline-scan.cs -p:UseSharedCompilation=false
```

Result:

- added/indexed/committed: 10/10/10;
- malformed: 0;
- unrelated: 0;
- inaccessible directories: 0;
- duplicate groups: 0.

The temporary diagnostic program was removed after the run.

### Release validation

```powershell
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER='1'
dotnet restore ScanVault.sln --disable-parallel -m:1 /nodeReuse:false
dotnet build-server shutdown
dotnet build ScanVault.sln --configuration Release --no-restore -m:1 /nodeReuse:false /p:UseSharedCompilation=false --verbosity:minimal
dotnet test ScanVault.sln --configuration Release --no-build -m:1 /nodeReuse:false /p:UseSharedCompilation=false --logger "console;verbosity=minimal"
```

Results:

- restore: successful, all projects up to date;
- build: successful, 0 warnings, 0 errors;
- tests: 24/24 passed (Core 8, Infrastructure 13, App 3).

Two earlier MSBuild invocations produced no output and were terminated. `dotnet build-server shutdown` plus disabling shared compilation removed the stale build-server state; the recorded successful commands above are authoritative.

## Post-change graph review

```powershell
$env:PYTHONUTF8='1'
code-review-graph update --base HEAD --repo . --brief
code-review-graph status --repo .
code-review-graph detect-changes --base HEAD --repo . --brief
```

CRG status after update: 287 nodes, 551 edges, 49 files. It reported 3 changed files, 8 changed symbols, no affected flow, risk 0.40. Its static test-gap report incorrectly marked the private parser helpers as untested; direct tests execute them through `PreviewPathResolver.Resolve` and the production metadata parser. Source and successful runtime evidence take precedence.

Graphify was not refreshed after implementation because no architecture, project boundary, entry point, or cross-project relationship changed.

## Duplicate policy and paths

The real diagnostic scan found no duplicate asset IDs, so there are no winner or skipped-copy paths to report for `tests/Megascan`.

## Remaining risk

- The WPF UI was not launched for a manual click-through scan in this run.
- The production scan pipeline and parser were executed against all supplied metadata, and SQLite behavior remains covered by the passing Infrastructure tests.
- User assets, images, and metadata were not changed.
