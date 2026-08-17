# Unreal Import Package Manifest

MLV-14 defines the ScanVault-to-UE57Editor handoff contract. ScanVault produces a deterministic JSON manifest that describes what should be imported and how semantic pieces should connect. It does not perform the Unreal import.

## Boundary

ScanVault owns library analysis, normalized metadata, content inventory, MLV-13 readiness, semantic role mapping, manifest generation, validation, preview, and export.

ScanVault does not launch Unreal Engine, call Unreal APIs, create `.uasset` files, import meshes or textures, create Material Instances, change Nanite/LOD/texture settings, inspect UE assets, or modify Megascans source files.

UE57Editor will later consume the manifest, resolve `/Game/...` assets, execute import tasks, create Material Instances, assign parameters, configure LODs/Nanite, and handle UE-version-specific behavior.

## Schema

The manifest is UTF-8 JSON with explicit `schemaVersion`.

```json
{
  "schemaVersion": 1,
  "packageId": "7bb1f6c0f4a2d91c0ef94b2f7b0cc6ae"
}
```

The schema version belongs to the import-package contract and is independent from application version.

## Main Sections

- `generator`: ScanVault application version, commit SHA, and generation timestamp.
- `source`: asset ID, original name, normalized asset type, JSON path, asset folder path, and last write time.
- `readiness`: persisted MLV-13 status, rule version, counts, and reasons.
- `destination`: user base `/Game/...` path, final content path, sanitized asset base name, and original source name.
- `mesh`: selected primary variant and ordered LOD entries when present.
- `textures`: one deterministic source per supported semantic role.
- `material`: selected material profile snapshot, compatible asset types, Master Material path, Material Instance name, prefix, and active parameter mappings.
- `options`: declarative import choices such as `importLods`, `enableNanite`, and `createMaterialInstance`.
- `validation`: deterministic errors/warnings/information for preview and export.

## Semantic Texture Roles

ScanVault maps normalized `TextureMapType` values to import roles:

- `Albedo` -> `BaseColor`
- `Normal` and `Bump` -> `Normal`
- `Roughness` and `Gloss` -> `Roughness`
- `AmbientOcclusion` and `Cavity` -> `AO`
- `Displacement` -> `Displacement`
- `Opacity` -> `Opacity`
- `Specular` -> `Specular`
- `Translucency` -> `Translucency`
- unsupported values -> `Other`

The manifest preserves the original normalized `mapType`. For example, `Gloss` remains `mapType: "gloss"` while carrying role `roughness`. ScanVault does not numerically invert or convert gloss maps.

When more than one texture in the selected primary texture set maps to the same semantic role, ScanVault records an `ambiguousTextureRole` validation warning before collapsing candidates. Schema v1 still exports only the selected texture for each role.

Candidate priority is deterministic:

1. preferred/native map type for the role;
2. higher resolution;
3. normalized map type;
4. texture format;
5. normalized source path text.

For the `Roughness` role, native `Roughness` is preferred over `Gloss`; `Gloss` remains a fallback and keeps its original `mapType`.

## Package Identity

`packageId` is a SHA-256 based stable semantic identity truncated to 32 lowercase hex characters. It includes:

- schema version;
- asset ID;
- source JSON path;
- physical asset folder path;
- source `lastWriteTimeUtc` as the existing indexed source revision marker;
- readiness status and readiness rule version;
- destination content path;
- sanitized asset base name;
- material profile ID;
- material profile compatible asset types sorted case-insensitively and normalized to uppercase for identity hashing;
- Master Material path;
- Material Instance prefix;
- generated Material Instance name;
- all texture parameter mappings sorted by semantic role and parameter name;
- selected primary variant;
- declarative options;
- selected texture roles, map types, source paths, texture-set kind, resolution, and format;
- selected mesh LOD variant, LOD number, format, and source path.

Before hashing, path separators are normalized to `/`, booleans are normalized as lowercase `true`/`false`, numeric values use invariant formatting, and every collection is explicitly sorted. Path text is not lowercased; ScanVault preserves indexed physical path identity while making separator behavior stable.

It excludes `generatedAtUtc`, so regenerating the same package at a different time does not change identity.

## Readiness Eligibility

Allowed by default:

- `Ready`
- `ReadyWithWarnings`

Blocked by validation:

- `NotReady`
- `Unknown`
- `NotApplicable`
- stale readiness rule version or missing evaluation timestamp

`ReadyWithWarnings` remains exportable but surfaces a package warning and all readiness reasons.

## Destination and Names

The user chooses a destination base path such as `/Game/Megascans`. ScanVault generates deterministic subpaths by normalized asset type and sanitized asset name, for example:

```text
/Game/Megascans/3D_Assets/Forest_Rock
/Game/Megascans/Surfaces/Mossy_Ground
```

Sanitization handles whitespace, punctuation, slash/backslash, repeated separators, empty names, and Unicode letters/digits without changing the original source name stored under `source` and `destination.originalAssetName`.

## Material Profiles

Material profiles are declarative templates. They include stable ID, name, compatible asset types, Master Material path, Material Instance prefix, default options, and semantic-role-to-parameter mappings.

The material profile snapshot in every manifest includes the compatible asset-type set from the selected profile. Package validation blocks export when the current source asset type is not present in that set, using case-insensitive comparison. ScanVault does not silently re-enable the current asset type or switch profiles; a user can save a profile for another asset type, but cannot export it for an incompatible current asset.

Built-in templates are provided for Surface, 3D Asset, 3D Plant, Atlas, Decal, and Billboard. Built-ins are visible, selectable, and usable, but not destructively edited or deleted.

The package window includes a simple profile editor. A user can create a new profile, duplicate a built-in or user profile, edit user profiles, save, and delete user profiles. Editable user-profile fields are:

- name;
- description;
- compatible asset types;
- Master Material `/Game/...` path;
- Material Instance prefix;
- active texture parameter mappings for BaseColor, Normal, Roughness, AO, Displacement, and Opacity;
- default Import LODs, Enable Nanite, and Create Material Instance options.

`New Profile` creates a mutable user profile for the current asset type from the current built-in template and does not persist it until `Save user` is used. `Duplicate` creates a mutable user copy of the current profile and also waits for `Save user` before persistence.

Mapping presence is part of the UE57Editor contract:

- mapping present: UE57Editor should assign that semantic role to the named Material Instance parameter;
- mapping absent: UE57Editor should not assign that semantic role.

The editor shows every supported role, but enabled state is separate from the parameter-name suggestion. Disabled mappings are not serialized into the profile or manifest, do not trigger missing optional texture warnings, and do not affect `packageId` through placeholder text. Enabling a mapping, disabling a mapping, or changing an active parameter name changes `packageId`.

ScanVault validates only profile syntax. It does not verify that the Master Material or parameter names exist in Unreal.

## Export

The recommended extension is:

```text
<SanitizedAssetName>.scanvault-ue.json
```

Export writes to a temporary file and then publishes the destination. Cancellation or failure does not intentionally publish a partial manifest. Source files are referenced in place and are not copied or modified.

## Example

```json
{
  "schemaVersion": 1,
  "packageId": "7bb1f6c0f4a2d91c0ef94b2f7b0cc6ae",
  "source": {
    "assetId": "asset-id",
    "name": "Forest Rock",
    "assetType": "3D Asset",
    "jsonPath": "J:/Megascans/ForestRock/asset.json",
    "assetFolderPath": "J:/Megascans/ForestRock"
  },
  "destination": {
    "baseContentPath": "/Game/Megascans",
    "contentPath": "/Game/Megascans/3D_Assets/Forest_Rock",
    "assetBaseName": "Forest_Rock",
    "originalAssetName": "Forest Rock"
  },
  "mesh": {
    "primaryVariant": "Var1",
    "lods": [
      {
        "variant": "Var1",
        "lod": 0,
        "sourcePath": "J:/Megascans/ForestRock/Var1/asset_LOD0.fbx",
        "format": "fbx"
      }
    ]
  },
  "textures": [
    {
      "role": "baseColor",
      "sourcePath": "J:/Megascans/ForestRock/asset_4K_Albedo.jpg",
      "mapType": "albedo",
      "setKind": "general",
      "resolution": 4096,
      "format": ".jpg"
    }
  ],
  "options": {
    "importLods": true,
    "enableNanite": true,
    "createMaterialInstance": true,
    "readinessOverride": false
  }
}
```
