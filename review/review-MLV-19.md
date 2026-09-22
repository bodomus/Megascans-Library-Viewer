# Review MLV-19

## Summary

The startup loading screen is implemented at the application lifecycle level. The loading window is closed before `MainWindow.Show()`, so the virtualizing panel enters its ordinary single-window layout lifecycle and no dispatcher/layout retry loop is introduced.

## Reviewed safety properties

- `VirtualizingWrapPanel.cs` was not modified and matches `b50bd27`.
- No `ScheduleGeneratorRetry` loop or exception-driven generator retry was added.
- Startup does not mix the panel generator with a parent `ItemsControl` generator.
- Shutdown cannot occur merely because the temporary loading window closes.
- The main window becomes `Application.MainWindow` before loading closes.
- The application returns to `OnMainWindowClose` after the main window is shown.
- The loading window is also closed on startup failure.

## Automated evidence

- Release build: passed with 0 warnings and 0 errors.
- Full solution tests: 232 passed, 0 failed.
- Regression UI test: a real `DispatcherFrame` processed a `DispatcherPriority.Input` callback within the bounded five-second interval with 200 asset cards.
- Virtualization assertion: the first item was realized while item 199 remained unrealized.
- Post-change Graphify and CRG impact checks completed; CRG found no affected flows and reported risk 0.35.

## Open acceptance item

Manual native-WPF interaction could not be automated in the current Codex environment. A user must still verify mouse clicks, keyboard input, list scrolling, and search against a large real catalog. This review does not treat `Process.Responding` as evidence and does not declare the ticket ready before that verification.

## Files changed

- `src/ScanVault.App/App.xaml.cs`
- `src/ScanVault.App/LoadingWindow.xaml`
- `src/ScanVault.App/LoadingWindow.xaml.cs`
- `tests/ScanVault.App.Tests/MainWindowTests.cs`
- `Docs/architecture.md`
- `Tickets/MLV-19.md`
- `Task/MLV-19/investigation.md`
- `Task/MLV-19/implementation-plan.md`
- `Task/MLV-19/implementation-report.md`
