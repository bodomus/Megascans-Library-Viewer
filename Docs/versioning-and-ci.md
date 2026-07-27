# Versioning and continuous integration

## Version policy

`Directory.Build.props` is the only authoritative product-version source for every project in the solution. The current product version is `0.2.0`. Project files must not duplicate it.

The generated versions have different purposes:

- `VersionPrefix` is the compact product version shown in the application title (`0.2.0`).
- `AssemblyVersion` is the CLR assembly identity (`0.2.0.0`).
- `FileVersion` is the Windows file version (`0.2.0.0`).
- `InformationalVersion` identifies a particular build. A local build is `0.2.0-dev`; CI produces `0.2.0-ci.<run>+<short-sha>`.

The commit is supplied to MSBuild through `CommitSha`. A local build does not invoke Git and records `unavailable` at runtime when no commit metadata was injected. The main-window title deliberately excludes the suffix and SHA.

To start a new product line, change `VersionPrefix` once in `Directory.Build.props`. Do not add versions to individual `.csproj` files and do not commit generated version files.

## SDK policy

`global.json` selects stable .NET SDK `10.0.302`, permits only later patches through `latestPatch`, and rejects preview SDKs. Developers and CI use this same policy. Install a compatible stable 10.0.3xx SDK before restoring the solution.

## Local validation

Run from the repository root:

```powershell
dotnet restore ScanVault.sln
dotnet build ScanVault.sln --configuration Release --no-restore
dotnet test ScanVault.sln --configuration Release --no-build
dotnet format ScanVault.sln --verify-no-changes --no-restore
git diff --check
```

Local builds require no Git metadata. To reproduce CI metadata without modifying tracked files:

```powershell
dotnet build ScanVault.sln --configuration Release --no-restore `
  -p:VersionSuffix=ci.42 `
  -p:CommitSha=0123456 `
  -p:ContinuousIntegrationBuild=true
```

## GitHub Actions CI

`.github/workflows/ci.yml` runs on every push and pull request on `windows-latest`. It checks out the repository with read-only contents permission, installs the SDK selected by `global.json`, injects `ci.<run>` and the short commit SHA at build time, restores, builds, tests, verifies formatting, and checks whitespace. Failed jobs retain available TRX diagnostics for seven days.

The workflow does not publish packages, create releases, use secrets, update version files, or commit changes.

## Recommended protection for `master`

Configure repository branch protection manually in GitHub:

1. Require pull requests before merging.
2. Require the `Windows build and test` status check.
3. Block merging when that check fails.
4. Prefer requiring the branch to be up to date before merge when the team workflow permits it.

MLV-5 documents these settings but intentionally does not change repository settings through an API.
