using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Core.Tests;

public sealed class UnrealReadinessPolicyTests
{
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReadyThreeDAssetRequiresMeshAndPrimaryMaps()
    {
        var evaluation = Evaluate("3D Asset",
            "Var1/Var1_LOD0.fbx",
            "Var1/Var1_LOD1.fbx",
            "asset_4K_Albedo.jpg",
            "asset_4K_Normal.jpg",
            "asset_4K_Roughness.jpg",
            "asset_4K_Displacement.exr");

        Assert.Equal(UnrealReadinessStatus.Ready, evaluation.Status);
        Assert.Equal(UnrealReadinessPolicy.CurrentRuleVersion, evaluation.ReadinessRuleVersion);
        Assert.Contains(evaluation.Reasons, static reason => reason.RuleCode == UnrealReadinessRuleCode.UeReady && reason.Code == "UE_READY");
    }

    [Fact]
    public void MissingMeshBlocksThreeDAsset()
    {
        var evaluation = Evaluate("3D Asset",
            "asset_4K_Albedo.jpg",
            "asset_4K_Normal.jpg",
            "asset_4K_Roughness.jpg");

        Assert.Equal(UnrealReadinessStatus.NotReady, evaluation.Status);
        Assert.Contains(evaluation.Reasons, static reason => reason.RuleCode == UnrealReadinessRuleCode.UeMissingMesh && reason.Severity == UnrealReadinessSeverity.Blocking);
    }

    [Fact]
    public void MissingAlbedoBlocksSurface()
    {
        var evaluation = Evaluate("Surface",
            "asset_4K_Normal.jpg",
            "asset_4K_Roughness.jpg");

        Assert.Equal(UnrealReadinessStatus.NotReady, evaluation.Status);
        Assert.Contains(evaluation.Reasons, static reason => reason.RuleCode == UnrealReadinessRuleCode.UeMissingAlbedo);
    }

    [Fact]
    public void MissingNormalAndSingleLodAreWarnings()
    {
        var evaluation = Evaluate("3D Asset",
            "Var1/Var1_LOD0.fbx",
            "asset_4K_Albedo.jpg",
            "asset_4K_Roughness.jpg",
            "asset_4K_Displacement.exr");

        Assert.Equal(UnrealReadinessStatus.ReadyWithWarnings, evaluation.Status);
        Assert.Contains(evaluation.Reasons, static reason => reason.RuleCode == UnrealReadinessRuleCode.UeMissingNormal);
        Assert.Contains(evaluation.Reasons, static reason => reason.RuleCode == UnrealReadinessRuleCode.UeNoLods);
    }

    [Fact]
    public void IncompleteLodChainIsWarning()
    {
        var evaluation = Evaluate("3D Asset",
            "Var1/Var1_LOD0.fbx",
            "Var1/Var1_LOD2.fbx",
            "asset_4K_Albedo.jpg",
            "asset_4K_Normal.jpg",
            "asset_4K_Roughness.jpg",
            "asset_4K_Displacement.exr");

        Assert.Equal(UnrealReadinessStatus.ReadyWithWarnings, evaluation.Status);
        Assert.Contains(evaluation.Reasons, static reason => reason.RuleCode == UnrealReadinessRuleCode.UeIncompleteLodChain);
    }

    [Fact]
    public void AtlasMissingOpacityIsNotReady()
    {
        var evaluation = Evaluate("Atlas",
            "Textures/Atlas/asset_4K_Albedo.jpg",
            "Textures/Atlas/asset_4K_Normal.jpg",
            "Textures/Atlas/asset_4K_Roughness.jpg");

        Assert.Equal(UnrealReadinessStatus.NotReady, evaluation.Status);
        Assert.Contains(evaluation.Reasons, static reason => reason.RuleCode == UnrealReadinessRuleCode.UeMissingOpacity);
    }

    [Fact]
    public void UnknownTypeNeverReceivesReady()
    {
        var evaluation = Evaluate("Mystery",
            "asset_4K_Albedo.jpg",
            "asset_4K_Normal.jpg");

        Assert.Equal(UnrealReadinessStatus.Unknown, evaluation.Status);
        Assert.Contains(evaluation.Reasons, static reason => reason.RuleCode == UnrealReadinessRuleCode.UeUnknownAssetType);
    }

    [Fact]
    public void AmbiguousInventoryDoesNotReceiveFalseReady()
    {
        var evaluation = Evaluate("3D Asset",
            "A/Var1_LOD0.fbx",
            "B/var1_lod0.FBX",
            "asset_4K_Albedo.jpg",
            "asset_4K_Normal.jpg",
            "asset_4K_Roughness.jpg");

        Assert.Equal(UnrealReadinessStatus.Unknown, evaluation.Status);
        Assert.Contains(evaluation.Reasons, static reason => reason.RuleCode == UnrealReadinessRuleCode.UeAmbiguousInventory);
        Assert.Contains(evaluation.Reasons, static reason => reason.RuleCode == UnrealReadinessRuleCode.UeDuplicateLogicalFiles);
    }

    [Fact]
    public void MixedResolutionsAreWarningAndFileOrderIsDeterministic()
    {
        var first = Evaluate("Surface",
            "asset_2K_Normal.jpg",
            "asset_4K_Albedo.jpg",
            "asset_4K_Roughness.jpg",
            "asset_4K_Displacement.exr");
        var second = Evaluate("Surface",
            "asset_4K_Displacement.exr",
            "asset_4K_Roughness.jpg",
            "asset_4K_Albedo.jpg",
            "asset_2K_Normal.jpg");

        Assert.Equal(UnrealReadinessStatus.ReadyWithWarnings, first.Status);
        Assert.Contains(first.Reasons, static reason => reason.RuleCode == UnrealReadinessRuleCode.UeMixedResolutions);
        Assert.Equal(first.Reasons.Select(static reason => reason.Code), second.Reasons.Select(static reason => reason.Code));
    }

    [Fact]
    public void SummaryCountsCurrentAndStaleEvaluations()
    {
        var ready = Asset("3D Asset",
            "Var1/Var1_LOD0.fbx",
            "Var1/Var1_LOD1.fbx",
            "asset_4K_Albedo.jpg",
            "asset_4K_Normal.jpg",
            "asset_4K_Roughness.jpg",
            "asset_4K_Displacement.exr");
        var stale = ready with { UnrealReadiness = UnrealReadinessEvaluation.Unknown };

        var summary = UnrealReadinessPolicy.Summarize([ready, stale]);

        Assert.Equal(1, summary.ReadyCount);
        Assert.Equal(1, summary.UnknownCount);
        Assert.Equal(1, summary.RequiresRecalculationCount);
    }

    private static UnrealReadinessEvaluation Evaluate(string assetType, params string[] relativePaths) =>
        Asset(assetType, relativePaths).UnrealReadiness;

    private static AssetSummary Asset(string assetType, params string[] relativePaths)
    {
        var root = Path.Combine(Path.GetTempPath(), "readiness-fixture");
        var files = relativePaths.Select(path => new AssetContentFileCandidate(Path.Combine(root, path), path)).ToArray();
        var inventory = AssetContentAnalyzer.Analyze(assetType, files);
        var asset = new AssetSummary(
            "asset",
            "Asset",
            assetType,
            root,
            Path.Combine(root, "asset.json"),
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
            EvaluatedAt)
        {
            Content = inventory
        };
        return UnrealReadinessPolicy.EnsureCurrent(asset, EvaluatedAt);
    }
}
