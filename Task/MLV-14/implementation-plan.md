# MLV-14 Implementation Plan

## 1. Domain Contract

- Add explicit Core models for `UnrealImportPackage`, source identity, destination, mesh/LOD, textures, material profile snapshots, options, validation issues, settings, and export result.
- Add `UnrealImportPackageSchema.CurrentVersion = 1`.
- Keep the model strongly typed and JSON-oriented without WPF types.

## 2. Core Policies

- Extract shared asset content selection policy from readiness primary-set logic.
- Add semantic texture role mapping from `TextureMapType`.
- Add deterministic Unreal-safe name and destination path policies.
- Add built-in material profiles, compatibility filtering, duplication, and profile validation.
- Add package generation, validation, and deterministic package identity computation.

## 3. Infrastructure

- Persist user material profiles in a separate JSON document outside the asset index.
- Extend `ScanVaultPaths` with a profile path while preserving existing constructor compatibility.
- Add package serialization/export service using explicit JSON options and atomic temp-file publication.
- Validate source existence during export.

## 4. App/UI

- Add selected-asset command and asset context-menu entry: `Create UE Import Package`.
- Add a dedicated WPF preview/configuration window.
- Add ViewModel support for destination base path, profile selection, profile duplicate/save/delete, declarative options, validation rows, raw JSON preview, copy manifest, and export.
- Persist default destination base path, last manifest export folder, and default profile by asset type in settings.

## 5. Tests

- Add Core tests for manifest shape, readiness blocking, role mapping, sanitization, profile compatibility, variant/LOD preservation, and package identity.
- Add Infrastructure tests for profile persistence, malformed profile storage, atomic export, source existence validation, UTF-8 JSON, and roundtrip schema.
- Add App tests for command availability, validation blocking, preview refresh, copy serializer usage, export service calls, settings persistence, and profile CRUD state.
- Add test-method classification comments before each new or edited test method.

## 6. Documentation and Validation

- Add manifest contract documentation.
- Run restore/build/test/format/diff checks.
- Run post-change CRG impact inspection and attempt Graphify refresh.
- Write implementation and review reports.

## Review Fix Scope

- Extend `packageId` so it tracks the material contract, selected texture/LOD semantic fields, and the indexed source revision marker while still excluding generation time.
- Detect duplicate semantic texture-role candidates before deterministic candidate collapse, preserve ambiguity warnings through export revalidation, and document the selection priority.
- Complete the material profile editor with new profile creation, compatible asset-type toggles, editable mappings, editable default options, validation, and user-profile persistence coverage.
- Keep built-in profiles immutable and ensure unsaved profile edits refresh the preview/package identity without persisting until `Save user`.

## Final Fix Scope

- Add material-profile compatible asset types to the manifest snapshot and include the sorted compatibility set in `packageId`.
- Add `IncompatibleMaterialProfile` validation so packages cannot export when the selected profile does not support the current asset type.
- Treat `TextureParameterMappings` as active mappings only: editor rows keep separate enabled state, disabled roles remain absent during preview/save/export, and optional texture warnings only apply to active mappings.
- Add Core, Infrastructure, and App regression coverage for profile compatibility, active/absent mapping semantics, persistence, and preview/export gating.
