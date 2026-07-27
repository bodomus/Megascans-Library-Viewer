**# Diagnostics, About information, and index compatibility**

**## Status**

Ready for implementation after MLV-5.

**## Project**

****ScanVault — Megascans Library Viewer****

**## Summary**

ScanVault must expose the state of the running application and its local index clearly.

The application has several independent version concepts:

\- application version;

\- SQLite schema version;

\- metadata-normalization version;

\- index freshness;

\- last successful scan.

This ticket introduces a compact About/Diagnostics experience and explicit index compatibility handling.

**## Mandatory workflow**

This is a ****Level 2 structural task****.

Before implementation, Codex must:

1\. Resolve repository root.

2\. Read \`AGENTS.md\`.

3\. Read \`.codex/PRE_TICKET_WORKFLOW.md\`.

4\. Execute \`$graphify-repository-analysis\`.

5\. Execute \`$code-review-graph-analysis\`.

6\. Inspect MLV-5 versioning, SQLite schema handling, normalization markers, settings, logs, scan state, and UI command architecture.

7\. Validate graph findings against source.

8\. Create:

   - \`Task/MLV-6/investigation.md\`;

   - \`Task/MLV-6/implementation-plan.md\`.

9\. Implement only after the plan is complete.

10\. Update CRG after implementation.

11\. Inspect blast radius.

12\. Run validation.

13\. Refresh Graphify only if justified.

14\. Create \`Task/MLV-6/implementation-report.md\`.

Preserve unrelated user changes.

**## Goal**

Provide one place where the user can inspect and copy diagnostic information and understand whether the current index is:

\- compatible;

\- outdated but readable;

\- requiring migration;

\- requiring Rescan;

\- created by a newer unsupported application;

\- missing;

\- corrupted.

**## UI scope**

Add an ****About / Diagnostics**** window or dialog.

Recommended entry:

\`\`\`text

Help → About / Diagnostics

\`\`\`

If no menu exists, a toolbar button is acceptable.

Keep the UI functional and compact. Cosmetic redesign is out of scope.

**## Required diagnostic fields**

Display:

\`\`\`text

Application version

Informational version

Commit SHA

Build configuration

Runtime version

Operating system

Process architecture

Library root

Indexed asset count

Last successful scan

Last scan duration

Last scan result

SQLite database path

Thumbnail/cache path

Database schema version

Metadata normalization version

Index compatibility state

Rescan requirement

\`\`\`

Optional when already available safely:

\`\`\`text

Log directory

Settings file path

Current sort mode

Current selected folder

\`\`\`

Do not display secrets, tokens, unrelated environment variables, or broad machine inventory.

**## Copy diagnostics**

Add:

\`\`\`text

Copy diagnostics

\`\`\`

Generate stable, readable text suitable for a bug report.

Example:

\`\`\`text

ScanVault diagnostics

Application: 0.2.0

Informational: 0.2.0+abc1234

Runtime: .NET X.Y.Z

OS: Windows ...

Architecture: x64

Library root: J:\\...

Indexed assets: 1842

Last successful scan: 2026-07-27 18:30:12 +03:00

Database schema: 2

Normalization version: 2

Index state: Compatible

Database: C:\\Users\\...\\scanvault.db

Logs: C:\\Users\\...\\Logs

\`\`\`

Requirements:

\- stable ordering;

\- no raw stack traces;

\- no secrets;

\- consistent unavailable-value formatting;

\- graceful clipboard failure;

\- formatting implemented in a testable service, not code-behind.

**## Index compatibility model**

Create an explicit state, for example:

\`\`\`csharp

public enum IndexCompatibilityState

{

&nbsp;&nbsp;&nbsp;&nbsp;Compatible,

&nbsp;&nbsp;&nbsp;&nbsp;RequiresMigration,

&nbsp;&nbsp;&nbsp;&nbsp;RequiresRescan,

&nbsp;&nbsp;&nbsp;&nbsp;NewerVersionUnsupported,

&nbsp;&nbsp;&nbsp;&nbsp;Missing,

&nbsp;&nbsp;&nbsp;&nbsp;Corrupted

}

\`\`\`

Exact naming may differ.

Do not derive state by parsing UI messages.

**### Compatible**

\- schema supported;

\- normalization supported;

\- index readable;

\- no corrective action required.

**### RequiresMigration**

\- schema older;

\- supported migration path exists;

\- migration not yet completed.

**### RequiresRescan**

\- schema readable;

\- normalization version outdated;

\- rows may remain visible;

\- Rescan required for authoritative corrected metadata.

**### NewerVersionUnsupported**

\- index created by a newer unsupported format;

\- application must not write to it;

\- no destructive downgrade;

\- clear user message.

**### Missing**

\- no index exists;

\- normal initial state.

**### Corrupted**

\- index cannot be read or validated;

\- do not silently delete it;

\- show recovery guidance;

\- log technical details.

**## Startup behavior**

At startup:

\- determine compatibility before writes;

\- keep startup responsive;

\- avoid destructive recovery;

\- show concise main-window status when action is required;

\- expose full details in Diagnostics.

Examples:

\`\`\`text

Index metadata is outdated — Rescan required.

Index was created by a newer ScanVault version and cannot be opened safely.

Index could not be read. See Diagnostics.

\`\`\`

Avoid recurring large modal dialogs unless normal operation is blocked.

**## Write safety**

For \`NewerVersionUnsupported\` or \`Corrupted\`:

\- block unsafe writes;

\- disable actions that would overwrite data without an explicit safe path;

\- preserve the file;

\- provide documented recovery guidance.

Do not silently delete, rename, replace, or downgrade user data.

**## Scan metadata**

Persist or expose:

\`\`\`text

LastSuccessfulScanUtc

LastScanDuration

LastScanStatus

LastScanAddedCount

LastScanUpdatedCount

LastScanRemovedCount

LastScanSkippedCount

LastScanInaccessibleFolderCount

\`\`\`

Introduce the smallest coherent persistence change.

Do not couple scan-history persistence to UI code.

**## About information**

The dialog may include:

\`\`\`text

ScanVault

Megascans Library Viewer

Version ...

\`\`\`

Optional:

\- repository reference;

\- copyright;

\- license when defined.

Do not add network calls or update checking.

**## Architecture**

Recommended ownership:

\`\`\`text

Core

\- compatibility state

\- diagnostics model

\- formatter abstractions

Infrastructure

\- SQLite/version inspection

\- settings/cache/log path providers

\- persisted scan metadata

App

\- diagnostics view model

\- clipboard command

\- dialog

\`\`\`

Requirements:

\- no SQL in view models;

\- no filesystem probing in code-behind;

\- no runtime Git execution from UI;

\- no duplicated version literals;

\- complex compatibility decisions commented;

\- state transitions testable.

**## Logging**

Log:

\- compatibility state;

\- schema and normalization versions;

\- migration or Rescan requirement;

\- newer unsupported index;

\- corrupted index;

\- diagnostics-copy failure.

Do not emit full exception stacks at information level.

**## Tests**

Add tests for:

\- each compatibility state;

\- older supported schema;

\- outdated normalization;

\- newer unsupported schema;

\- missing index;

\- corrupted index;

\- write blocking;

\- diagnostics formatting;

\- missing optional fields;

\- stable field ordering;

\- absence of secret/unrelated environment data;

\- persisted scan metadata;

\- clipboard abstraction;

\- startup status messages.

Use temporary databases and directories only.

**## Manual validation checklist**

1\. Open Diagnostics on a compatible index.

2\. Verify application and commit version.

3\. Verify database/cache paths.

4\. Copy diagnostics.

5\. Validate RequiresRescan.

6\. Validate Missing.

7\. Simulate NewerVersionUnsupported.

8\. Confirm writes are blocked.

9\. Simulate Corrupted.

10\. Confirm no file is deleted.

11\. Confirm concise main-window status.

12\. Restart and verify scan metadata persists.

**## Acceptance criteria**

\- [ ] About/Diagnostics UI exists.

\- [ ] Application, runtime, OS, library, index, and scan information is shown.

\- [ ] Copy Diagnostics creates stable readable text.

\- [ ] Compatibility state is explicit.

\- [ ] All required states are handled.

\- [ ] Newer unsupported indexes are never written or downgraded.

\- [ ] Corrupted indexes are not silently deleted.

\- [ ] Startup shows concise action status.

\- [ ] Last-scan metadata persists or is reliably available.

\- [ ] Diagnostics logic is testable outside WPF.

\- [ ] Tests pass.

\- [ ] Release build succeeds.

\- [ ] CRG is updated and impact reviewed.

\- [ ] Graphify is refreshed only if justified.

\- [ ] Investigation, plan, and implementation report exist.

**## Explicit non-goals**

Do not implement:

\- automatic backup/restore;

\- automatic replacement;

\- cloud diagnostics upload;

\- telemetry;

\- crash-reporting service;

\- update checker;

\- GitHub API integration;

\- release workflow;

\- installer;

\- Unreal integration;

\- asset inventory;

\- visual restyling.

**## Required deliverables**

\`\`\`text

Task/MLV-6/investigation.md

Task/MLV-6/implementation-plan.md

Task/MLV-6/implementation-report.md

README.md

Docs/architecture.md

Docs/index-format.md

Docs/diagnostics.md

\`\`\`

**## Validation**

Run repository-supported equivalents of:

\`\`\`powershell

dotnet restore ScanVault.sln

dotnet build ScanVault.sln --configuration Release --no-restore

dotnet test ScanVault.sln --configuration Release --no-build

dotnet format ScanVault.sln --verify-no-changes --no-restore

git diff --check

\`\`\`

Do not claim interactive checks that were not executed.

**## Definition of done**

ScanVault exposes reliable diagnostics, clearly distinguishes application/schema/normalization/scan state, and prevents unsafe writes to incompatible or corrupted indexes.
