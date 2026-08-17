using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Core.Tests;

public sealed class UnrealImportPackagePolicyTests
{
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

    // Unit test: ready surface assets produce a versioned package with semantic texture roles.
    [Fact]
    public void ReadySurfacePackageContainsSchemaDestinationAndTextureRoles()
    {
        var package = CreatePackage("Surface",
            "surface_4K_Albedo.jpg",
            "surface_4K_Normal.jpg",
            "surface_4K_Gloss.jpg",
            "surface_4K_Displacement.exr");

        Assert.Equal(UnrealImportPackageSchema.CurrentVersion, package.SchemaVersion);
        Assert.Equal(UnrealReadinessStatus.Ready, package.Readiness.Status);
        Assert.Equal("/Game/Megascans/Surfaces/Forest_Rock", package.Destination.ContentPath);
        Assert.Contains(package.Textures, static texture => texture.Role == UnrealImportSemanticRole.BaseColor && texture.MapType == TextureMapType.Albedo);
        Assert.Contains(package.Textures, static texture => texture.Role == UnrealImportSemanticRole.Roughness && texture.MapType == TextureMapType.Gloss);
        Assert.False(package.Validation.HasErrors);
    }

    // Unit test: 3D asset mesh variant and LOD identity are preserved in deterministic order.
    [Fact]
    public void ThreeDAssetPackagePreservesPrimaryVariantAndLodOrder()
    {
        var package = CreatePackage("3D Asset",
            "Var2/asset_LOD1.fbx",
            "Var1/asset_LOD1.fbx",
            "Var1/asset_LOD0.fbx",
            "asset_4K_Albedo.jpg",
            "asset_4K_Normal.jpg",
            "asset_4K_Roughness.jpg");

        Assert.Equal("Var1", package.Mesh?.PrimaryVariant);
        Assert.Equal([0, 1], package.Mesh!.Lods.Select(static lod => lod.Lod));
        Assert.All(package.Mesh.Lods, static lod => Assert.Equal("Var1", lod.Variant));
    }

    // Unit test: blocked or stale readiness evaluations create validation errors.
    [Fact]
    public void NotReadyAndStaleReadinessAreBlocked()
    {
        var notReady = CreatePackage("Surface", "surface_4K_Normal.jpg");
        var staleAsset = CreateAsset("Surface", "surface_4K_Albedo.jpg", "surface_4K_Normal.jpg") with
        {
            UnrealReadiness = UnrealReadinessEvaluation.Unknown
        };
        var stale = UnrealImportPackagePolicy.Create(Request(staleAsset));

        Assert.Contains(notReady.Validation.Issues, static issue => issue.Code == UnrealImportPackageIssueCode.ReadinessBlocked);
        Assert.Contains(stale.Validation.Issues, static issue => issue.Code == UnrealImportPackageIssueCode.ReadinessStale);
    }

    // Unit test: sanitization and Material Instance naming are deterministic and preserve source names separately.
    [Fact]
    public void SanitizationKeepsOriginalNameAndCreatesSafeMaterialInstanceName()
    {
        var asset = CreateAsset("Surface", "surface_4K_Albedo.jpg") with { Name = "  Rock/水 01!! " };
        var package = UnrealImportPackagePolicy.Create(Request(asset));

        Assert.Equal("Rock_水_01", package.Destination.AssetBaseName);
        Assert.Equal("  Rock/水 01!! ", package.Destination.OriginalAssetName);
        Assert.Equal("MI_Rock_水_01", package.Material.MaterialInstanceName);
    }

    // Unit test: package identity excludes generator timestamp but changes for destination and profile changes.
    [Fact]
    public void PackageIdentityIgnoresTimestampButTracksSemanticInputs()
    {
        var asset = CreateAsset("Surface", "surface_4K_Albedo.jpg", "surface_4K_Normal.jpg");
        var first = UnrealImportPackagePolicy.Create(Request(asset, generatedAt: GeneratedAt));
        var later = UnrealImportPackagePolicy.Create(Request(asset, generatedAt: GeneratedAt.AddHours(1)));
        var otherDestination = UnrealImportPackagePolicy.Create(Request(asset, destination: "/Game/Other"));
        var otherProfile = UnrealImportPackagePolicy.Create(Request(asset, profile: Profile() with { Id = "custom" }));

        Assert.Equal(first.PackageId, later.PackageId);
        Assert.NotEqual(first.PackageId, otherDestination.PackageId);
        Assert.NotEqual(first.PackageId, otherProfile.PackageId);
    }

    // Unit test: package identity changes when material contract fields change under the same profile ID.
    [Fact]
    public void PackageIdentityTracksMaterialContractFields()
    {
        var asset = CreateAsset("Surface", "surface_4K_Albedo.jpg", "surface_4K_Normal.jpg");
        var profile = Profile();
        var baseline = UnrealImportPackagePolicy.Create(Request(asset, profile: profile));
        var changedMaster = UnrealImportPackagePolicy.Create(Request(asset, profile: profile with { MasterMaterialPath = "/Game/Materials/M_New" }));
        var changedPrefix = UnrealImportPackagePolicy.Create(Request(asset, profile: profile with { MaterialInstancePrefix = "CustomMI_" }));
        var changedMapping = UnrealImportPackagePolicy.Create(Request(asset, profile: profile with
        {
            TextureParameterMappings = profile.TextureParameterMappings
                .Select(static mapping => mapping.Role == UnrealImportSemanticRole.Normal
                    ? mapping with { ParameterName = "MyNormalParameter" }
                    : mapping)
                .ToArray()
        }));

        Assert.NotEqual(baseline.PackageId, changedMaster.PackageId);
        Assert.NotEqual(baseline.PackageId, changedPrefix.PackageId);
        Assert.NotEqual(baseline.PackageId, changedMapping.PackageId);
    }

    // Unit test: package identity tracks source revision, texture semantic fields, and LOD fields.
    [Fact]
    public void PackageIdentityTracksSourceTextureAndLodSemanticFields()
    {
        var asset = CreateAsset("3D Asset",
            "Var1/asset_LOD0.fbx",
            "Var1/asset_LOD1.fbx",
            "asset_4K_Albedo.jpg",
            "asset_4K_Normal.jpg",
            "asset_4K_Roughness.jpg");
        var baseline = UnrealImportPackagePolicy.Create(Request(asset));
        var changedSource = UnrealImportPackagePolicy.Create(Request(asset with { LastWriteTimeUtc = asset.LastWriteTimeUtc.AddMinutes(1) }));
        var changedTexture = baseline with
        {
            Textures = baseline.Textures
                .Select(static texture => texture.Role == UnrealImportSemanticRole.BaseColor
                    ? texture with { MapType = TextureMapType.Specular, Format = "PNG", Resolution = 2048 }
                    : texture)
                .ToArray()
        };
        var changedLod = baseline with
        {
            Mesh = baseline.Mesh! with
            {
                Lods = baseline.Mesh.Lods
                    .Select(static lod => lod.Lod == 1 ? lod with { SourcePath = lod.SourcePath + ".changed" } : lod)
                    .ToArray()
            }
        };

        Assert.NotEqual(baseline.PackageId, changedSource.PackageId);
        Assert.NotEqual(baseline.PackageId, UnrealImportPackagePolicy.ComputePackageId(changedTexture));
        Assert.NotEqual(baseline.PackageId, UnrealImportPackagePolicy.ComputePackageId(changedLod));
    }

    // Unit test: package identity is independent of equivalent mapping, texture, and LOD ordering.
    [Fact]
    public void PackageIdentityIgnoresEquivalentCollectionOrdering()
    {
        var package = CreatePackage("3D Asset",
            "Var1/asset_LOD1.fbx",
            "Var1/asset_LOD0.fbx",
            "asset_4K_Albedo.jpg",
            "asset_4K_Normal.jpg",
            "asset_4K_Roughness.jpg");
        var reordered = package with
        {
            Textures = package.Textures.Reverse().ToArray(),
            Material = package.Material with
            {
                TextureParameterMappings = package.Material.TextureParameterMappings.Reverse().ToArray()
            },
            Mesh = package.Mesh! with
            {
                Lods = package.Mesh.Lods.Reverse().ToArray()
            }
        };

        Assert.Equal(package.PackageId, UnrealImportPackagePolicy.ComputePackageId(reordered));
    }

    // Unit test: duplicate BaseColor candidates produce deterministic selection and ambiguity warning.
    [Fact]
    public void DuplicateBaseColorCandidatesWarnBeforeDeterministicSelection()
    {
        var first = CreatePackage("Surface",
            "A/surface_4K_Albedo.jpg",
            "B/surface_4K_Albedo.png",
            "surface_4K_Normal.jpg",
            "surface_4K_Roughness.jpg");
        var second = CreatePackage("Surface",
            "surface_4K_Roughness.jpg",
            "B/surface_4K_Albedo.png",
            "surface_4K_Normal.jpg",
            "A/surface_4K_Albedo.jpg");

        Assert.Equal(TextureMapType.Albedo, first.Textures.Single(static texture => texture.Role == UnrealImportSemanticRole.BaseColor).MapType);
        Assert.Contains(first.Validation.Issues, static issue => issue.Code == UnrealImportPackageIssueCode.AmbiguousTextureRole);
        Assert.Equal(first.Textures.Select(static texture => texture.SourcePath), second.Textures.Select(static texture => texture.SourcePath));
        Assert.Equal(first.PackageId, second.PackageId);
    }

    // Unit test: Roughness is preferred over Gloss while preserving ambiguity and source map type.
    [Fact]
    public void RoughnessAndGlossPreferNativeRoughnessAndWarn()
    {
        var package = CreatePackage("Surface",
            "surface_4K_Albedo.jpg",
            "surface_4K_Normal.jpg",
            "surface_4K_Gloss.jpg",
            "surface_4K_Roughness.jpg");
        var roughness = package.Textures.Single(static texture => texture.Role == UnrealImportSemanticRole.Roughness);

        Assert.Equal(TextureMapType.Roughness, roughness.MapType);
        Assert.Contains(package.Validation.Issues, static issue => issue.Code == UnrealImportPackageIssueCode.AmbiguousTextureRole);
    }

    // Unit test: single candidate per semantic role does not report false ambiguity.
    [Fact]
    public void SingleTextureCandidatePerRoleDoesNotWarnForAmbiguity()
    {
        var package = CreatePackage("Surface",
            "surface_4K_Albedo.jpg",
            "surface_4K_Normal.jpg",
            "surface_4K_Roughness.jpg");

        Assert.DoesNotContain(package.Validation.Issues, static issue => issue.Code == UnrealImportPackageIssueCode.AmbiguousTextureRole);
    }

    // Unit test: material profile compatibility only returns profiles matching the normalized asset type.
    [Fact]
    public void ProfileCompatibilityFiltersByAssetType()
    {
        var compatible = UnrealMaterialProfilePolicy.CompatibleProfiles(
            UnrealMaterialProfilePolicy.BuiltInProfiles,
            "3D Asset");

        Assert.Contains(compatible, static profile => profile.Id == "default-3d-asset");
        Assert.DoesNotContain(compatible, static profile => profile.Id == "default-surface");
    }

    private static UnrealImportPackage CreatePackage(string assetType, params string[] relativePaths) =>
        UnrealImportPackagePolicy.Create(Request(CreateAsset(assetType, relativePaths)));

    private static UnrealImportPackageRequest Request(
        AssetSummary asset,
        UnrealMaterialProfile? profile = null,
        string destination = "/Game/Megascans",
        DateTimeOffset? generatedAt = null) =>
        new(
            asset,
            profile ?? Profile(asset.AssetType),
            destination,
            profile?.DefaultOptions ?? Profile(asset.AssetType).DefaultOptions,
            "1.0.0",
            "abcdef1",
            generatedAt ?? GeneratedAt);

    private static UnrealMaterialProfile Profile(string assetType = "Surface") =>
        UnrealMaterialProfilePolicy.BuiltInProfiles.First(profile =>
            profile.AssetTypes.Contains(assetType, StringComparer.OrdinalIgnoreCase));

    private static AssetSummary CreateAsset(string assetType, params string[] relativePaths)
    {
        var root = Path.Combine(Path.GetTempPath(), "ue-package-fixture");
        var files = relativePaths.Select(path => new AssetContentFileCandidate(Path.Combine(root, path), path)).ToArray();
        var inventory = AssetContentAnalyzer.Analyze(assetType, files);
        var asset = new AssetSummary(
            "asset-id",
            "Forest Rock",
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
