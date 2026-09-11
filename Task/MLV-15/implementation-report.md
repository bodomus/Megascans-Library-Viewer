# MLV-15 Implementation Report

## Summary

Implemented local UE Import Package UI error boundaries for package open and export failure paths. The fix keeps the original exception object for structured logging, shows short user-facing warning messages, and leaves retry state usable after export failure. MLV-14 manifest semantics were not changed.

## Changes

- `MainViewModel.UnrealImportPackageRequested` now raises the selected `AssetSummary` instead of constructing the package ViewModel inside an `async void` command path.
- `MainWindow.OnUnrealImportPackageRequested` now creates/loads `UnrealImportPackageViewModel`, constructs `UnrealImportPackageWindow`, assigns `DataContext`, and calls `ShowDialog()` inside a local error boundary.
- `UnrealImportPackageWindow.OnExportClick` now runs `ExportAsync` through a local error boundary that catches unexpected failures, keeps the dialog open, and shows the requested warning.
- `UnrealImportPackageWindow.xaml` now marks read-only `Run.Text` display bindings as `Mode=OneWay`, including `AssetType`, which was the reproduced crash source.
- `JsonPreview` is now explicitly `Mode=OneWay` in the read-only manifest preview `TextBox`.
- Added `UnrealImportPackageWindowErrorBoundary` so open/export boundaries can be tested without real modal `MessageBox` calls.
- Added `ApplicationLog.UnrealImportPackageOpenFailed` and `ApplicationLog.UnrealImportPackageExportFailed` with the exception object.
- `UnrealImportPackageViewModel.ExportAsync` no longer swallows unexpected export failures before the window boundary can report them. Its public `ExportCommand` remains guarded by a command wrapper.
- Added regression coverage for open-boundary and export-boundary failure handling.

## Diagnostics

- Original exception type: `System.InvalidOperationException`.
- Original exception message: `A TwoWay or OneWayToSource binding cannot work on the read-only property 'AssetType' of type 'ScanVault.App.ViewModels.UnrealImportPackageViewModel'.`
- Original stack trace location: WPF binding/layout path at `MS.Internal.Data.PropertyPathWorker.CheckReadOnly`, then `BindingExpression.AttachToContext`, `ContextLayoutManager.UpdateLayout`, and `HwndTarget.OnResize`.
- Root cause: `UnrealImportPackageWindow.xaml` had `<Run Text="{Binding AssetType}" />`; WPF attempted a source update for `Run.Text` during layout, but `UnrealImportPackageViewModel.AssetType` is intentionally read-only. The binding is now explicitly `Mode=OneWay`.

## Validation

- `graphify --help`: passed.
- `graphify query "MLV-15 UE Import Package open window ShowDialog ExportAsync error handling MainWindow UnrealImportPackageWindow UnrealImportPackageViewModel logging tests" --budget 4000`: succeeded but low-signal/stale for this surface.
- `code-review-graph update --base 2deb6c382809 --brief`: updated 13 files, then hit a cp1251 `UnicodeEncodeError` while printing the rich panel.
- `code-review-graph update --base HEAD --brief` with `PYTHONIOENCODING=utf-8`: passed after implementation.
- `code-review-graph detect-changes --base HEAD --brief`: passed; reported no affected flows and risk score 0.55. Direct WPF handler remains graph-untested, with helper coverage for the boundary behavior.
- `dotnet test tests\ScanVault.App.Tests\ScanVault.App.Tests.csproj --configuration Release`: blocked by missing SDK `10.0.302` required by `global.json`.
- `MSBuild.dll ScanVault.sln /t:Restore /p:Configuration=Release`: passed using installed SDK `10.0.400`.
- `MSBuild.dll ScanVault.sln /t:Build /p:Configuration=Release /p:Restore=false /p:OutDir=artifacts\MLV-15\solution-out\`: passed.
- Focused MLV-15 tests via `vstest.console.dll`: passed, 3/3, including the UE Import Package window layout regression.
- Full test DLL run via `vstest.console.dll`: Core 98/98 passed, Infrastructure 65/65 passed, App 45/46 passed. Existing failure: `ApplicationBuildInfoTests.FromAssemblyReadsGeneratedBuildMetadata` expects `0.2.0`, but `Directory.Build.props` currently emits `0.5.0`.
- Solution `dotnet format --verify-no-changes --no-restore`: blocked by missing SDK `10.0.302` from `global.json`.
- Folder-level whitespace format verification for changed C# files via SDK `10.0.400`: passed.
- `git diff --check`: passed; Git emitted CRLF normalization warnings only.

## Manual Validation

Manual WPF validation was not run. A `ScanVault.App` process was already running from `src\ScanVault.App\bin\Release\net10.0-windows` and holding Release DLLs; it was not terminated without user approval. Automated regression tests validate the non-modal error-boundary behavior.

## Remaining Risks

- The exact original crash root cause remains unknown until reproduced with logs.
- Direct `ShowDialog()` UI behavior is not covered by automated tests because real modal dialogs are unsuitable for deterministic unit tests. The package window is now realized and layout-updated in an STA UI test to catch binding crashes.
- Full solution tests are blocked from an all-green result by an existing version expectation mismatch unrelated to MLV-15.
