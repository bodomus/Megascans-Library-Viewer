# SCAN-2 — Normalize Megascans metadata and improve asset cards, hover popup, sorting, and navigation

## Status

Ready for implementation.

## Project

**ScanVault — Megascans Library Viewer**

## Summary

The first usable ScanVault version successfully scans a legacy Megascans library, builds a local SQLite index, displays the physical folder tree, renders thumbnail cards, shows metadata on hover, and opens previews.

Manual validation revealed several data-quality and usability issues:

```text
Type: normal
Maximum resolution: 400,400
Biome: undefined
Region: undefined
```

`normal` and `specular` are texture-component types, not asset types. Resolution parsing is also incorrect, and absent values should not appear as literal `undefined`.

This ticket must correct metadata normalization first, then improve browsing ergonomics with sorting, card hierarchy, persistent selection, folder counts, context actions, and keyboard navigation.

---

# Goal

Make ScanVault display correct, normalized, human-readable metadata and provide practical catalog navigation.

After this ticket, the viewer should clearly answer:

- what the asset is;
- what its Megascans ID is;
- which type and categories it belongs to;
- what maximum resolution is available;
- where it is stored;
- which asset is selected;
- how the visible collection is sorted.

---

# Mandatory workflow

This is a **Level 2 structural task**.

Before implementation, Codex must:

1. Resolve the repository root.
2. Read `AGENTS.md`.
3. Read `.codex/PRE_TICKET_WORKFLOW.md`.
4. Execute `$graphify-repository-analysis`.
5. Execute `$code-review-graph-analysis`.
6. Inspect current implementation and tests.
7. Validate graph findings against source.
8. Create:
   - `Task/SCAN-2/investigation.md`;
   - `Task/SCAN-2/implementation-plan.md`.
9. Implement only after the plan is complete.
10. Update CRG after implementation.
11. Inspect changed symbols and blast radius.
12. Run targeted and broader validation.
13. Refresh Graphify only if architecture or subsystem boundaries changed.
14. Create `Task/SCAN-2/implementation-report.md`.

Do not invent Graphify or CRG commands.

Preserve unrelated user changes.

---

# Required investigation

Before editing, determine and document:

1. Where `AssetType` is currently extracted.
2. Why `components[].type`, such as `normal` or `specular`, can reach the asset card.
3. Where maximum resolution is parsed and why `4096x4096` can become `400,400`.
4. Whether malformed normalized data is already persisted in SQLite.
5. Whether the fix requires:
   - schema migration;
   - parser-version marker;
   - index rebuild;
   - corrective rescan.
6. How missing values become literal `undefined`.
7. Which layer owns display formatting.
8. Whether filtering and sorting occur in SQLite, memory, `ICollectionView`, or another query service.
9. Whether cards already support selection and keyboard focus.
10. Whether the hover popup already has delayed-open coordination and stale-hover cancellation.

The implementation plan must identify the smallest coherent correction.

---

# Part A — Metadata normalization

## 1. Asset type

Do not derive asset type from texture components.

The following values are component/map types, not asset types:

```text
normal
specular
roughness
albedo
opacity
displacement
bump
gloss
translucency
```

Use deterministic priority when resolving `AssetType`:

1. `semanticTags.asset_type`;
2. explicit top-level asset type field from a known schema variant;
3. root classification/category data;
4. `assetCategories`;
5. known category inference;
6. fallback `Unknown`.

Expected normalized display values include:

```text
3D Asset
3D Plant
Atlas
Surface
Decal
Brush
Imperfection
Unknown
```

Preserve the raw source value separately when useful for diagnostics.

Do not infer botanical species or unsupported semantic identity.

## 2. Categories

Keep categories independent from `AssetType`.

Example:

```text
Asset type: 3D Asset
Categories: 3D / brick / debris / rubble
```

Normalize categories by:

- trimming whitespace;
- removing empty values;
- removing exact duplicates case-insensitively;
- preserving stable source order where practical;
- using consistent display casing without changing canonical IDs;
- avoiding repeated asset-name values when they add no information.

## 3. Maximum resolution

Correctly resolve the maximum available map resolution.

Accepted source forms may include:

```text
4096x4096
4096 × 4096
4K
2048x2048
1024
```

Use a structured value instead of fragile string replacement.

Recommended form:

```csharp
public readonly record struct ImageResolution(int Width, int Height)
{
    public int MaxDimension => Math.Max(Width, Height);
}
```

Expected display:

```text
4K (4096 × 4096)
2K (2048 × 2048)
1K (1024 × 1024)
4096 × 2048
—
```

Rules:

- derive maximum from all available component resolutions;
- do not assume every map has the same resolution;
- preserve non-square dimensions;
- ignore malformed entries safely;
- log malformed resolution values at an appropriate level;
- do not use culture-sensitive parsing for the `x` separator.

## 4. Physical size

Normalize:

```text
0.96x0.96
1x1
0.96x0.96 m
```

Expected display:

```text
0.96 × 0.96 m
1 × 1 m
—
```

Do not append units twice.

Do not reinterpret unknown units.

## 5. Texel density

Expected display:

```text
4264 px/m
—
```

Rules:

- parse invariant values;
- retain meaningful precision;
- avoid floating-point artifacts;
- omit the row when unavailable if the popup uses conditional rows.

## 6. Missing and invalid values

The UI must not display literal placeholders originating from source data:

```text
undefined
null
N/A
unknown
""
```

Exception: `Unknown` is allowed only as the deliberate normalized asset-type fallback.

Create one normalization policy for optional strings:

- trim whitespace;
- convert known placeholder tokens to `null`;
- preserve legitimate readable text;
- compare placeholder tokens case-insensitively.

Recommended popup policy:

Always show:

- Name
- ID
- Asset type
- Categories
- Folder

Show only when available:

- Biome
- Region
- Physical size
- Maximum resolution
- Texel density
- State
- Colors
- Industries

## 7. Tags

Preserve tag groups where present:

```text
Descriptive
State
Colors
Industry
Contains
Theme
```

Normalize:

- leading and trailing spaces;
- duplicates;
- placeholder strings;
- repeated values within a group.

Do not lose tag-group identity in the domain/index model.

## 8. Stable ID display

Display the Megascans ID explicitly:

```text
ID: tfcnacapa
```

Retain original casing and exact value.

---

# Part B — Existing-index upgrade

The application must not require users to manually delete SQLite.

Implement and document a deterministic upgrade strategy.

Preferred approaches:

## Option A — parser/index version

Store a normalization/index version.

When an older version is detected:

- mark index as requiring rescan;
- show a concise message;
- allow explicit Rescan;
- preserve the previous index until a successful replacement commits.

## Option B — schema migration plus reparse

Use a migration if structured fields require new columns.

Schema may migrate automatically, but corrected metadata must be reparsed from source before becoming authoritative.

Required behavior:

- no manual database deletion;
- no silent corruption;
- prior usable index survives failed or cancelled correction;
- implementation report explains the selected strategy.

---

# Part C — Asset card redesign

## Required hierarchy

Each card should display:

1. preview image;
2. normalized asset name;
3. useful type/category line;
4. compact resolution or ID line.

Recommended examples:

```text
Mossy Stone Wall
3D Asset · Wall
4K · ID: ukhqdfvga
```

```text
Brick Debris
3D Asset · Brick / Debris
4K
```

Do not show technical values such as:

```text
normal · 3d
specular · 3d
```

## Display rules

- name is strongest;
- type/category is secondary;
- ID/resolution is tertiary;
- long values use ellipsis;
- full values remain available through popup or tooltip;
- cards keep consistent height and thumbnail alignment.

## Selected state

Single click must visibly select the card.

Requirements:

- clear selected border/background in dark theme;
- keyboard focus distinguishable from hover;
- selection remains after pointer leaves;
- selection is preserved after sort/filter refresh by stable identity where practical;
- selection clears safely if the asset disappears.

---

# Part D — Hover popup redesign

Retain the existing hover popup. Do not add a permanent inspector in this ticket.

Recommended structure:

```text
Brick Debris
ID: tfcnacapa

3D Asset · Brick / Debris
Resolution: 4K (4096 × 4096)
Physical size: 1 × 1 m
Texel density: 4264 px/m

Biome: Mediterranean Forest
Region: Asia

Tags
brick, debris, detritus, rubble, damaged

Folder
J:\Projects\...
```

Only render meaningful rows.

## Popup behavior

- delayed opening remains approximately 350–500 ms;
- stale delayed popups are cancelled during rapid pointer movement;
- popup does not steal keyboard focus;
- popup stays inside screen/work-area bounds;
- long paths wrap or ellipsize predictably;
- reasonable maximum width and height;
- no duplicated queued popup instances;
- no raw exceptions in UI.

## Copy actions

Add context actions for:

- Copy asset ID
- Copy asset folder path
- Copy JSON path

Prefer a card context menu rather than several visible popup buttons.

---

# Part E — Sorting

Add a sort selector near the search field.

Required modes:

```text
Name A–Z
Name Z–A
Type A–Z
Resolution high to low
Resolution low to high
Recently modified
Oldest modified
Asset ID A–Z
```

Rules:

- default: `Name A–Z`;
- current sort persists in user settings;
- sort applies to current folder/search result;
- deterministic tie-breaker: Asset ID, then folder path;
- missing values sort last;
- sorting does not block UI;
- selected asset remains selected after sort when still present;
- direction is visible.

Use an enum or dedicated sort descriptor. Do not use display strings as logic keys.

---

# Part F — Folder tree counts

Show descendant-inclusive counts:

```text
3D (4)
3dplant (3)
atlas (2)
bush (1)
```

Rules:

- count includes folder and descendants;
- root count equals all indexed assets;
- counts update after successful rescan;
- counts come from the index/query model, not synchronous filesystem traversal;
- existing folder filtering behavior remains unchanged.

Do not add virtual type/biome/region trees in this ticket.

---

# Part G — Context menu and keyboard navigation

## Context menu

Add:

```text
Open preview
Open folder
Copy asset ID
Copy folder path
Copy JSON path
```

Rules:

- disable unavailable commands;
- `Open folder` opens Explorer safely;
- no shell command string concatenation;
- failures produce concise UI message and structured log.

## Keyboard behavior

Required:

- arrow keys move selection through cards;
- `Enter` opens preview;
- `Esc` closes preview;
- `Ctrl+C` copies selected asset folder path or another clearly documented default;
- do not override normal copy inside search text box;
- focus remains usable after closing preview.

---

# Search interaction

Existing search must continue to work.

Search normalized fields:

- name;
- ID;
- asset type;
- categories;
- tags;
- biome;
- region.

Search is case-insensitive.

Search, folder filter, and sorting must compose correctly.

Do not add advanced query syntax.

---

# Architecture requirements

Keep normalization out of XAML.

Recommended flow:

```text
Raw Megascans JSON
        ↓
Parser DTOs
        ↓
Metadata normalizer
        ↓
Domain/index model
        ↓
Query/read model
        ↓
View model formatting
```

Responsibilities:

- parser reads schema variants safely;
- normalizer creates canonical values;
- persistence stores canonical/queryable values;
- view model formats normalized values;
- XAML only lays out and styles.

Do not place parsing rules in converters.

Avoid `Dictionary<string, object>` as the primary metadata model.

Complex methods must contain concise comments explaining:

- normalization precedence;
- resolution parsing;
- index upgrade behavior;
- popup cancellation/lifetime;
- deterministic sorting;
- selection preservation.

---

# Testing requirements

## Metadata normalization tests

Cover:

- `semanticTags.asset_type = atlas`;
- `semanticTags.asset_type = 3d`;
- category fallback when semantic type missing;
- `normal` never becoming asset type;
- `specular` never becoming asset type;
- empty and placeholder strings;
- leading whitespace in tags;
- duplicate categories/tags;
- exact ID preservation;
- unknown fallback.

## Resolution tests

Cover:

```text
4096x4096
4096 × 4096
2048x2048
4096x2048
4K
1024
malformed
missing
```

Verify maximum selection across components.

Verify display:

```text
4K (4096 × 4096)
4096 × 2048
—
```

## Physical-size tests

Cover:

```text
0.96x0.96
1x1
0.96x0.96 m
malformed
missing
```

## Existing-index tests

Cover:

- old normalization version detected;
- rescan-required state;
- old index remains readable;
- corrected scan commits atomically;
- cancellation preserves previous index.

## Sorting tests

Cover all required modes.

Verify:

- deterministic tie-breakers;
- missing values last;
- folder + search + sort composition;
- stable selected identity.

## View-model tests

Cover:

- card secondary text;
- missing popup rows omitted;
- explicit ID label;
- context-command enablement;
- copy commands;
- selected state;
- Enter/Esc behavior;
- persisted sort preference.

## Folder-count tests

Cover:

- direct counts;
- descendant-inclusive counts;
- root total;
- updates after rescan.

## Regression tests

All existing scanner, SQLite, settings, preview, cancellation, and malformed-JSON tests must remain passing.

Use only sanitized small fixtures.

---

# Manual validation checklist

1. Run against the existing test Megascans library.
2. Rescan without manually deleting SQLite.
3. Confirm `normal` and `specular` no longer appear as asset types.
4. Confirm `4096x4096` displays as `4K (4096 × 4096)`.
5. Confirm `undefined` rows are absent.
6. Confirm `ID:` is explicit.
7. Confirm card hierarchy is readable.
8. Hover rapidly across several cards.
9. Verify no stale popup or flicker.
10. Verify long-path handling.
11. Verify selected state remains after pointer exit.
12. Test every sort mode.
13. Confirm sort composes with folder and search.
14. Confirm selected asset remains selected after sorting.
15. Verify folder counts.
16. Open preview with `Enter`.
17. Close preview with `Esc`.
18. Test context menu.
19. Copy ID and paths.
20. Open folder in Explorer.
21. Restart and verify sort preference persists.
22. Cancel corrective rescan and verify previous index remains usable.

---

# Acceptance criteria

- [ ] Asset type is never derived from texture component types.
- [ ] `normal`, `specular`, `roughness`, and similar values no longer appear as asset types.
- [ ] Asset-type precedence is deterministic and documented.
- [ ] Maximum resolution parses correctly.
- [ ] Non-square resolution is preserved.
- [ ] Physical size is formatted consistently.
- [ ] Missing values do not display as `undefined`, `null`, empty strings, or accidental placeholders.
- [ ] Asset ID is explicitly labeled.
- [ ] Categories and tags are normalized without meaningful data loss.
- [ ] Existing users do not need to delete SQLite manually.
- [ ] Failed/cancelled corrective scans preserve the prior usable index.
- [ ] Card hierarchy shows name, useful type/category, and compact resolution/ID.
- [ ] Selected card has a clear persistent state.
- [ ] Popup uses normalized metadata and omits unavailable optional rows.
- [ ] Popup remains stable during rapid pointer movement.
- [ ] Required sorting modes are implemented and persisted.
- [ ] Sorting composes with search and folder filter.
- [ ] Folder tree displays descendant-inclusive counts.
- [ ] Context menu supports preview, Explorer, and copy actions.
- [ ] Keyboard navigation works.
- [ ] Existing functionality remains intact.
- [ ] Tests pass.
- [ ] Release build succeeds.
- [ ] CRG is updated and impact reviewed.
- [ ] Graphify is refreshed only if justified.
- [ ] Investigation, plan, and implementation report are produced.

---

# Explicit non-goals

Do not implement:

- Unreal Engine import;
- FBX inspection;
- LOD detection;
- Billboard detection;
- texture completeness analysis;
- material creation;
- virtual trees by biome/type/region;
- permanent right-side inspector;
- editable metadata;
- file rename/move/delete;
- thumbnail regeneration;
- visual similarity;
- botanical identification;
- advanced query syntax;
- multi-select batch operations;
- installer or auto-update.

---

# Required deliverables

```text
Task/SCAN-2/investigation.md
Task/SCAN-2/implementation-plan.md
Task/SCAN-2/implementation-report.md
```

Update when applicable:

```text
README.md
Docs/architecture.md
Docs/index-format.md
```

Add or update tests in existing test projects.

---

# Validation

Discover and use repository-supported commands.

At minimum run the equivalent of:

```powershell
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release --no-build
```

Also run configured formatter/analyzer commands.

Record:

- exact command;
- working directory;
- result;
- test count;
- skipped validation;
- remaining risks.

Do not claim tests or manual checks that were not executed.

---

# Definition of done

ScanVault displays correct normalized Megascans metadata, presents readable cards and hover details, supports deterministic sorting and practical context actions, and upgrades existing indexed data without requiring manual database deletion or risking loss of the previous usable index.
