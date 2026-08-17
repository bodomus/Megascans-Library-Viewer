# MLV-14 Investigation

## Baseline

- Branch: `master`
- Start commit: `d0de9a0bd9b83cfe191bff16ff74c183add17952`
- Initial worktree: clean except generated ticket artifact after preflight.
- YouTrack state was updated to `In Progress`.

## Ticket Summary

MLV-14 adds a read-only ScanVault feature that creates a deterministic, versioned JSON manifest for a selected Megascans asset. ScanVault owns analysis, semantic mapping, validation, preview, and export. Unreal Engine import, `.uasset` creation, material instance creation, Nanite setup, texture settings, and all UE APIs are out of scope.

## Source Findings

- `AssetSummary` already carries normalized asset identity, physical paths, `AssetContentInventory`, and persisted `UnrealReadinessEvaluation`.
- `AssetContentInventory` already models mesh variants, LOD entries, texture sets, normalized `TextureMapType`, texture-set kind, resolution, format, completeness, and inventory issues.
- `UnrealReadinessPolicy` has rule version `1`, freshness checks, deterministic reason ordering, and primary texture-set selection logic that MLV-14 should share rather than duplicate.
- Report export already uses explicit writer services and atomic temporary-file publication; MLV-14 should use a separate package export service because package semantics differ from report rows.
- Settings are persisted in JSON outside the index. Smart collections provide the closest pattern for separate user configuration persistence with corrupt-file backup.
- Main WPF actions use top-level buttons, asset card context menus, ViewModel events, and dedicated windows.
- MLV-12 duplicate handling means package generation must preserve physical source identity. The manifest includes asset ID, JSON path, and asset folder path so a future UE consumer does not resolve only by ID.

## Code Intelligence

- CRG preflight: `code-review-graph update --brief` succeeded before implementation.
- Graphify preflight query found the relevant nodes: `AssetSummary`, `AssetContentInventory`, `ExportReportViewModel`, `SettingsViewModel`, and `RelayCommand`.
- Source inspection validated graph hints before implementation.

## Risks

- MLV-14 is the first version of the manifest contract. The schema is versioned, but UE57Editor has not consumed it yet.
- ScanVault validates path syntax and file existence only. It does not validate that a configured Unreal Master Material exists.
- User material profile editing is intentionally simple and profile-focused, not a full design-system editor.
