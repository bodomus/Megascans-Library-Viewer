# MLV-2 — Investigation

## Workflow

- Level: 1, narrow bugfix in the known Infrastructure parsing component.
- Repository root: `J:/Projects/UE_Projects/Megascans Library Viewer`.
- Branch: `master`.
- Initial commit: `fe76e2a`.
- Initial working tree: only untracked user fixture directory `tests/Megascan/`; it is preserved unchanged.

## Graph-assisted preflight

- Graphify was queried for JSON parsing, metadata, preview resolution, and path relationships. It identified `MegascansMetadataParser.ParseAsync`, `PreviewPathResolver.Resolve`, and `JsonElementSearch` as the relevant production flow.
- The Graphify result was validated directly against production source and tests.
- CRG status before the change: 283 nodes, 543 edges, 49 files, head `fe76e2a`.
- CRG did not link the static resolver call and related tests reliably; direct source inspection is authoritative for this change.

## Reproduction and root cause

The production `MegascansMetadataParser` was run against every JSON file below `tests/Megascan`.

- JSON files: 10.
- Successful parses before the fix: 0.
- `InvalidOperationException`: 10.

`PreviewPathResolver.ReadMetadataCandidates` called `JsonElement.TryGetInt32` for every property named `resolution` without first checking its `ValueKind`. Real Megascans metadata contains both numeric values and strings such as `"1024x1024"`. `TryGetInt32` throws when invoked on a string element, and the exception aborts the scan.

The existing representative parser test contained numeric resolutions only, which left the string schema variant uncovered.

## Scope and impact

- Owning project: `ScanVault.Infrastructure`.
- Call flow: scanner → `MegascansMetadataParser.ParseAsync` → `PreviewPathResolver.Resolve` → candidate selector.
- `PreviewCandidateSelector` orders candidates by role, then descending parsed resolution, then normalized path.
- No WPF/UI-thread, cancellation, SQLite, transaction, or image-lifetime behavior changes.
- User assets and `tests/Megascan` are read-only diagnostic input and are not committed.

## Chosen behavior

- Positive JSON integers remain supported.
- Positive scalar numeric strings are accepted as a harmless schema variation.
- `width x height` strings accept `x`, `X`, or `×`; the larger dimension is used for ordering.
- Empty, malformed, non-positive, overflowing, and unrelated JSON values are treated as missing resolution.
- The parser does not add a broad `InvalidOperationException` catch; the invalid type assumption is fixed at its source.
