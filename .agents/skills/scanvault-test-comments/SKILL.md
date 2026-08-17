---
name: scanvault-test-comments
description: Enforce ScanVault test-method classification comments. Use whenever Codex creates or edits test files in this repository, especially C# xUnit tests under tests/, to ensure every test method has a short preceding comment naming whether the test is unit, integration, regression, migration, UI, diagnostics, persistence, history, or another clear test kind.
---

# ScanVault Test Comments

When creating or editing test files in this repository, add a short comment immediately before every test method.

The comment must state what kind of test it is. Keep it concise and useful.

Examples:

```csharp
// Unit test: verifies deterministic UE readiness rule ordering.
[Fact]
public void ReadinessReasonsAreDeterministic()
{
}

// Integration test: verifies SQLite v6 to v7 migration preserves indexed assets.
[Fact]
public async Task VersionSixMigrationPreservesRows()
{
}

// Regression test: protects against stale current-version readiness being persisted.
[Fact]
public async Task ReplacementRecomputesReadinessFromChangedInventory()
{
}
```

Use a more specific label when it helps:

- `Unit test`
- `Integration test`
- `Regression test`
- `Migration test`
- `Persistence test`
- `History test`
- `Diagnostics test`
- `UI test`

If one method belongs to multiple categories, prefer the category that explains why the test exists, for example `Regression test` over `Integration test`.
