# MLV-19 — Implementation report

## Result

Implemented a startup loading window without changing the virtualization component. The application now owns the complete transition from loading to the main window and closes loading before the first `MainWindow` layout.

## Changes

- Added a presentation-only `LoadingWindow` with an indeterminate progress indicator.
- Changed `App.OnStartup` to use this order:
  1. `ShutdownMode.OnExplicitShutdown`;
  2. show `LoadingWindow`;
  3. initialize the host and `MainViewModel`;
  4. create and assign `MainWindow`;
  5. close `LoadingWindow`;
  6. show `MainWindow`;
  7. switch to `ShutdownMode.OnMainWindowClose`.
- Kept `VirtualizingWrapPanel.cs` byte-for-byte unchanged from commit `b50bd27`.
- Added a regression UI test with 200 asset cards and a real `DispatcherFrame`. It verifies that an `Input`-priority callback executes within five seconds and that the final item remains unrealized, preserving virtualization.
- Documented the startup lifecycle in `Docs/architecture.md`.

## Validation

- `dotnet restore ScanVault.sln --disable-parallel` — passed.
- `dotnet build ScanVault.sln --configuration Release --no-restore --disable-build-servers -maxcpucount:1 -nodeReuse:false` — passed, 0 warnings, 0 errors.
- `dotnet test ScanVault.sln --configuration Release --no-build --no-restore --disable-build-servers -maxcpucount:1 -nodeReuse:false` — passed: Core 110, Infrastructure 65, App 57; total 232.
- Targeted `MainWindowTests.RealizesApplicationWindowsWithResponsiveLoadingToMainTransition` — passed.
- `git diff --check` — passed (Git only reported line-ending notices).
- `git diff --exit-code b50bd27 -- src/ScanVault.App/Controls/VirtualizingWrapPanel.cs` — no differences.

## Code intelligence

- Graphify was refreshed before and after implementation. Post-change graph: 175 files, 2641 nodes, 6289 edges, 115 communities.
- CRG was built before implementation and updated afterward. Reported change risk: 0.35, no affected flows. CRG completed its update but its console rendering ended with the known Windows CP1251 `UnicodeEncodeError`; this did not affect build or test results.

## Remaining acceptance check

The executable was started, but this environment exposes browser tabs only and cannot control native WPF windows. Therefore mouse input, keyboard input, scrolling, and search were not claimed as verified. Per the ticket, `Process.Responding` was not used as proof. The ticket should not be marked ready/complete until the user performs those four interactions on a real library.
