# review-MLV-8

## Result
Implemented saved filters and dynamic smart collections for ScanVault on branch `codex/MLV-8-smart-collections`.

## Work Done
- Added stable smart collection definitions with `DefinitionVersion`, folder scopes, compatibility states and built-in read-only collections.
- Added reusable matching/count policy over the current in-memory index, reusing existing asset search/folder/inventory semantics instead of duplicating card filtering logic.
- Added JSON persistence at `%APPDATA%/ScanVault/smart-collections.json`, with atomic save and corrupt-file backup.
- Added Smart Collections UI above physical folders: built-ins, user collections, counts, active/modified highlight and actions for save/apply/edit/update/duplicate/delete/reorder/reset/manual.
- Added create/edit dialog with name, description, folder scope, save-sort option and criteria summary.
- Counts refresh asynchronously and are invalidated after collection changes, folder changes and Rescan.
- Added structured logging events for load/count/apply/create/update/duplicate/delete operations.
- Added focused Core and Infrastructure tests and updated WPF smoke tests for the new left panel.

## Notes
- `Recently Added` was not added because MLV-8 requires it only when MLV-9 scan history can be used as a stable collection criterion. This branch keeps the safe built-in set from the ticket.
- Specific saved folders store relative paths. Missing folders remain visible via compatibility warning and applying them yields an empty result instead of silently switching to root.
- CRG Graphify query was attempted during preflight but blocked by approval review because it could send repository context externally; CRG and direct source validation were used.

## Validation
- `dotnet restore ScanVault.sln` passed.
- `dotnet build ScanVault.sln --configuration Release --no-restore` passed.
- `dotnet test ScanVault.sln --configuration Release --no-build` passed: Core 47, Infrastructure 34, App 22.
- `git diff --check` passed; only line-ending normalization warnings from Git.
- `code-review-graph update --brief` completed with risk score 0.60; remaining test gaps are generated logging partial methods.
- `code-review-graph status`: 617 nodes, 1432 edges, branch `codex/MLV-8-smart-collections`.
