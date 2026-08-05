# MLV-8 Implementation Report

## Changed Files
- Core: smart collection models, definition matching/count policy, and `ISmartCollectionStore` contract.
- Infrastructure: `JsonSmartCollectionStore`, `ScanVaultPaths.SmartCollectionsPath`, DI registration.
- App: Smart Collections panel, create/edit dialog, MainViewModel active/modified/count/persistence workflow, structured logging.
- Tests: Core policy tests, JSON store tests, WPF smoke test update, MainViewModel test factory update.
- Docs/artifacts: `README.md`, `Tickets/MLV-8.md`, investigation, implementation plan, review file.

## Persistence Strategy
User collections are stored in a separate JSON document at `%AppData%/ScanVault/smart-collections.json`. This keeps definitions outside the SQLite asset index and outside the scanned Megascans library. Saves are atomic via a temporary file. Corrupted JSON is moved to `.corrupt-yyyyMMddHHmmss` and the app loads an empty user collection list instead of crashing.

## Schema And Migration
No SQLite migration was required because smart collections are global app preferences, not index rows tied to a specific `LibraryIdentity`. Existing three-argument `ScanVaultPaths` construction remains source-compatible for tests and callers.

## Definition Version
Definitions use `SmartCollectionDefinition.CurrentVersion = 1`. Unknown definition versions are treated as `UnsupportedDefinition` by policy and cannot be applied.

## Supported Criteria
Definition v1 supports search text, asset types, folder scope, FBX, ABC, LODs, variants, atlas, billboard, texture sets, issues, completeness statuses, min/max resolution, and optional sort mode. Current UI capture stores the criteria the current UI can express directly; built-ins use the wider definition model for negative criteria such as Missing Mesh.

## Folder Scope Behavior
- `EntireLibrary`: ignores current folder.
- `CurrentFolder`: evaluates against the folder selected when applied or counted.
- `SpecificFolder`: stores a normalized relative path inside the library root. Missing folders remain visible with `MissingFolder` status and do not silently switch to root.

## System Collections
Added read-only built-ins: All Assets, Complete Assets, Assets With Issues, Missing Mesh, Missing LODs, Atlas Assets, Billboard Assets. `Recently Added` was intentionally not added because the ticket forbids a fake timestamp-based version without a stable MLV-9 criterion.

## Active And Modified State
Applying a collection highlights it, filters cards from the stored definition, syncs visible UI fields where possible, and restores optional sort. Manual search/filter/folder/sort changes mark the active collection as modified. User collections can be updated, duplicated, deleted, reset, reordered, or deactivated back to manual mode.

## Count Strategy
Counts run asynchronously from the current in-memory asset snapshot using a batch pass over collection definitions. Count refresh is cancelled/restarted after folder, Rescan, and definition changes. Counts do not write to the index and do not touch library files.

## Performance Measurements
Validation used the automated test fixture sizes. Count work is off the UI thread and cancellable. Large-library interactive timing still needs a manual pass on a real Megascans library before release notes claim specific durations.

## Known Limitations
- No complex AND/OR builder; MLV-8 is AND-only.
- Edit dialog changes metadata/folder scope/sort; condition edits are done through current filters plus Update collection.
- Context-menu actions are represented as panel buttons in this pass.
- Diagnostics does not yet list smart collection counts/version details.

## Validation
- `dotnet restore ScanVault.sln`: passed.
- `dotnet build ScanVault.sln --configuration Release --no-restore`: passed.
- `dotnet test ScanVault.sln --configuration Release --no-build`: passed, Core 47 / Infrastructure 34 / App 22.
- `git diff --check`: passed with line-ending warnings only.
- CRG update/status: 617 nodes, 1432 edges, risk 0.60; remaining test gaps are generated logging partial methods.
- Graphify query was attempted but blocked by approval review because it could send repository context externally.

## Blast Radius
The change touches navigation UI, MainViewModel state, smart collection persistence, and shared filtering policy. It does not change SQLite schema, scan parsing, asset indexing, source-library files, or image cache behavior.
