# MLV-13 Unreal Engine readiness

Source: https://bodomus.youtrack.cloud/issue/MLV-13/Unreal-Engine-readiness

## Goal

Add rule-based, read-only evaluation of whether indexed Megascans assets are ready for a typical manual Unreal Engine import. The feature must not launch Unreal Engine, create `.uasset` files, generate materials, convert assets, modify the library, read binary contents, or promise guaranteed import success.

## Statuses

- UE Ready
- UE Ready With Warnings
- Not UE Ready
- Not Applicable
- Unknown

Each result must include explainable reasons with stable rule code, severity, message, and related inventory item where possible.

Severity:

- Blocking
- Warning
- Information

Suggested rule codes:

- `UE_MISSING_MESH`
- `UE_MISSING_ALBEDO`
- `UE_MISSING_NORMAL`
- `UE_MISSING_ROUGHNESS`
- `UE_MISSING_DISPLACEMENT`
- `UE_MISSING_OPACITY`
- `UE_NO_LODS`
- `UE_INCOMPLETE_LOD_CHAIN`
- `UE_ONLY_BILLBOARD`
- `UE_AMBIGUOUS_INVENTORY`
- `UE_MIXED_RESOLUTIONS`
- `UE_UNSUPPORTED_FILE_FORMAT`
- `UE_UNKNOWN_ASSET_TYPE`
- `UE_DUPLICATE_LOGICAL_FILES`
- `UE_NO_PRIMARY_TEXTURE_SET`

## Semantics

`UE Ready` means the current index shows the minimum non-conflicting set of files for typical manual Unreal Engine import. It does not mean guaranteed import success, automatic `.uasset` creation, material readiness without setup, collision readiness, Nanite readiness, or readiness for a specific UE version.

## Minimum Rules

Codex must refine rules against actual project asset types, content inventory, issue codes, texture map normalization, mesh/LOD/variant model, and real asset examples where available.

### 3D Asset / 3D Plant

Blocking:

- Missing FBX or other supported mesh.
- Missing albedo/base color.
- Inventory is too ambiguous to determine primary set.
- Mesh classification unsupported.

Warnings:

- Missing LODs.
- Incomplete LOD chain.
- Missing normal.
- Missing roughness.
- Missing displacement.
- No billboard for vegetation.
- Mixed texture resolutions.
- Duplicate/ambiguous files.

### Surface / Material

Blocking:

- Missing albedo.
- Missing usable texture set.
- Primary texture set cannot be determined.

Warnings:

- Missing normal.
- Missing roughness.
- Missing displacement.
- Mixed resolutions.
- Ambiguous map type.

### Atlas

Blocking:

- Missing atlas/base-color texture.
- Missing opacity/alpha if required for the actual type.

Warnings:

- Missing normal.
- Missing roughness.
- Mixed resolutions.

### Billboard

Blocking:

- Missing billboard texture.
- Missing opacity/alpha.
- Missing base color.

Warnings:

- Missing normal.
- Unknown variant/orientation metadata.

### Decal

Blocking:

- Missing usable decal texture set.
- Missing required mask/opacity.
- Missing main color/normal set according to the actual model.

### Unknown Type

- Status: Unknown.
- Reason: Unsupported or unrecognized Asset Type.
- Unknown type must not receive Ready.

## Rule Engine

Rules must be explicit, deterministic, testable, locale-independent, and order-independent. Add `ReadinessRuleVersion`. When rules change, version must increase, old results must be invalidated, controlled recompute must occur, and diagnostics must show the state. Do not use reflection, hidden scores, or dynamic UI expressions.

## Persistence

Minimally persist:

- AssetId
- ReadinessStatus
- ReadinessRuleVersion
- BlockingCount
- WarningCount
- EvaluatedAtUtc

Use existing issue infrastructure for reasons if suitable. Do not create a second incompatible issue system. Recalculate after Rescan, rule version changes, migration affecting inventory, and normalization changes. Failed/partial scan must not leave inconsistent readiness.

## UI

Asset card badge:

- UE Ready
- Warnings
- Not Ready
- Unknown

Requirements: readable in light theme, tooltip with summary, color is not the only indicator.

Filters:

- UE Ready
- UE Ready With Warnings
- Not UE Ready
- Unknown
- Not Applicable
- Missing Mesh
- Missing Normal
- Missing LODs
- Blocking Issues
- Warnings

Use the common query pipeline.

Asset details:

- Status
- Blocking issues
- Warnings
- Rule version

Reasons should navigate to the related inventory item where possible.

Integrations:

- MLV-8 smart collections: UE Ready Assets, UE Ready With Warnings, Not UE Ready, Missing Mesh, Missing LODs.
- MLV-10 comparison: UE Readiness, Blocking count, Warning count, Readiness reasons.
- MLV-11 export: UnrealReadinessStatus, ReadinessRuleVersion, BlockingIssueCount, WarningCount, ReadinessReasons.
- MLV-9 history should record readiness changes through existing change model or a separate flag if architecturally justified.

Summary:

- UE Ready
- Ready with warnings
- Not ready
- Unknown
- Not applicable

Place in Diagnostics or another suitable area without overloading the main window.

## Performance

- Compute only from indexed data.
- Do not read binary file contents.
- Batch evaluation.
- Avoid N+1.
- Do not run on UI thread.
- Support cancellation.
- Log duration and counts.

Implementation report measurements:

- Asset count
- Evaluation duration
- Assets per second
- Ready count
- Warning count
- Not ready count
- Unknown count

## Diagnostics

Add:

- UE readiness rule version
- Last readiness evaluation
- Ready asset count
- Not ready asset count
- Unknown asset count
- Requires readiness recalculation

## Acceptance Criteria

- Readiness depends on Asset Type.
- Blocking and warnings are separated.
- Missing and Unknown are distinct.
- Rule codes are stable.
- Rule version is persisted.
- File order does not affect result.
- Unknown type does not receive Ready.
- Ambiguous inventory does not receive false Ready.
- Readiness persists after restart.
- Migration does not delete the index.
- Rule version changes invalidate old data.
- Badge and filters work.
- Details show reasons.
- Smart collections, comparison, and export use readiness.
- Library is not changed.
- Unreal Engine is not launched.
- GitHub Actions remains green.

## Tests

Unit: ready 3D asset, missing mesh, missing albedo, missing normal, no LOD warning, incomplete LOD chain, ready surface, ambiguous surface, atlas missing opacity, billboard missing alpha, unknown type, mixed resolutions, duplicate logical files, deterministic order, rule-version invalidation, summary counts.

Integration: migration, persistence, batch evaluation, filter query, smart collection integration, comparison integration, export integration, diagnostics, failed/partial scan, large synthetic dataset.

Manual: complete 3D asset, asset without FBX, asset without LOD, tooltip, filters, smart collection, comparison, export, Rescan/recompute, rule-version invalidation.
