# MLV-15 Implementation Plan

1. Save the attached ticket as `Tickets/MLV-15.md`.
2. Change `MainViewModel.UnrealImportPackageRequested` to pass `AssetSummary` instead of a prepared `UnrealImportPackageViewModel`.
3. Move UE package ViewModel creation/loading into `MainWindow.OnUnrealImportPackageRequested` and wrap preparation, window construction, `DataContext`, and `ShowDialog()` in a local error boundary.
4. Add structured application log events for UE package open failure and export failure.
5. Add a small internal helper to make the open/export UI boundaries testable without showing real `MessageBox` dialogs.
6. Change `UnrealImportPackageViewModel.ExportAsync` so unexpected export failures are not swallowed before the WPF boundary can report them.
7. In `UnrealImportPackageWindow.OnExportClick`, wrap export with the helper, show the requested warning message, keep the window open, and leave retry state usable.
8. Add regression tests for open-boundary and export-boundary handling.
9. Run focused App tests, then restore/build/test/format/diff checks.
10. Update CRG after changes and create implementation/review reports.
