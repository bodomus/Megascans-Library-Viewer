using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Core.Tests;

public sealed class UnrealReadinessIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CommonInventoryFilterMatchesReadinessStatusAndReasons()
    {
        var ready = Asset("ready", "3D Asset",
            "Var1/Var1_LOD0.fbx",
            "Var1/Var1_LOD1.fbx",
            "asset_4K_Albedo.jpg",
            "asset_4K_Normal.jpg",
            "asset_4K_Roughness.jpg",
            "asset_4K_Displacement.exr");
        var missingMesh = Asset("missing", "3D Asset",
            "asset_4K_Albedo.jpg",
            "asset_4K_Normal.jpg",
            "asset_4K_Roughness.jpg");

        Assert.True(AssetFiltering.MatchesInventoryFilter(ready, AssetInventoryFilter.UnrealReady));
        Assert.True(AssetFiltering.MatchesInventoryFilter(missingMesh, AssetInventoryFilter.UnrealNotReady | AssetInventoryFilter.UnrealMissingMesh));
        Assert.False(AssetFiltering.MatchesInventoryFilter(ready, AssetInventoryFilter.UnrealMissingMesh));
    }

    [Fact]
    public void SmartCollectionBuiltInsUseReadinessCriteria()
    {
        var notReady = Asset("missing", "3D Asset",
            "asset_4K_Albedo.jpg",
            "asset_4K_Normal.jpg",
            "asset_4K_Roughness.jpg");
        var collection = Assert.Single(SmartCollectionPolicy.BuiltIns, static item => item.Id == "builtin-ue-missing-mesh");

        Assert.True(SmartCollectionPolicy.Matches(notReady, collection.Definition, null, null));
    }

    [Fact]
    public void ComparisonIncludesReadinessOverviewRows()
    {
        var left = Asset("left", "3D Asset",
            "Var1/Var1_LOD0.fbx",
            "Var1/Var1_LOD1.fbx",
            "asset_4K_Albedo.jpg",
            "asset_4K_Normal.jpg",
            "asset_4K_Roughness.jpg",
            "asset_4K_Displacement.exr");
        var right = Asset("right", "3D Asset", "asset_4K_Albedo.jpg");

        var snapshot = AssetComparisonPolicy.Compare(left, right);

        Assert.Contains(snapshot.Overview, static row => row.Key == "ue-readiness");
        Assert.Contains(snapshot.Overview, static row => row.Key == "ue-blocking-count");
        Assert.Contains(snapshot.Overview, static row => row.Key == "ue-readiness-reasons");
    }

    [Fact]
    public void CatalogExportIncludesReadinessColumns()
    {
        var asset = Asset("ready", "Surface",
            "asset_4K_Albedo.jpg",
            "asset_4K_Normal.jpg",
            "asset_4K_Roughness.jpg",
            "asset_4K_Displacement.exr");
        var document = ReportProfilePolicy.CreateDocument(new(
            ReportProfile.AssetCatalog,
            ReportFormat.Csv,
            ReportScope.EntireLibrary,
            Path.Combine(Path.GetTempPath(), "readiness.csv"),
            Path.GetTempPath(),
            IncludeAbsolutePaths: false,
            IncludeMetadata: false,
            PrettyJson: false,
            IncludeUnchangedScanItems: false,
            [],
            [asset],
            "all",
            "name",
            "test",
            "abcdef1",
            7,
            3), Now);

        var row = Assert.IsType<AssetCatalogRowDto>(Assert.Single(document.Rows));
        Assert.Equal("UE Ready", row.UnrealReadinessStatus);
        Assert.Equal(UnrealReadinessPolicy.CurrentRuleVersion, row.ReadinessRuleVersion);
        Assert.Contains("UE_READY", row.ReadinessReasons, StringComparison.Ordinal);
    }

    private static AssetSummary Asset(string id, string assetType, params string[] relativePaths)
    {
        var root = Path.Combine(Path.GetTempPath(), "readiness-integration-fixture", id);
        var files = relativePaths.Select(path => new AssetContentFileCandidate(Path.Combine(root, path), path)).ToArray();
        var inventory = AssetContentAnalyzer.Analyze(assetType, files);
        var asset = new AssetSummary(
            id,
            id,
            assetType,
            root,
            Path.Combine(root, $"{id}.json"),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            Now)
        {
            Content = inventory
        };
        return UnrealReadinessPolicy.EnsureCurrent(asset, Now);
    }
}
