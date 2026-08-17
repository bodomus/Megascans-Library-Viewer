# MLV-14 Implementation Report

## Implemented

- Added a versioned UE import manifest contract in Core with schema version `1`.
- Added deterministic package generation from `AssetSummary`, `AssetContentInventory`, and persisted MLV-13 readiness.
- Added semantic texture roles separate from raw `TextureMapType`; `Gloss` maps to `Roughness` while preserving `mapType = Gloss`.
- Added destination path and Unreal-safe name policies for folder segments, asset base names, and Material Instance names.
- Added reusable material profile models, built-in profile templates, compatibility checks, duplicate/save/delete support for user profiles, and profile validation.
- Added deterministic package identity using schema version, physical source identity, selected profile, destination, selected variant/LODs, textures, and options. `generatedAtUtc` is excluded.
- Added package validation for stale/blocked readiness, source identity, destination path, missing mesh, missing BaseColor, missing source paths, Master Material path syntax, optional map warnings, and readiness warnings.
- Added separate Infrastructure services for user material profile persistence and atomic manifest JSON export.
- Added WPF flow from selected asset and asset context menu into a dedicated package preview/export window.
- Added copy manifest support using the same serializer service used for export.
- Added user-facing technical documentation in `Docs/unreal-import-package.md`.
- Addressed review findings by expanding package identity to include material contract fields, selected texture/LOD semantics, and indexed source revision.
- Moved ambiguous texture-role detection before deterministic candidate collapse and preserved those warnings through export validation.
- Completed the material profile editor for user profiles: new profile creation, compatible asset types, editable mappings, editable default options, validation, and persistence tests.

## Validation

- `dotnet restore ScanVault.sln -v:minimal` passed after sandbox escalation.
- `dotnet build ScanVault.sln --configuration Release --no-restore -m:1 -v:minimal` passed.
- `dotnet test ScanVault.sln --configuration Release --no-build -m:1 -v:minimal` passed:
  - Core: 90 tests
  - Infrastructure: 65 tests
  - App: 41 tests
- `dotnet format ScanVault.sln --verify-no-changes --no-restore` passed after sandbox escalation for build-host named pipe access.
- `git diff --check` passed. Git reported line-ending normalization warnings only.
- `code-review-graph update --brief` passed after implementation.
- `code-review-graph detect-changes --base HEAD --brief` passed; CRG reported remaining gaps around small UI/command wrapper surfaces.
- `graphify update` was attempted and failed with `[WinError 5] Access is denied`.

## Boundaries

- ScanVault does not launch Unreal Engine.
- ScanVault does not call Unreal APIs.
- ScanVault does not create `.uasset` files.
- ScanVault does not create Material Instances, import textures/meshes, configure Nanite, configure LODs in UE, or mutate Megascans source files.

## Known Limitations

- Built-in Master Material paths are editable templates. ScanVault validates only `/Game/...` syntax, not asset existence.
- Built-in profiles are intentionally immutable in the UI; user profiles can be created, edited, saved, and deleted.
- Manual WPF smoke testing was not performed in this automated run.
- GitHub Actions was not verified because no push was requested/performed.
