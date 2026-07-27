# Review MLV-2

## Result

Bug fixed. Scanning no longer throws when legacy Megascans metadata contains string `resolution` values such as `"1024x1024"`.

## Root cause

`PreviewPathResolver.ReadMetadataCandidates` invoked `JsonElement.TryGetInt32` without checking `JsonValueKind`. The real test library uses both numeric and string representations, and the string representation caused `InvalidOperationException` before the asset could be indexed.

## Implemented behavior

- positive JSON number → used directly;
- positive numeric string → parsed safely;
- `width x height` string → maximum dimension used for deterministic preview ordering;
- malformed, empty, non-positive, overflowing, or unsupported value → treated as missing resolution;
- no broad exception suppression was added.

## Files changed

- `src/ScanVault.Infrastructure/Parsing/PreviewPathResolver.cs`
- `tests/ScanVault.Infrastructure.Tests/MegascansMetadataParserTests.cs`
- `tests/ScanVault.Infrastructure.Tests/PreviewPathResolverTests.cs`
- `Tickets/MLV-2.md`
- `Task/MLV-2/investigation.md`
- `Task/MLV-2/implementation-plan.md`
- `Task/MLV-2/implementation-report.md`
- `review/review-MLV-2.md`

## Verification

- Targeted Infrastructure tests: 13/13 passed.
- Full Release build: passed, 0 warnings, 0 errors.
- Full solution tests: 24/24 passed.
- Production parser over `tests/Megascan`: 10/10 successful.
- Production scan pipeline over `tests/Megascan`: 10 assets indexed/committed, 0 malformed, 0 unrelated, 0 inaccessible directories.
- Duplicate groups: 0; therefore no winner/skipped copy paths exist for this dataset.
- Post-change CRG: 287 nodes, 551 edges, 49 files; risk 0.40 and no affected flow.
- Graphify refresh was unnecessary because the fix does not alter architecture.

## Data safety

The untracked user fixture directory `tests/Megascan/` was read only. No source asset, image, or metadata file was modified, deleted, moved, or added to Git.

## Not manually verified

The WPF window was not launched for a manual UI click-through. The failing production parser and the complete scan-service path were exercised directly against all supplied metadata.
