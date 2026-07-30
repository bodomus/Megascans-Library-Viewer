# MLV-8 Implementation Plan

1. Add Core smart collection models, built-ins and matching/count policy that reuses AssetFiltering and AssetSorting semantics.
2. Add ISmartCollectionStore and JSON implementation with backup-on-corrupt load behavior and stable DefinitionVersion.
3. Wire store in DI and extend ScanVaultPaths with a smart collections path.
4. Extend MainViewModel with SmartCollections, active/modified state, create/apply/update/duplicate/delete/reorder/deactivate/reset commands and async count refresh.
5. Add a small WPF dialog for name/description/folder-scope/save-sort metadata and expose it from MainWindow.
6. Add Smart Collections UI above the physical folder tree with built-in/user sections, counts, active highlight, context actions and missing/unsupported status text.
7. Add tests for criteria matching, JSON persistence and VM active/modified behavior.
8. Run restore/build/tests, CRG update/status, diff review and create review/review-MLV-8.md.
