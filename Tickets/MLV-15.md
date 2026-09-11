# MLV-15 - Prevent crash when opening UE Import Package

## Summary

Fix a crash-level ScanVault bug: choosing the asset context command `Create UE Import Package` can close the whole application without an error message.

Required behavior:

- ScanVault must not terminate because opening UE Import Package failed.
- The user must receive a clear error message.
- Technical details and the stack trace must be logged.
- The UE Import Package window must remain stable when export fails.
- The original exception cause must remain diagnosable after reproduction.

## Context

Current flow:

```text
Asset context menu
    -> Create UE Import Package
    -> MainViewModel.CreateUnrealImportPackageCommand
    -> RequestUnrealImportPackage(...)
    -> UnrealImportPackageRequested
    -> MainWindow.OnUnrealImportPackageRequested(...)
    -> new UnrealImportPackageWindow
    -> ShowDialog()
```

The open handler is not protected by a local `try/catch`.

Other windows, such as Scan History, Export Report, Duplicate Detection, and Diagnostics, already wrap opening in `try/catch` with a `MessageBox`.

`UnrealImportPackageWindow.OnExportClick` is `async void` and calls `await viewModel.ExportAsync(...)` without local exception handling.

## Mandatory Workflow

Before changing code:

1. Read `AGENTS.md`.
2. Read `.codex/PRE_TICKET_WORKFLOW.md`.
3. Check the current flow through `MainViewModel`, `MainWindow.xaml.cs`, `UnrealImportPackageWindow.xaml.cs`, `UnrealImportPackageViewModel`, and existing application logging.
4. Use CRG/Graphify according to the standard pre-ticket workflow.
5. Create `Task/MLV-15/investigation.md` and `Task/MLV-15/implementation-plan.md`.

After implementation:

```text
Task/MLV-15/implementation-report.md
```

## Goals

1. Exceptions during UE Import Package preparation/open must not terminate ScanVault.
2. Exceptions during `ShowDialog()` must be caught.
3. Exceptions during `ExportAsync()` must not terminate the application.
4. The user must see a short error message.
5. The full exception must be logged.
6. After an error, the main ScanVault window must remain usable.
7. Do not hide the original exception cause.
8. Do not expand scope by changing MLV-14 package semantics.

## Required Changes

### Protect Package Creation/Open Flow

Wrap the UE Import Package open flow in a local `try/catch`, preferably in `MainWindow.OnUnrealImportPackageRequested(...)` or above if investigation shows that the exception can occur before the event handler.

At minimum cover ViewModel construction, package preparation, `UnrealImportPackageWindow` construction, `InitializeComponent`, `DataContext` assignment, and `ShowDialog`.

### User-Facing Open Error

Show:

```text
UE Import Package could not be opened.

<exception.Message>

See the application log for technical details.
```

Title: `ScanVault UE import package`

Icon: `Warning`

Do not show a large stack trace in the MessageBox.

### Structured Logging

Log the full exception with the exception object, not just `exception.Message`.

### Protect ExportAsync

In `UnrealImportPackageWindow.OnExportClick(...)`, wrap `await viewModel.ExportAsync(...)` in `try/catch`.

On failure:

- do not close the window;
- do not terminate the application;
- show a MessageBox;
- log the exception;
- preserve the ability to retry export after fixing destination/settings.

User message:

```text
UE Import Package could not be exported.

<exception.Message>

See the application log for technical details.
```

## Diagnostics Requirement

Do not turn the ticket into only “swallow exception”. Reproduction must still give the real reason.

The implementation report must record original exception type, message, stack trace location, and root cause if reproduced. If not reproduced, state that explicitly.

## Error Boundaries

Handle exceptions locally at UI boundaries:

- open package window;
- export package.

Do not add empty catches. Do not convert internal Core errors into silent success.

## Tests

Open flow:

1. normal flow;
2. ViewModel/package preparation throws;
3. window/open boundary exception is handled;
4. application state remains usable;
5. error logged;
6. user error presentation invoked.

Export flow:

1. successful export;
2. `ExportAsync()` throws;
3. exception does not leave the WPF event handler;
4. error message is shown;
5. logger receives exception;
6. window remains open;
7. UI state after failure is correct.

## Manual Validation

1. Run ScanVault.
2. Open an asset context menu.
3. Click `Create UE Import Package`.
4. If the original bug reproduces, ScanVault must remain open, show MessageBox, and log exception plus stack trace.
5. Fix root cause if local and in MLV-15 scope.
6. Repeat.
7. Verify successful package window open.
8. Trigger a safe export failure.
9. Verify the app does not close.
10. Verify ScanVault remains usable.

## Acceptance Criteria

- [ ] `Create UE Import Package` no longer terminates ScanVault due to an unhandled exception.
- [ ] Open errors are displayed through MessageBox.
- [ ] Full exception is logged.
- [ ] `ExportAsync()` errors do not terminate the application.
- [ ] Export error is shown to the user.
- [ ] UE Import Package window remains open after export failure.
- [ ] Main window remains usable after open failure.
- [ ] Existing successful package flow is not broken.
- [ ] MLV-14 manifest semantics are unchanged.
- [ ] Tests passed.
- [ ] `git diff --check` passed.

## Non-Goals

Do not include:

- UE Import Package UI redesign;
- manifest schema changes;
- readiness changes;
- material profile changes;
- export contract changes;
- UE57Editor changes;
- batch export;
- global error-handling rewrite.

## Deliverables

```text
Task/MLV-15/investigation.md
Task/MLV-15/implementation-plan.md
Task/MLV-15/implementation-report.md
```

Update the review file if the project uses per-ticket reviews.
