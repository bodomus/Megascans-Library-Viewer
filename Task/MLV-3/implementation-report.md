# MLV-3 — Implementation report

## Result

The WPF card template now binds `Popup.IsOpen` to the ViewModel in the correct source-to-target direction:

```xml
<Popup IsOpen="{Binding IsHoverOpen, Mode=OneWay}">
```

`AssetCardViewModel.IsHoverOpen` remains privately writable. The existing `BeginHoverAsync()` and `EndHover()` methods remain the only owners of hover state.

## Regression coverage

Added `tests/ScanVault.App.Tests/MainWindowTests.cs`. The STA test creates the real `App` and `MainWindow`, supplies a non-empty asset collection, performs WPF layout, and verifies that the first virtualized card container is realized.

Before the production change the test failed with the reported exception:

```text
System.Windows.Markup.XamlParseException:
A TwoWay or OneWayToSource binding cannot work on the read-only property
'IsHoverOpen' of type 'ScanVault.App.ViewModels.AssetCardViewModel'.
```

The stack passed through `VirtualizingWrapPanel.RealizeRange`, confirming that the panel only exposed the invalid template binding and was not the defect source. After adding `Mode=OneWay`, the same test passed.

## Validation

Executed from the repository root:

```powershell
dotnet test tests\ScanVault.App.Tests\ScanVault.App.Tests.csproj --configuration Debug --no-restore -m:1 /nodeReuse:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~MainWindowTests" --logger "console;verbosity=normal"
dotnet restore ScanVault.sln --disable-parallel -m:1 /nodeReuse:false
dotnet build ScanVault.sln --configuration Release --no-restore -m:1 /nodeReuse:false /p:UseSharedCompilation=false --verbosity:minimal
dotnet test ScanVault.sln --configuration Release --no-build -m:1 /nodeReuse:false /p:UseSharedCompilation=false --logger "console;verbosity=minimal"
```

Results:

- targeted regression: 1/1 passed after the fix;
- Release build: succeeded, 0 warnings, 0 errors;
- full tests: 25/25 passed (Core 8, Infrastructure 13, App 4);
- Release EXE: reached input-idle and remained running through the startup observation window.

## Post-change impact

The final CRG refresh indexed 294 nodes, 566 edges, and 50 C# files. It reported 0 affected flows and a heuristic risk score of 0.30. Its six "test gaps" refer only to the newly added test and helper/model declarations (`MainWindowTests`, the regression method, `FindVisualChild`, `AssetSummary`, and `NullImageLoader`), not to uncovered production flows; the regression method itself was executed successfully. XAML bindings are outside CRG's C# index, so the exact behavior was validated by source inspection and the real WPF template test.

Graphify was not rebuilt because no architecture, project boundary, or subsystem relationship changed.

## Scope and data safety

- `VirtualizingWrapPanel` was not changed.
- Core, Infrastructure, SQLite schema, scanner, parser, and image loading were not changed.
- User source assets were not read, modified, moved, or deleted by the fix.
- The untracked test data directory `tests/Megascan/` was preserved unchanged and excluded from the commit.
