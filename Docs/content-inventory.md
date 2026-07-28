# Asset content inventory

## Purpose and safety

MLV-7 inventories the real contents of each indexed legacy Megascans asset folder. It is strictly read-only: traversal reads directory entries and JSON metadata, never mesh bytes or texture pixels. ScanVault does not import, repair, rename, move, delete, generate, or rewrite source content.

## Discovery

Only deterministic duplicate-ID winners are inventoried. Up to four asset folders are processed concurrently outside the WPF thread. Each subtree is traversed with cancellation checks, reparse points skipped, and inaccessible directories recorded. Results are returned in the original asset order before the single SQLite replacement transaction.

Recognized mesh extensions are `.fbx` and `.abc`. Recognized texture extensions are `.jpg`, `.jpeg`, `.png`, `.tif`, `.tiff`, `.exr`, and `.tga`. Other files are ignored unless a recognized mesh/texture cannot be classified, in which case its full path and reason are retained as an unclassified file.

## Conservative filename rules

Variant and LOD tokens are complete case-insensitive tokens `VarN` and `LODN`, where N is any non-negative integer. They may occur in a filename or parent folder. A mesh without exactly one LOD token, or with conflicting variant/LOD tokens, is not guessed. A mesh with one LOD and no variant is grouped as `Default`.

Texture aliases normalize as follows while `RawMapName`, `FileName`, and full path remain unchanged:

| Raw aliases | Normalized map |
| --- | --- |
| Albedo, BaseColor, Diffuse | Albedo |
| Normal, NormalBump, NormalObject | Normal |
| Alpha, Opacity | Opacity |
| AO, AmbientOcclusion | AmbientOcclusion |
| Height, Displacement | Displacement |

Roughness, Gloss, Specular, Translucency, Cavity, Bump, and Brush stay distinct. Filename resolution tokens recognize 1K/2K/4K/8K/16K and 512/1024/2048/4096/8192 without opening the image.

Texture-set classification priority is folder context, filename token, matching JSON-reference context, then `General`. Brackets and case are ignored for `Atlas`/`Billboard` folder tokens. A sibling Billboard folder never changes unrelated textures.

## Duplicate and issue policy

Mesh identity is variant + LOD + format. Texture identity is set kind + normalized map + resolution + format. When a key has multiple paths, every candidate is preserved and the asset becomes `Ambiguous`; ScanVault does not choose a hidden winner. Conflicting names, case-only candidates, content-like missing JSON references, inaccessible directories, and unclassified files have explicit issue codes and messages.

## Confirmed completeness profiles

The user confirmed these rules on 2026-07-28:

- **3D Asset Complete:** FBX, LOD0, Albedo, Normal or Bump, and Roughness or Gloss.
- **3D Plant Complete:** the 3D Asset requirements plus Opacity.
- **Atlas Complete:** Albedo, Opacity, Normal or Bump, and Roughness or Gloss.
- **Surface Complete:** Albedo, Normal or Bump, and Roughness or Gloss.
- **Brush Complete:** a Brush map.

ABC is useful inventory content; an ABC-only 3D asset may be `Usable` but cannot be `Complete`. Billboard is optional. JPG can satisfy a component without EXR. Roughness/Gloss and Normal/Bump are alternatives, not duplicate mandatory requirements.

Status priority is:

1. `Ambiguous` for unresolved duplicate/conflict keys;
2. `MissingCriticalFiles` for a missing primary mesh/map or recognized missing reference;
3. `Complete` when the full type profile is met;
4. `Usable` when critical visual content exists but a Complete-only requirement is absent;
5. `Partial` is reserved for recognized but only partly usable future profiles;
6. `Unknown` when no supported asset-kind profile exists.

## Persistence and navigation

Schema v3 stores the full structured inventory as JSON plus indexed scalar/map projections. Inventory replacement, stale-row removal, scan state, and normalization promotion share the full-scan transaction. A cancelled or failed scan therefore leaves the previous inventory available.

Search covers mesh/texture filenames, normalized maps, status, issue messages, variant names, and LOD labels. Persistent filters are Has FBX, Has LODs, Billboard, Atlas, Complete, Incomplete, and Ambiguous; status filters are mutually exclusive while capability filters compose. Sort modes include completeness, variant count, distinct LOD count, and texture-set count with stable ID/path tie-breakers.

Cards show at most five high-value text badges and a clear ambiguous/incomplete warning. Hover content is a bounded count/summary view. “Show content inventory” opens a read-only grouped window for variants/LODs, texture sets, all classified/unclassified paths, and issues; paths can be copied and containing folders opened.

## Performance characteristics

Inventory work is O(number of directory entries) and holds only paths/domain records, not file contents. Parallelism is capped at four assets. SQLite writes are batched in the existing single transaction. The MLV-7 implementation report records the measured read-only run over `tests/Megascan`; automated correctness tests use only tiny sanitized temporary fixture trees.
