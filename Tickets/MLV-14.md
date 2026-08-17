# MLV-14 — Unreal Engine Import Package / Manifest

## Context

Repository:

`https://github.com/bodomus/Megascans-Library-Viewer`

Project:

`Megascans Library Viewer / ScanVault`

YouTrack:

`MLV-14 — Unreal Engine Import Package`

This ticket starts the next architectural stage after MLV-13 Unreal Engine Readiness.

The intended architecture is:

```text
ScanVault
   |
   | MLV-13: analyze whether an asset is suitable for Unreal
   |
   +--> UE Ready / UE Ready With Warnings / Not Ready / Unknown
   |
   | MLV-14: describe what should be imported
   v
Versioned UE Import Package / Manifest
   |
   v
UE57Editor plugin
   |
   +--> import mesh
   +--> import textures
   +--> configure texture settings
   +--> create Material Instance
   +--> assign Master Material
   +--> assign texture parameters
   +--> configure LODs
   +--> configure Nanite
   +--> configure Virtual Textures / future UE-specific features
```

## Architectural Boundary

This boundary is mandatory:

### ScanVault owns

- analysis of the Megascans library;
- normalized asset metadata;
- content inventory;
- MLV-13 Unreal readiness;
- mapping indexed files to semantic import roles;
- generation and validation of a versioned import manifest;
- user-facing preview of the package;
- export of the package.

### ScanVault must NOT

- launch Unreal Engine;
- call Unreal APIs;
- create `.uasset` files;
- import FBX/ABC/textures into Unreal;
- modify Content Browser;
- create Materials or Material Instances;
- change Nanite settings;
- change texture compression/sRGB/virtual-texture settings in Unreal;
- directly inspect or mutate UE assets;
- modify/delete/rename any Megascans source file.

### UE57Editor owns later

- consuming the manifest;
- resolving `/Game/...` assets;
- actual Unreal import;
- asset factories/import tasks;
- Master Material loading;
- Material Instance creation;
- texture parameter assignment;
- Nanite configuration;
- LOD configuration;
- Unreal-specific texture settings;
- Virtual Texturing;
- collision;
- reimport/update policies;
- editor transactions and undo;
- all future UE-version-specific behavior.

MLV-14 must define a clean contract for that future consumer.

---

# Goal

Add a read-only ScanVault feature:

```text
Create UE Import Package
```

The command produces a deterministic, versioned JSON manifest describing how a selected Megascans asset should be imported into Unreal Engine.

The manifest must contain enough semantic information for the future UE57Editor plugin to perform the import without re-parsing Megascans filenames.

The manifest should be human-readable, testable, forward-compatible, and safe to store in source control.

MLV-14 does not perform the actual Unreal Engine import.

---

# Relationship to MLV-13

MLV-13 answers:

```text
Can this asset be imported safely enough for a typical manual Unreal workflow?
```

MLV-14 answers:

```text
What exactly should the Unreal importer import and how should the semantic pieces be connected?
```

The package generator must consume the existing normalized `AssetSummary`, content inventory, and `UnrealReadinessEvaluation`.

Do not introduce a second independent content analyzer.

MLV-14 should reuse:

- `AssetSummary`
- normalized asset type
- `AssetContentInventory`
- mesh variants / LOD entries
- texture sets
- normalized `TextureMapType`
- completeness
- MLV-13 readiness status/reasons
- existing paths and metadata

---

# Availability Rules

The `Create UE Import Package` command must have explicit eligibility semantics.

Recommended policy:

## Allowed by default

- `UE Ready`
- `UE Ready With Warnings`

## Blocked by default

- `Not UE Ready`
- `Unknown`
- `Not Applicable`

For a blocked asset, show a clear explanation using existing MLV-13 reasons.

Do not silently generate an apparently valid package from an asset that is classified `NotReady` or `Unknown`.

If there is a strong product reason to allow an override, implement it only as an explicit advanced action such as:

```text
Create package anyway
```

with a visible warning and a manifest flag indicating that readiness requirements were overridden.

Do not add an override unless it fits cleanly into the existing UI. A safe initial implementation may simply block generation.

---

# Core Model

Add a Core model for an Unreal import package.

Suggested shape:

```csharp
UnrealImportPackage
UnrealImportPackageSource
UnrealImportDestination
UnrealImportMesh
UnrealImportMeshLod
UnrealImportTexture
UnrealImportMaterialProfile
UnrealImportOptions
UnrealImportPackageValidation
UnrealImportPackageIssue
```

Names may be refined, but keep the model explicit and strongly typed.

Do not use loose dictionaries for the primary domain model.

---

# Manifest Schema

The package must serialize to JSON.

Use an explicit schema version:

```json
{
  "schemaVersion": 1
}
```

Do not infer schema version from application version.

The schema version belongs to the import-package contract.

Add a constant in Core, for example:

```csharp
UnrealImportPackageSchema.CurrentVersion
```

or equivalent.

The format must be deterministic and locale-independent.

---

# Proposed Manifest

The exact JSON may be refined, but it should preserve the following semantics.

```json
{
  "schemaVersion": 1,

  "generator": {
    "application": "ScanVault",
    "applicationVersion": "1.2.3",
    "commitSha": "abcdef1",
    "generatedAtUtc": "2026-08-17T07:00:00Z"
  },

  "source": {
    "assetId": "qwerty123",
    "name": "Forest Rock",
    "assetType": "3D Asset",
    "jsonPath": "J:/Megascans/ForestRock/ForestRock.json",
    "assetFolderPath": "J:/Megascans/ForestRock",
    "lastWriteTimeUtc": "2026-08-17T06:50:00Z"
  },

  "readiness": {
    "status": "Ready",
    "ruleVersion": 1,
    "blockingCount": 0,
    "warningCount": 0,
    "reasons": [
      {
        "code": "UE_READY",
        "severity": "Information",
        "message": "Indexed content satisfies the minimum Unreal Engine readiness rules."
      }
    ]
  },

  "destination": {
    "contentPath": "/Game/Megascans/Rocks/ForestRock",
    "assetBaseName": "ForestRock"
  },

  "mesh": {
    "primaryVariant": "Var1",
    "lods": [
      {
        "lod": 0,
        "sourcePath": "J:/Megascans/ForestRock/Var1/ForestRock_LOD0.fbx",
        "format": "FBX"
      },
      {
        "lod": 1,
        "sourcePath": "J:/Megascans/ForestRock/Var1/ForestRock_LOD1.fbx",
        "format": "FBX"
      }
    ]
  },

  "textures": [
    {
      "role": "BaseColor",
      "sourcePath": "J:/Megascans/ForestRock/ForestRock_4K_Albedo.jpg",
      "mapType": "Albedo",
      "setKind": "General",
      "resolution": 4096
    },
    {
      "role": "Normal",
      "sourcePath": "J:/Megascans/ForestRock/ForestRock_4K_Normal.jpg",
      "mapType": "Normal",
      "setKind": "General",
      "resolution": 4096
    }
  ],

  "material": {
    "profileId": "3d-asset-default",
    "masterMaterial": "/Game/Materials/M_Master_Megascans_3D",
    "materialInstanceName": "MI_ForestRock",
    "parameters": {
      "BaseColor": "BaseColorTexture",
      "Normal": "NormalTexture",
      "Roughness": "RoughnessTexture",
      "AO": "AOTexture",
      "Displacement": "HeightTexture",
      "Opacity": "OpacityTexture"
    }
  },

  "options": {
    "importLods": true,
    "enableNanite": true,
    "createMaterialInstance": true
  }
}
```

The exact output must be based on actual existing ScanVault models.

Do not add manifest fields that cannot be derived or configured reliably.

---

# Paths

Path semantics must be explicit.

## Source paths

The package may contain absolute local source paths because the UE57Editor consumer will run on the same workstation/library in the initial use case.

However, structure the model so future relative/package-root paths remain possible.

Each source file entry must have a clear semantic type.

Do not make the UE consumer infer roles from filenames.

## Destination path

Add a configurable Unreal destination content path.

Example:

```text
/Game/Megascans
```

The generated asset subpath should be deterministic.

For example:

```text
/Game/Megascans/3D_Assets/ForestRock
/Game/Megascans/Surfaces/MossyGround
/Game/Megascans/Plants/Fern
```

Do not hardcode a fragile English folder mapping deep inside a ViewModel.

Create a Core policy for destination-path generation.

The user must be able to override the destination base path before export.

---

# Asset Name Sanitization

Unreal asset/package naming has constraints.

MLV-14 should generate safe proposed names without pretending to validate every UE rule.

Add deterministic sanitization for:

- package folder segment;
- base asset name;
- Material Instance name.

Preserve original source names separately.

At minimum handle:

- spaces;
- punctuation;
- slash/backslash;
- repeated separators;
- empty names;
- leading/trailing whitespace;
- Unicode safely.

Do not destroy the original asset name stored in source metadata.

Add tests for deterministic sanitization.

---

# Master Material Profiles

This is a central part of MLV-14.

The system must support reusable **Master Material profiles** rather than generating a unique Material definition for every asset.

Initial conceptual profiles:

```text
Surface
3D Asset
3D Plant / Foliage
Atlas
Decal
Billboard
```

Profiles should be user-configurable.

Example:

```text
Profile: 3D Asset Default

Applies to:
    3D Asset

Master Material:
    /Game/Materials/M_Master_Megascans_3D

Parameters:
    BaseColor     -> BaseColorTexture
    Normal        -> NormalTexture
    Roughness     -> RoughnessTexture
    AO            -> AOTexture
    Displacement  -> HeightTexture
    Opacity       -> OpacityTexture
```

Do not make ScanVault inspect the actual UE Master Material.

This mapping is a declarative contract only.

The UE57Editor plugin will later validate whether the configured Master Material and parameter names really exist.

---

# Material Profile Model

Suggested fields:

```text
Id
Name
Description
AssetTypes
MasterMaterialPath
CreateMaterialInstance
MaterialInstancePrefix
TextureParameterMappings
DefaultOptions
IsBuiltIn / IsUser
```

Parameter mapping should use semantic roles, not Megascans filenames.

For example:

```text
BaseColor
Normal
Roughness
AO
Displacement
Opacity
```

mapped to UE scalar/name strings:

```text
BaseColorTexture
NormalTexture
RoughnessTexture
AOTexture
HeightTexture
OpacityTexture
```

Do not use arbitrary free-form source texture names as keys.

---

# Built-in Profiles

Provide conservative built-in profiles.

At minimum:

```text
Default Surface
Default 3D Asset
Default 3D Plant
Default Atlas
Default Decal
```

Do not assume all users have these Master Material paths.

Built-in profiles should be templates/configuration defaults and clearly editable.

If a Master Material path is not configured, package validation should report it.

Do not mark the package invalid solely because Unreal is not available; ScanVault cannot verify the UE asset exists.

---

# User Profile Persistence

Persist material profiles separately from the asset index.

Use the existing settings/config pattern if appropriate.

Requirements:

- user profiles survive restart;
- built-in profiles remain distinguishable from user profiles;
- user can duplicate/edit a built-in profile without mutating the built-in definition;
- profile IDs are stable;
- invalid/malformed profile storage fails safely.

Do not store user Material profile definitions inside individual assets.

---

# Profile Selection

When creating a package:

1. determine compatible profiles by asset type;
2. select a sensible default;
3. allow user to choose another compatible profile;
4. show the Master Material path;
5. show parameter mapping before export.

Remember the user's selected default profile by asset type if this fits the existing settings architecture.

Do not automatically choose an incompatible profile.

---

# Texture Semantic Roles

Introduce an import semantic role model separate from raw `TextureMapType`.

Suggested roles:

```text
BaseColor
Normal
Roughness
AO
Displacement
Opacity
Specular
Metalness
Emissive
Translucency
Other
```

Only add roles supported by actual normalized ScanVault data.

The mapping policy should translate existing `TextureMapType` values into import roles.

Examples:

```text
Albedo        -> BaseColor
Normal        -> Normal
Roughness     -> Roughness
Gloss         -> Roughness semantic family, with source type retained
AmbientOcclusion -> AO
Displacement  -> Displacement
Opacity       -> Opacity
```

Preserve the original normalized `TextureMapType` in the manifest.

Do not silently convert Gloss into Roughness numerically; that is UE import/material logic and belongs later in UE57Editor.

The manifest may indicate the semantic role while retaining `mapType = Gloss`.

---

# Texture Selection

When multiple possible texture components exist, selection must be deterministic.

Use the same primary-set logic or extracted shared policy used by MLV-13 where appropriate.

Avoid duplicated inconsistent logic between Readiness and Package generation.

For each semantic role:

- select one primary source deterministically;
- preserve useful metadata:
  - path;
  - normalized map type;
  - texture-set kind;
  - resolution;
  - format.

If multiple equally valid candidates exist and the selection is ambiguous:

- add a validation warning/error;
- do not silently choose based on filesystem enumeration order.

---

# Mesh / Variant Selection

For 3D assets/plants, the package must explicitly describe mesh variants and LODs.

Initial implementation may export a selected primary variant.

The model should be extensible to multiple variants.

Requirements:

- deterministic variant ordering;
- deterministic LOD ordering;
- preserve LOD number;
- preserve format;
- preserve source path.

If an asset has VAR1 / VAR2 / VAR3, expose variant selection in package preview if the current inventory model supports it cleanly.

Do not collapse all physical variants into one mesh list without preserving variant identity.

---

# Nanite Option

MLV-14 may include a requested import option:

```json
"enableNanite": true
```

This is only declarative.

ScanVault must not decide whether Nanite is technically valid by opening Unreal assets.

Recommended default policy:

- 3D Asset: true
- 3D Plant: configurable / conservative
- Surface: not applicable
- Atlas: not applicable
- Decal: not applicable

Keep this in an explicit package-option policy/profile.

Do not hardcode it in WPF code-behind.

---

# LOD Import Option

Manifest should contain:

```text
importLods: true/false
```

If the inventory has only LOD0:

- package may still be valid;
- MLV-13 warning remains visible;
- `importLods` may be false or true with only one entry, depending on clean implementation.

Do not invent missing LODs.

---

# Material Instance Naming

Generate a proposed Material Instance name.

Default:

```text
MI_<SanitizedAssetName>
```

Make the prefix configurable in the material profile.

Do not create the Material Instance in ScanVault.

---

# Package Validation

Before export, validate the package.

Create a deterministic validation policy.

Suggested validation severities:

```text
Error
Warning
Information
```

Suggested checks:

## Errors

- unsupported manifest schema;
- missing source asset identity;
- blocked MLV-13 readiness;
- required primary mesh missing for mesh asset;
- required BaseColor missing;
- profile missing for a package that requests Material Instance creation;
- invalid destination `/Game` path;
- no selected source for a required semantic role.

## Warnings

- readiness is `ReadyWithWarnings`;
- missing optional map;
- no LODs;
- incomplete LOD chain;
- no Master Material path configured;
- ambiguous optional texture role;
- Nanite requested for a category with uncertain suitability;
- source path no longer exists at preview/export time.

Do not reimplement all MLV-13 reasons; reuse and surface them.

---

# Source Existence Validation

At package preview/export time, it is acceptable to verify that source paths still exist.

This is metadata/file-existence validation only.

Do not read binary contents.

If a source file disappeared after Rescan:

- package validation must report it;
- export should be blocked if required input is missing.

Optional missing files should produce warnings where appropriate.

---

# Determinism

Given:

- same indexed asset;
- same profile;
- same destination settings;
- same package options;

the semantic manifest must be identical except explicitly non-semantic generator metadata such as `generatedAtUtc`.

For tests, support generation with injected/fixed clock/build metadata.

Stable ordering is required for:

- textures;
- variants;
- LODs;
- validation issues;
- readiness reasons;
- material mappings.

Do not depend on dictionary/hashset enumeration order.

---

# Generator Metadata

Include useful provenance:

```text
ScanVault application version
commit SHA
package schema version
readiness rule version
generatedAtUtc
```

Do not include machine-specific noise unless useful.

Consider including normalized library root only if required.

---

# Package Identity

Add a deterministic package identity separate from asset ID.

It may be derived from semantic fields such as:

```text
schemaVersion
assetId
source json path or physical identity
selected variant
profile ID
destination path
```

A stable hash/ID is useful for future UE57Editor idempotency.

Do not include `generatedAtUtc` in the semantic package identity.

Suggested field:

```json
"packageId": "..."
```

Document how it is computed.

---

# Future Idempotency

MLV-14 does not implement import/reimport, but design the package so UE57Editor can later decide:

```text
new import
already imported
source changed
profile changed
package changed
reimport/update required
```

For that reason include enough stable provenance to compare packages.

Do not implement a UE import registry in ScanVault in this ticket.

---

# Export Format

Initial export format:

```text
*.scanvault-ue.json
```

or another clear extension.

Recommended:

```text
<SanitizedAssetName>.scanvault-ue.json
```

Do not use generic `.json` if a dedicated extension improves discoverability.

The actual JSON remains standard UTF-8 JSON.

---

# Export Folder

Allow user to choose a destination folder for manifests.

Remember the last used export folder if consistent with existing report/export settings.

Do not write into the source Megascans asset folder by default.

Preferred default is a separate package/export folder.

---

# Optional Package Directory

Design for a future package directory:

```text
UEImportPackages/
    ForestRock.scanvault-ue.json
    Fern.scanvault-ue.json
```

MLV-14 only needs manifest export.

Do not copy source meshes/textures into the package directory unless explicitly required by later tickets.

The manifest should point to the existing source library.

---

# UI

Add a user-facing flow from the selected asset.

Suggested entry points:

```text
Create UE Import Package
```

available in:

- selected asset action/context menu;
- asset details if appropriate.

Do not overload the main grid.

---

# Package Preview Window

Add a dedicated preview/configuration window.

Suggested sections:

## Header

- Asset name
- Asset type
- MLV-13 readiness badge
- package schema version

## Destination

- Unreal base path
- generated final content path
- sanitized asset name

## Mesh

- selected variant
- LOD list
- source formats

## Textures

Table:

```text
Role | Map Type | Resolution | Format | Source
```

## Material

- selected profile
- Master Material path
- Material Instance name
- parameter mappings

## Options

- Import LODs
- Enable Nanite
- Create Material Instance

## Validation

- Errors
- Warnings
- MLV-13 readiness reasons

## Actions

```text
Export Package
Copy Manifest
Open Export Folder
Cancel
```

Do not perform UE import.

---

# Preview JSON

Provide an optional raw JSON preview or `Copy Manifest` command.

The preview must use the exact serializer used for export.

Do not maintain a second hand-built JSON representation.

---

# Settings

Add persistent settings for at least:

```text
Default UE destination base path
Last manifest export folder
Default material profile by asset type
```

Do not mix these into library scanning settings if the current architecture supports a separate import-package settings section cleanly.

---

# Core / Infrastructure / App Boundaries

## Core

Own:

- import package models;
- schema version;
- semantic role mapping;
- package generation policy;
- destination path policy;
- sanitization policy;
- package validation;
- material profile models and compatibility logic;
- deterministic package identity.

No WPF.
No SQLite-specific code.
No shell APIs.

## Infrastructure

Own:

- profile persistence;
- settings persistence if appropriate;
- manifest file serialization/export;
- file-existence validation helper if needed;
- atomic file write.

## App

Own:

- preview/config ViewModel;
- commands;
- WPF window;
- profile selection UI;
- save/export dialogs;
- user-facing validation.

---

# Manifest Serialization

Use explicit serializer options.

Requirements:

- UTF-8;
- stable enum representation;
- readable JSON;
- camelCase unless project conventions strongly favor another format;
- deterministic property/order behavior where practical;
- schemaVersion always present;
- null/optional behavior explicitly tested.

Avoid polymorphic magic.

Do not serialize WPF/ViewModel types.

---

# Atomic Export

Manifest export should be safe.

Write to a temporary file and atomically replace/move to final destination where feasible.

Cancellation/failure must not leave a partially written manifest.

Do not modify source library files.

---

# Existing File Behavior

If the target manifest already exists, do not silently overwrite.

Use an explicit policy:

```text
Ask / Replace
```

or generate a safe alternative filename.

For initial implementation, a Save File dialog with normal overwrite confirmation is acceptable if aligned with existing application patterns.

---

# Material Profiles UI

Add a simple profile management surface.

At minimum support:

- list profiles;
- inspect;
- create;
- duplicate;
- edit;
- delete user profile;
- choose compatible asset types;
- edit Master Material path;
- edit parameter mappings;
- Material Instance prefix;
- defaults for Nanite / LOD / Material Instance.

Built-in profiles cannot be destructively edited; duplicate to customize.

Do not build an oversized design-system editor.

---

# Profile Validation

Validate:

- non-empty ID/name;
- unique user profile name/ID as appropriate;
- compatible asset types selected;
- Master Material path syntax looks like `/Game/...` when provided;
- parameter names non-empty;
- no duplicate semantic role mappings;
- valid MI prefix.

Do not claim the Master Material exists — only UE57Editor can verify that.

---

# MLV-13 Integration

Package preview should display MLV-13 readiness exactly as persisted.

Do not rerun a second readiness algorithm in the ViewModel.

If readiness is stale by rule version:

- block package generation;
- instruct user to Rescan/recompute.

If MLV-13 reports `ReadyWithWarnings`, show those warnings in the package validation area.

---

# MLV-9 History

MLV-14 does not need to make manifest generation itself part of scan history.

Do not pollute asset-change history when a user merely exports a package.

Profile/settings changes are application configuration, not library changes.

---

# MLV-11 Export

Do not overload the existing report-export subsystem with package semantics if it makes the architecture unclear.

It is acceptable for manifest export to use a separate `IUnrealImportPackageExportService`.

Reuse common atomic/file-dialog helpers where appropriate.

---

# MLV-12 Duplicate Assets

For duplicate same-ID physical copies, package generation must operate on the explicitly selected physical asset identity where possible.

Do not accidentally resolve only by Asset ID.

Use:

- JSON path;
- physical asset folder;
- or another stable physical source identity.

This is especially important because MLV-12 intentionally preserves skipped same-ID physical sources outside the normal winner-only catalog.

If MLV-14 is initially only exposed from normal browsable assets, document that limitation.

Do not silently generate a package for the wrong same-ID copy.

---

# Performance

Package generation should be cheap.

It operates on one selected indexed asset and optional file-existence checks.

Requirements:

- no binary file reads;
- no hashing unless package identity specifically requires existing indexed hashes;
- no full library rescan;
- no blocking heavy work on the UI thread;
- cancellation for export/validation if asynchronous.

---

# Logging

Add structured logs for:

```text
package preview created
package validation failed
package exported
profile created
profile updated
profile deleted
```

Do not log full JSON manifests at normal log levels.

Paths may be logged consistently with existing application policy.

---

# Security / Safety

Treat manifest paths as data.

Do not execute any command embedded in manifests/profiles.

Do not allow profile fields to become shell command arguments.

MLV-14 produces JSON only.

---

# Tests

Add comprehensive tests.

## Core tests

At minimum:

1. ready Surface package;
2. ready 3D Asset package;
3. ReadyWithWarnings package;
4. NotReady blocked;
5. Unknown blocked;
6. stale readiness blocked;
7. texture map → semantic role mapping;
8. Gloss retains source type while mapping to Roughness semantic role;
9. deterministic texture selection/order;
10. deterministic LOD order;
11. variant identity preserved;
12. destination path generation;
13. asset-name sanitization;
14. Material Instance naming;
15. profile compatibility;
16. missing Master Material path warning;
17. missing required map error;
18. missing required mesh error;
19. package identity stable;
20. generated timestamp excluded from semantic package identity;
21. same semantic inputs produce deterministic manifest model;
22. different profile changes package identity;
23. different destination changes package identity.

## Infrastructure tests

At minimum:

1. user material profiles persist across restart;
2. malformed profile storage fails safely;
3. atomic manifest export;
4. cancellation/failure leaves no partial file;
5. exported JSON round-trips;
6. schemaVersion persisted;
7. UTF-8 path/name support;
8. existing file behavior;
9. source existence validation;
10. no source library file modified.

## App/ViewModel tests

At minimum:

1. command enabled for Ready;
2. command enabled for ReadyWithWarnings;
3. command disabled or blocked for NotReady;
4. stale readiness blocked;
5. compatible profile selection;
6. changing profile refreshes preview;
7. changing destination refreshes path;
8. option changes refresh package model;
9. validation errors disable Export;
10. warnings do not necessarily disable Export;
11. Copy Manifest uses real serializer;
12. Export calls package export service;
13. profile CRUD command states.

---

# Synthetic Examples

Use realistic synthetic fixtures for:

- Surface
- 3D Asset
- 3D Plant
- Atlas
- Decal

Do not depend on the user's real Megascans library.

If a real library is manually tested, document it separately and never modify it.

---

# Migration / Compatibility

If profile/settings persistence introduces a schema/version:

- version it explicitly;
- migrate non-destructively;
- preserve user profiles;
- reject future unsupported versions safely.

The import manifest schema version and profile-storage schema version are separate concepts.

Do not conflate them.

---

# Documentation

Create/update:

```text
Task/MLV-14/investigation.md
Task/MLV-14/implementation-plan.md
Task/MLV-14/implementation-report.md
review/review-MLV-14.md
```

Also create or update user-facing technical documentation describing the manifest contract.

Recommended:

```text
docs/unreal-import-package.md
```

Document:

- architectural boundary;
- schema version;
- example manifest;
- semantic roles;
- Material Profile model;
- package identity;
- readiness requirements;
- what ScanVault does NOT do;
- intended UE57Editor consumer behavior.

---

# Investigation Requirements

Before implementation, Codex must inspect the real repository.

Specifically investigate:

- current `AssetSummary`;
- `AssetContentInventory`;
- mesh variant/LOD models;
- `TextureMapType`;
- texture-set selection logic;
- MLV-13 `UnrealReadinessPolicy`;
- MLV-12 physical duplicate-source model;
- settings persistence;
- smart collection persistence patterns;
- report export/atomic file-writing patterns;
- WPF command/context-menu patterns;
- build metadata model;
- diagnostics/logging conventions.

Use Graphify and code-review-graph per repository workflow when available.

Do not implement from this ticket text alone without source inspection.

---

# Implementation Strategy

Prefer incremental steps.

## Step 1 — Domain contract

- manifest models;
- schema version;
- semantic roles;
- profile models;
- validation models.

## Step 2 — Policies

- role mapping;
- destination path;
- name sanitization;
- profile compatibility;
- package generator;
- package validator;
- deterministic package identity.

## Step 3 — Persistence

- material profile store;
- import-package settings;
- serializer/export service.

## Step 4 — UI

- command entry point;
- preview/config ViewModel;
- WPF window;
- profile selection;
- validation display;
- export/copy.

## Step 5 — Tests

- Core;
- Infrastructure;
- App;
- round-trip;
- persistence;
- determinism.

## Step 6 — Documentation / validation

- task docs;
- review;
- CI;
- manual smoke test if possible.

---

# Out of Scope

Do NOT implement in MLV-14:

- actual Unreal Engine import;
- UE commandlet;
- Editor Utility Widget;
- UE Python importer;
- UE C++ importer;
- `.uasset` creation;
- actual Material Instance creation;
- verification that Master Material exists;
- parameter validation against UE asset;
- Nanite activation;
- texture compression/sRGB changes;
- Virtual Texture conversion;
- collision generation;
- reimport;
- delete/update of UE assets;
- automatic Fab/Epic download.

Those belong to the future UE57Editor consumer ticket(s).

---

# Acceptance Criteria

MLV-14 is complete when:

1. `Create UE Import Package` exists.
2. Ready assets can generate a package.
3. ReadyWithWarnings assets can generate a package with warnings visible.
4. NotReady/Unknown/stale assets are blocked safely.
5. Manifest schema is explicitly versioned.
6. Manifest is deterministic.
7. Semantic package identity is deterministic.
8. Package identity excludes generated timestamp.
9. Source asset identity is preserved.
10. Physical same-ID ambiguity cannot silently select the wrong asset.
11. Mesh variant and LOD identity are preserved.
12. Texture roles are explicit.
13. UE consumer does not need filename parsing for supported roles.
14. Master Material profile is included.
15. Material parameter mappings are included.
16. Material Instance naming is included.
17. destination `/Game/...` path is included.
18. Nanite/LOD/MI options are declarative only.
19. user material profiles persist.
20. built-in profiles are not destructively editable.
21. profile validation works.
22. package validation works.
23. missing source files are detected before export.
24. export is atomic.
25. exported JSON round-trips.
26. no source Megascans file is changed.
27. Unreal Engine is not launched.
28. no `.uasset` is created.
29. tests pass.
30. GitHub Actions is green.
31. documentation describes the contract for UE57Editor.

---

# Validation

Run:

```powershell
dotnet restore ScanVault.sln -v:minimal
dotnet build ScanVault.sln --configuration Release --no-restore -m:1 -v:minimal
dotnet test ScanVault.sln --configuration Release --no-build -m:1 -v:minimal
dotnet format ScanVault.sln --verify-no-changes --no-restore
git diff --check
```

Run repository code-intelligence workflow:

```powershell
$env:PYTHONIOENCODING="utf-8"
code-review-graph update --brief
code-review-graph detect-changes --base HEAD --brief
```

Attempt Graphify refresh.

If Graphify still fails with `[WinError 5] Access is denied`, record it accurately.

Do not use Graphify failure as an excuse to skip source/build/tests.

---

# GitHub Actions

After pushing the implementation, verify the actual GitHub workflow for the exact commit.

Current repository CI is expected to run on push and PR.

Report:

```text
workflow name
run number
head SHA
status
conclusion
```

Do not call CI green based only on local tests.

---

# Manual Validation

If practical, manually test:

1. select UE Ready Surface;
2. open Create UE Import Package;
3. verify destination;
4. verify textures;
5. select/change profile;
6. verify Master Material mapping;
7. export;
8. inspect JSON;
9. repeat for 3D Asset with LODs;
10. verify ReadyWithWarnings;
11. verify NotReady is blocked;
12. verify source files unchanged.

Record whether this was actually done.

---

# Final Codex Report

Return a concise final report with:

1. architecture implemented;
2. files changed;
3. manifest schema;
4. example manifest;
5. package identity algorithm;
6. readiness eligibility policy;
7. destination-path policy;
8. sanitization policy;
9. semantic texture roles;
10. variant/LOD behavior;
11. Material Profile model;
12. built-in profiles;
13. user profile persistence;
14. validation rules;
15. export behavior;
16. UI flow;
17. test counts by project;
18. build result;
19. format result;
20. `git diff --check`;
21. CRG result;
22. Graphify result;
23. GitHub Actions result;
24. manual validation result;
25. known limitations;
26. explicit confirmation that no Unreal import is implemented in MLV-14.

Do not create a PR unless explicitly requested.

Do not modify/delete/move real Megascans source assets.
