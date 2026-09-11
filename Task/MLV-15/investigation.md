# MLV-15 Investigation

## Workflow

- Repository root: `J:/Projects/UE_Projects/Megascans Library Viewer`.
- Branch: `master`.
- Initial commit: `f38ccece7d935a6b46bb2f5b37c3fe76ea5e5eac`.
- Initial working tree: clean.
- Level: Level 2 bugfix because this touches WPF `async void` UI boundaries and requires regression coverage.

## Graphify

- `graphify --help` succeeded.
- Existing `graphify-out/graph.json` was present.
- Focused query: `graphify query "MLV-15 UE Import Package open window ShowDialog ExportAsync error handling MainWindow UnrealImportPackageWindow UnrealImportPackageViewModel logging tests" --budget 4000`.
- Result was low-signal/stale for the new MLV-14/MLV-15 surface, so source inspection is authoritative.

## CRG

- `code-review-graph --help` succeeded.
- `code-review-graph status` showed 1264 nodes, 3608 edges, 149 files, built on `master` at commit `2deb6c382809`.
- `code-review-graph update --base 2deb6c382809 --brief` refreshed 13 changed files but ended with a console `UnicodeEncodeError` while printing the rich panel. The useful pre-error output reported 13 files updated and risk around `UnrealImportPackageViewModel` and related MLV-14 types.

## Source Findings

- `MainViewModel.CreateUnrealImportPackageCommand` calls `RequestUnrealImportPackage`.
- Before the fix, `RequestUnrealImportPackage` was `async void`, created and loaded `UnrealImportPackageViewModel`, then raised `UnrealImportPackageRequested`.
- That caller caught preparation exceptions and logged `AssetActionFailed`, but only updated status text; no user `MessageBox` was shown.
- `MainWindow.OnUnrealImportPackageRequested` constructed `UnrealImportPackageWindow`, assigned `Owner` and `DataContext`, and called `ShowDialog()` without a local `try/catch`.
- `UnrealImportPackageWindow.OnExportClick` was `async void` and awaited `viewModel.ExportAsync(CancellationToken.None)` without a local `try/catch`.
- `UnrealImportPackageViewModel.ExportAsync` caught general exceptions internally, logged them as validation/export failures, and updated status text. This prevented a crash for export-service errors but also prevented the window boundary from showing the requested `MessageBox`.

## Constraints

- Keep MLV-14 manifest/package semantics unchanged.
- Keep Core independent of WPF.
- Do not add global exception swallowing.
- Handle only the two UI boundaries: open package window and export package.

## Reproduction

The crash was reproduced after the initial defensive boundary work.

- Original exception type: `System.InvalidOperationException`.
- Original exception message: `A TwoWay or OneWayToSource binding cannot work on the read-only property 'AssetType' of type 'ScanVault.App.ViewModels.UnrealImportPackageViewModel'.`
- Original stack trace location: WPF binding/layout path beginning at `MS.Internal.Data.PropertyPathWorker.CheckReadOnly`, then `BindingExpression.AttachToContext`, `ContextLayoutManager.UpdateLayout`, and `HwndTarget.OnResize`.
- Root cause: `UnrealImportPackageWindow.xaml` used `<Run Text="{Binding AssetType}" />`. `Run.Text` does not default like the normal `TextBlock.Text` display binding in this context and attempted a source update against the read-only `UnrealImportPackageViewModel.AssetType` property during layout.

The fixed XAML makes the read-only `Run.Text` display bindings explicit `Mode=OneWay`. `JsonPreview` is also explicitly `Mode=OneWay` because it is displayed in a read-only `TextBox`.
