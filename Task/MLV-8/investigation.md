# MLV-8 Investigation

## Preflight
- Branch: codex/MLV-8-smart-collections from master.
- YouTrack MLV-8 loaded and moved to In Progress.
- CRG status rebuilt for this branch at e0014f56410e, 583 nodes / 1301 edges / 82 files.
- Graphify query was attempted but blocked by approval review because it could send repository context to an external model/service. Proceeding with CRG and direct source validation.

## Current filtering pipeline
- MainViewModel owns UI state: SearchText, SelectedFolderPath, SortMode, InventoryFilter.
- RefreshVisibleAssets filters the in-memory allAssets list, then applies AssetSorting.Apply.
- AssetFiltering centralizes folder, search and inventory flag matching.
- AssetSorting centralizes sort modes.

## UI state and navigation
- MainWindow header contains search, sort, status and inventory checkboxes.
- Left panel is the physical folder TreeView bound to Folders.
- Folder selection is absolute path based; settings store LibraryRoot.
- Scan History exists in this branch, but MLV-8 must not add Recently Added unless the collection can be backed by a stable scan-history criterion. This pass keeps built-ins to the explicit non-history set.

## Persistence decision
- Use a separate JSON smart collection store under the current ScanVault app data paths.
- This avoids SQLite schema churn for definitions that are global app preferences, keeps library index immutable, and allows corrupted/unsupported collection definitions to be isolated.
- Definitions use explicit DefinitionVersion and app-level stable IDs, never WPF view models.

## Blast radius
- Core models/policies for collection definitions and matching.
- Infrastructure JSON store and DI registration.
- MainViewModel commands/state/count refresh and MainWindow left panel UI.
- Dialog/view models for create/edit collection metadata.
- Focused tests for policy/store/viewmodel behavior.
