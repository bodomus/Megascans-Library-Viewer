# MLV-5 — Implementation plan

## Objective

Introduce one reproducible application version source, deterministic SDK selection, visible/runtime build metadata, and Windows GitHub Actions validation without release publishing or tracked-file mutation.

## Steps

1. Centralize version properties in `Directory.Build.props`:
   - `VersionPrefix=0.2.0`;
   - local default suffix `dev`;
   - derived `Version`, assembly, file, and informational versions;
   - optional build-time `CommitSha`;
   - assembly metadata for product version, commit, and configuration;
   - retain deterministic and CI build settings.
2. Update `global.json` to the already validated stable SDK `10.0.302`, `latestPatch`, prereleases disabled.
3. Add immutable `ApplicationBuildInfo` in App:
   - read assembly attributes/metadata;
   - extract or shorten commit metadata when available;
   - provide graceful missing metadata fallbacks;
   - capture runtime, OS, and architecture without runtime Git/process work.
4. Register the helper in `App.xaml.cs`, inject it into `MainViewModel`, bind `MainWindow.Title`, and expand the structured startup log.
5. Add `.github/workflows/ci.yml`:
   - push and pull-request triggers;
   - `windows-latest`;
   - least-privilege contents read;
   - concurrency cancellation;
   - checkout v6 and setup-dotnet v5 using `global.json`;
   - restore/build/test/format/whitespace validation;
   - `ci.<run_number>` suffix and short SHA injection at build time;
   - TRX upload on failure via upload-artifact v7;
   - no cache unless needed, no secrets, no publishing.
6. Add/update App tests for metadata/fallback/title and adjust constructor fixtures.
7. Update `README.md`, `Docs/architecture.md`, and add `Docs/versioning-and-ci.md`, including branch-protection guidance and explicit non-publishing policy.
8. Validate generated assembly metadata, YAML syntax where locally possible, targeted tests, formatter, whitespace, and full Release restore/build/test.
9. Update CRG, inspect changed symbols/blast radius, and refresh Graphify only if important architectural relationships changed.
10. Create implementation/review reports, attach `Tickets/MLV-5.md`, commit to `master`, and update YouTrack fields/comment.

## Acceptance mapping

- Authoritative version and no duplication: steps 1 and 6.
- Stable SDK line: step 2.
- UI and startup metadata: steps 3, 4, and 6.
- CI coverage and metadata injection: step 5.
- Branch protection and version semantics documentation: step 7.
- Build/test/format/whitespace and graph evidence: steps 8 and 9.

## Explicit non-goals

No release workflow, GitHub Release, publish/package, installer, code signing, auto-update, branch-protection API call, diagnostics window, or cosmetic redesign.
