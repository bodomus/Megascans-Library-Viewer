# MLV-4 — Investigation

## Identity and workflow

- YouTrack: `MLV-4` (`Metadata-normalization-and-catalog-navigation`).
- Source specification identity: `SCAN-2`; user confirmed `SCAN-2` maps to `MLV-4` for this project.
- Workflow level: 2, structural change across Core, Infrastructure, App, persistence, settings, and tests.
- Repository root: `J:/Projects/UE_Projects/Megascans Library Viewer`.
- Branch: `master`.
- Initial commit: `4799f9a`.
- Initial working tree: clean.
- The real library under `tests/Megascan/` is read-only validation data and must not be changed or used by deterministic automated tests.

## Graph evidence

Graphify query:

```powershell
graphify query "Megascans metadata parser asset type resolution physical size tags SQLite schema settings sorting filtering folder tree asset cards selection context menu keyboard navigation" --budget 5000 --graph graphify-out\graph.json
```

The graph identified the expected path: `MegascansMetadataParser` and `JsonElementSearch` feed `AssetSummary`; `SqliteAssetIndex` persists it; `MainViewModel`, `AssetCardViewModel`, `MainWindow`, `AssetFiltering`, settings, and existing tests consume it.

CRG was refreshed with:

```powershell
$env:PYTHONUTF8='1'
code-review-graph update --base HEAD~1 --repo . --brief
code-review-graph status --repo .
```

Pre-change status: 294 nodes, 566 edges, 50 C# files, branch `master`, commit `4799f9a`.

## Required findings

### Asset type

`MegascansMetadataParser.ParseAsync` currently calls the recursive helper:

```csharp
JsonElementSearch.FindString(root, "assetType", "type")
```

`TryFind` descends into every object and array. When the root lacks `assetType`, the generic name `type` can resolve to `components[].type` or a map entry. This is how `normal`, `specular`, and similar texture-component identities reach the asset card.

Real fixtures instead expose canonical candidates such as `semanticTags.asset_type` (`3D asset`, `3D plant`, `decal`, `brush`) and root categories (`3d`, `3dplant`, `atlas`, `brush`). Asset-type extraction therefore needs explicit schema paths and deterministic normalization, not an unrestricted recursive `type` search.

### Resolution

`JsonElementSearch.FindResolution` takes the first recursive property named `resolution`, removes every non-digit character, and parses the remainder. Thus `4096x4096` becomes `40964096`, while the first match can also be a small preview resolution instead of an asset map.

Real fixtures contain map/component resolution collections at several sizes plus `semanticTags.resolution`. Resolution must be parsed structurally and selected from map/component candidates. Non-square dimensions must remain intact.

### Existing persisted data

SQLite schema v1 persists `asset_type`, `physical_size`, scalar `max_resolution`, optional strings, categories JSON, and tags JSON exactly as parsed. Incorrect normalized data can therefore already be stored and reloaded at startup.

There is no parser/normalization version marker. The application cannot distinguish old normalized data from current data, and the schema cannot represent non-square resolution.

### Upgrade requirement

A schema migration and normalization marker are required:

- schema v2 adds structured resolution width/height and optional raw asset type;
- migration preserves v1 rows and keeps them readable;
- old populated indexes receive an outdated normalization marker;
- startup reports that an explicit corrective Rescan is required;
- only a successful `ReplaceLibraryAsync` transaction advances the normalization marker;
- cancellation or failure rolls back both asset replacement and marker advancement.

No manual database deletion and no automatic scan are required.

### Optional strings, tags, and display ownership

Recursive string extraction currently returns literal values such as `undefined`, `null`, `N/A`, and `unknown`. `AssetCardViewModel.Display` only checks whitespace, so placeholders become visible.

Tag group identity already exists in `AssetTagKind`, but current parsing can flatten the whole `semanticTags` object incorrectly, and `TagNormalizer` sorts values rather than preserving stable source order. Normalization belongs in Core policy plus parser orchestration; human-readable formatting belongs in the App view model; XAML remains layout/style only.

### Filtering, sorting, selection, and keyboard behavior

- SQLite returns name-sorted rows, then `MainViewModel` filters and recreates cards in memory.
- Search currently covers only name, ID, and asset type.
- There is no explicit sort model or persisted sort preference.
- `ListBox` has implicit selection mechanics, but selection is not bound to the view model or restored after refresh.
- A `Button` fills each card, so a click opens preview instead of acting as a persistent catalog selection.
- Arrow navigation is available only incidentally through `ListBox`; Enter and scoped Ctrl+C are not implemented.
- Escape already closes preview.

### Hover behavior

Each card owns one popup with a 425 ms delayed open. `BeginHoverAsync`, `EndHover`, recycling cleanup, and cancellation already prevent stale delayed popups. This lifecycle should be preserved while optional rows and constrained layout are redesigned.

## Ownership and impact

- Direct: Core metadata models/policies, parser, SQLite migration/read-write path, settings, App view models/services/XAML, tests, architecture/index documentation.
- Adjacent: scan transaction and duplicate handling must remain unchanged; preview/image cancellation must remain intact.
- Not affected: filesystem traversal policy, source assets, image cache ownership, duplicate winner policy, Unreal integration.

## Safety conclusions

- No parsing or filesystem work moves to the dispatcher.
- Sorting/filtering stays in memory over the already-loaded index and is deterministic.
- Explorer launch uses `ProcessStartInfo.ArgumentList`, never a concatenated shell command.
- Clipboard and focus behavior remain App-layer concerns.
- The existing successful full-scan transaction remains the only place that removes stale rows or certifies new normalized data.

