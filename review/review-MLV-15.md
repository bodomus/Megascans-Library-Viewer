# Review MLV-15

## Completed Work

Implemented defensive error handling for UE Import Package open/export boundaries:

- open failures now show `UE Import Package could not be opened` and log the full exception;
- export failures now show `UE Import Package could not be exported`, keep the window open, log the full exception, and preserve retry state;
- package ViewModel creation moved out of the `async void` command path and into the `MainWindow` UI boundary;
- fixed the reproduced WPF binding crash by making read-only `Run.Text` bindings, including `AssetType`, explicit `Mode=OneWay`;
- MLV-14 package/manifest semantics were not changed;
- regression tests cover open and export boundary failures without real `MessageBox` dialogs.

## Validation

- Focused MLV-15 regression tests: passed, 3/3.
- Solution restore through SDK `10.0.400` MSBuild: passed.
- Solution build through SDK `10.0.400` MSBuild with isolated output: passed.
- Core tests: passed, 98/98.
- Infrastructure tests: passed, 65/65.
- App tests: 45/46 passed; existing `ApplicationBuildInfoTests.FromAssemblyReadsGeneratedBuildMetadata` expects `0.2.0` while current `Directory.Build.props` emits `0.5.0`.
- Changed-file whitespace format check: passed.
- `git diff --check`: passed with CRLF normalization warnings only.
- CRG update and detect-changes: passed after setting `PYTHONIOENCODING=utf-8`.

## Notes

The reproduced root cause was `Run.Text="{Binding AssetType}"` attempting a source update into read-only `UnrealImportPackageViewModel.AssetType` during WPF layout. The new UI test realizes `UnrealImportPackageWindow` with a read-only `AssetType` context to guard that path.
