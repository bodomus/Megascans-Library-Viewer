# Review MLV-3

## Ticket

- ID: MLV-3
- URL: https://bodomus.youtrack.cloud/issue/MLV-3
- Type: Bug
- Priority: Critical
- Subsystem: Root

## Completed work

- Reproduced the reported `XamlParseException` with an automated WPF regression test that realizes the actual asset-card `DataTemplate`.
- Confirmed that `VirtualizingWrapPanel.RealizeRange` is the point at which WPF applies the invalid template, not the source of the defect.
- Changed `Popup.IsOpen` to `{Binding IsHoverOpen, Mode=OneWay}`.
- Kept `IsHoverOpen` read-only to consumers; hover state remains controlled by the ViewModel.
- Added `MainWindowTests.RealizesAssetCardWithViewModelOwnedHoverState`.

## Files

- `src/ScanVault.App/MainWindow.xaml`
- `tests/ScanVault.App.Tests/MainWindowTests.cs`
- `Tickets/MLV-3.md`
- `Task/MLV-3/investigation.md`
- `Task/MLV-3/implementation-plan.md`
- `Task/MLV-3/implementation-report.md`
- `review/review-MLV-3.md`

## Verification

- Pre-fix regression: failed with the exact reported read-only `IsHoverOpen` binding exception.
- Post-fix targeted regression: passed, 1/1.
- `dotnet restore ScanVault.sln`: succeeded.
- Release build: succeeded with 0 warnings and 0 errors.
- Release test suite: passed, 25/25.
- Release EXE startup smoke: reached input-idle and remained alive during the observation window.
- Final CRG: 294 nodes, 566 edges, 50 C# files, 0 affected flows, risk 0.30; six heuristic gaps point only to the new test/helper declarations, while the regression test itself passed.

## Risks and limitations

- The regression validates real WPF template creation and virtualization on an STA thread; pixel-level popup positioning was not automated.
- CRG indexes C# but not the changed XAML binding, so XAML correctness is established by the regression test and runtime startup smoke.

## Data and repository safety

No Megascans assets were modified. The user-provided `tests/Megascan/` directory remains unchanged and is intentionally excluded from the commit.
