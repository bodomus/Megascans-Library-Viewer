using ScanVault.Core.Models;

namespace ScanVault.Core.Policies;

public static class UnrealReadinessPolicy
{
    public const int CurrentRuleVersion = 1;
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public static AssetSummary EnsureCurrent(AssetSummary asset, DateTimeOffset evaluatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return asset.UnrealReadiness.ReadinessRuleVersion == CurrentRuleVersion &&
               asset.UnrealReadiness.EvaluatedAtUtc is not null
            ? asset
            : asset with { UnrealReadiness = Evaluate(asset, evaluatedAtUtc) };
    }

    public static UnrealReadinessEvaluation Evaluate(AssetSummary asset, DateTimeOffset evaluatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(asset);

        var reasons = new List<UnrealReadinessReason>();
        var type = NormalizeType(asset);
        if (type == AssetReadinessType.Unknown)
        {
            reasons.Add(Info(UnrealReadinessRuleCode.UeUnknownAssetType,
                $"Asset type '{asset.AssetType}' is not recognized by the Unreal readiness rules.", null));
            return Create(UnrealReadinessStatus.Unknown, reasons, evaluatedAtUtc);
        }

        if (type == AssetReadinessType.Brush)
        {
            reasons.Add(Info(UnrealReadinessRuleCode.UeNotApplicable,
                "Brush assets are not evaluated for standalone Unreal Engine import readiness.", null));
            return Create(UnrealReadinessStatus.NotApplicable, reasons, evaluatedAtUtc);
        }

        AddInventoryIssues(asset, reasons);
        if (reasons.Any(static reason => reason.RuleCode == UnrealReadinessRuleCode.UeAmbiguousInventory))
        {
            return Create(UnrealReadinessStatus.Unknown, reasons, evaluatedAtUtc);
        }

        switch (type)
        {
            case AssetReadinessType.ThreeDAsset:
                EvaluateMeshAsset(asset, reasons, requiresOpacity: false, requiresBillboard: false);
                break;
            case AssetReadinessType.ThreeDPlant:
                EvaluateMeshAsset(asset, reasons, requiresOpacity: true, requiresBillboard: true);
                break;
            case AssetReadinessType.Surface:
                EvaluateTextureAsset(asset, reasons, [TextureSetKind.General, TextureSetKind.Unknown], requiresOpacity: false, requiresNormalBlocking: false);
                break;
            case AssetReadinessType.Atlas:
                EvaluateTextureAsset(asset, reasons, [TextureSetKind.Atlas], requiresOpacity: true, requiresNormalBlocking: false);
                break;
            case AssetReadinessType.Billboard:
                EvaluateTextureAsset(asset, reasons, [TextureSetKind.Billboard], requiresOpacity: true, requiresNormalBlocking: false);
                break;
            case AssetReadinessType.Decal:
                EvaluateTextureAsset(asset, reasons, [TextureSetKind.General, TextureSetKind.Unknown, TextureSetKind.Atlas], requiresOpacity: true, requiresNormalBlocking: true);
                break;
        }

        if (reasons.Count == 0)
        {
            reasons.Add(Info(UnrealReadinessRuleCode.UeReady,
                "Indexed content satisfies the minimum Unreal Engine readiness rules.", null));
        }

        var status = reasons.Any(static reason => reason.Severity == UnrealReadinessSeverity.Blocking)
            ? UnrealReadinessStatus.NotReady
            : reasons.Any(static reason => reason.Severity == UnrealReadinessSeverity.Warning)
                ? UnrealReadinessStatus.ReadyWithWarnings
                : UnrealReadinessStatus.Ready;
        return Create(status, reasons, evaluatedAtUtc);
    }

    public static UnrealReadinessSummary Summarize(IEnumerable<AssetSummary> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        var list = assets.Select(static asset => asset.UnrealReadiness).ToArray();
        return new(
            list.Count(static item => item.Status == UnrealReadinessStatus.Ready),
            list.Count(static item => item.Status == UnrealReadinessStatus.ReadyWithWarnings),
            list.Count(static item => item.Status == UnrealReadinessStatus.NotReady),
            list.Count(static item => item.Status == UnrealReadinessStatus.NotApplicable),
            list.Count(static item => item.Status == UnrealReadinessStatus.Unknown),
            list.Count(static item => item.ReadinessRuleVersion != CurrentRuleVersion || item.EvaluatedAtUtc is null),
            CurrentRuleVersion,
            list.Select(static item => item.EvaluatedAtUtc).Where(static item => item is not null).DefaultIfEmpty().Max());
    }

    public static bool IsCurrent(UnrealReadinessEvaluation evaluation) =>
        evaluation.ReadinessRuleVersion == CurrentRuleVersion && evaluation.EvaluatedAtUtc is not null;

    private static void EvaluateMeshAsset(
        AssetSummary asset,
        List<UnrealReadinessReason> reasons,
        bool requiresOpacity,
        bool requiresBillboard)
    {
        var meshes = asset.Content.Variants.SelectMany(static variant => variant.Meshes).OrderBy(static mesh => mesh.Lod).ThenBy(static mesh => mesh.Path, Comparer).ToArray();
        var primary = PrimarySet(asset.Content, [TextureSetKind.General, TextureSetKind.Unknown, TextureSetKind.Atlas]);
        if (meshes.Length == 0)
        {
            if (asset.Content.HasBillboard)
            {
                reasons.Add(Blocking(UnrealReadinessRuleCode.UeOnlyBillboard,
                    "Only billboard textures were found; a supported mesh is required for this asset type.", RelatedSet(asset.Content, TextureSetKind.Billboard)));
            }
            reasons.Add(Blocking(UnrealReadinessRuleCode.UeMissingMesh,
                "No supported FBX or ABC mesh was found in the indexed inventory.", null));
        }
        else if (!meshes.Any(static mesh => mesh.Format == MeshFormat.Fbx || mesh.Format == MeshFormat.Abc))
        {
            reasons.Add(Blocking(UnrealReadinessRuleCode.UeUnsupportedFileFormat,
                "No mesh with an Unreal-supported indexed format was found.", meshes[0].Path));
        }

        if (primary is null)
        {
            reasons.Add(Blocking(UnrealReadinessRuleCode.UeNoPrimaryTextureSet,
                "No primary texture set could be determined.", null));
        }
        else
        {
            RequireMap(primary, TextureMapType.Albedo, UnrealReadinessRuleCode.UeMissingAlbedo, UnrealReadinessSeverity.Blocking,
                "Primary texture set is missing albedo/base color.", reasons);
            RequireSurfaceMaps(primary, reasons, normalBlocking: false);
            if (requiresOpacity)
            {
                RequireMap(primary, TextureMapType.Opacity, UnrealReadinessRuleCode.UeMissingOpacity, UnrealReadinessSeverity.Blocking,
                    "Vegetation assets require opacity/alpha for typical Unreal material setup.", reasons);
            }
        }

        AddLodReasons(meshes, reasons);
        if (requiresBillboard && !asset.Content.HasBillboard)
        {
            reasons.Add(Warning(UnrealReadinessRuleCode.UeOnlyBillboard,
                "No billboard texture set was found for this vegetation asset.", null));
        }
        AddResolutionReasons(asset.Content, reasons);
    }

    private static void EvaluateTextureAsset(
        AssetSummary asset,
        List<UnrealReadinessReason> reasons,
        IReadOnlyList<TextureSetKind> allowedKinds,
        bool requiresOpacity,
        bool requiresNormalBlocking)
    {
        var primary = PrimarySet(asset.Content, allowedKinds);
        if (primary is null)
        {
            reasons.Add(Blocking(UnrealReadinessRuleCode.UeNoPrimaryTextureSet,
                "No usable primary texture set could be determined.", null));
            return;
        }

        RequireMap(primary, TextureMapType.Albedo, UnrealReadinessRuleCode.UeMissingAlbedo, UnrealReadinessSeverity.Blocking,
            "Primary texture set is missing albedo/base color.", reasons);
        RequireMap(primary, TextureMapType.Normal, UnrealReadinessRuleCode.UeMissingNormal,
            requiresNormalBlocking ? UnrealReadinessSeverity.Blocking : UnrealReadinessSeverity.Warning,
            "Primary texture set is missing a normal map.", reasons, TextureMapType.Bump);
        RequireMap(primary, TextureMapType.Roughness, UnrealReadinessRuleCode.UeMissingRoughness, UnrealReadinessSeverity.Warning,
            "Primary texture set is missing roughness/gloss.", reasons, TextureMapType.Gloss);
        RequireMap(primary, TextureMapType.Displacement, UnrealReadinessRuleCode.UeMissingDisplacement, UnrealReadinessSeverity.Warning,
            "Primary texture set is missing displacement/height.", reasons);
        if (requiresOpacity)
        {
            RequireMap(primary, TextureMapType.Opacity, UnrealReadinessRuleCode.UeMissingOpacity, UnrealReadinessSeverity.Blocking,
                "Primary texture set is missing opacity/alpha.", reasons);
        }

        AddResolutionReasons(asset.Content, reasons);
    }

    private static void AddInventoryIssues(AssetSummary asset, List<UnrealReadinessReason> reasons)
    {
        if (asset.Content.Completeness == AssetCompletenessStatus.Ambiguous ||
            asset.Content.Issues.Any(static issue => issue.Code == AssetContentIssueCode.ConflictingName))
        {
            reasons.Add(Blocking(UnrealReadinessRuleCode.UeAmbiguousInventory,
                "Inventory contains conflicting content and cannot be evaluated as ready.", FirstIssuePath(asset.Content)));
        }

        if (asset.Content.Issues.Any(static issue => issue.Code is AssetContentIssueCode.DuplicateMesh or AssetContentIssueCode.DuplicateTexture))
        {
            reasons.Add(Warning(UnrealReadinessRuleCode.UeDuplicateLogicalFiles,
                "Duplicate logical files were found in the indexed inventory.", FirstIssuePath(asset.Content)));
        }

        var unsupportedMesh = asset.Content.UnclassifiedFiles
            .Where(static file => Path.GetExtension(file.Path) is ".fbx" or ".FBX" or ".abc" or ".ABC")
            .OrderBy(static file => file.Path, Comparer)
            .FirstOrDefault();
        if (unsupportedMesh is not null)
        {
            reasons.Add(Blocking(UnrealReadinessRuleCode.UeUnsupportedFileFormat,
                unsupportedMesh.Reason, unsupportedMesh.Path));
        }
    }

    private static void AddLodReasons(MeshLodEntry[] meshes, List<UnrealReadinessReason> reasons)
    {
        if (meshes.Length == 0)
        {
            return;
        }

        var lods = meshes.Select(static mesh => mesh.Lod).Distinct().Order().ToArray();
        if (lods.Length == 1 && lods[0] == 0)
        {
            reasons.Add(Warning(UnrealReadinessRuleCode.UeNoLods,
                "Only LOD0 was found; no lower-detail LOD meshes are indexed.", meshes[0].Path));
            return;
        }

        var expected = Enumerable.Range(0, lods[^1] + 1).ToArray();
        if (!lods.SequenceEqual(expected))
        {
            reasons.Add(Warning(UnrealReadinessRuleCode.UeIncompleteLodChain,
                $"LOD chain is incomplete. Found {string.Join(", ", lods.Select(static lod => $"LOD{lod}"))}.", meshes[0].Path));
        }
    }

    private static void AddResolutionReasons(AssetContentInventory content, List<UnrealReadinessReason> reasons)
    {
        var resolutions = content.TextureSets
            .SelectMany(static set => set.Components.Select(component => component.Resolution ?? set.Resolution))
            .Where(static resolution => resolution is not null)
            .Select(static resolution => resolution!.Value)
            .Distinct()
            .Order()
            .ToArray();
        if (resolutions.Length > 1)
        {
            reasons.Add(Warning(UnrealReadinessRuleCode.UeMixedResolutions,
                $"Texture maps use mixed resolutions: {string.Join(", ", resolutions)}.", null));
        }
    }

    private static void RequireSurfaceMaps(TextureSetInventory primary, List<UnrealReadinessReason> reasons, bool normalBlocking)
    {
        RequireMap(primary, TextureMapType.Normal, UnrealReadinessRuleCode.UeMissingNormal,
            normalBlocking ? UnrealReadinessSeverity.Blocking : UnrealReadinessSeverity.Warning,
            "Primary texture set is missing a normal map.", reasons, TextureMapType.Bump);
        RequireMap(primary, TextureMapType.Roughness, UnrealReadinessRuleCode.UeMissingRoughness, UnrealReadinessSeverity.Warning,
            "Primary texture set is missing roughness/gloss.", reasons, TextureMapType.Gloss);
        RequireMap(primary, TextureMapType.Displacement, UnrealReadinessRuleCode.UeMissingDisplacement, UnrealReadinessSeverity.Warning,
            "Primary texture set is missing displacement/height.", reasons);
    }

    private static void RequireMap(
        TextureSetInventory primary,
        TextureMapType map,
        UnrealReadinessRuleCode code,
        UnrealReadinessSeverity severity,
        string message,
        List<UnrealReadinessReason> reasons,
        params TextureMapType[] equivalents)
    {
        var accepted = equivalents.Append(map).ToHashSet();
        if (primary.Components.Any(component => accepted.Contains(component.MapType)))
        {
            return;
        }

        reasons.Add(new(code, severity, message, RelatedSet(primary)));
    }

    private static TextureSetInventory? PrimarySet(AssetContentInventory content, IReadOnlyList<TextureSetKind> kinds) =>
        content.TextureSets
            .Where(set => kinds.Contains(set.Kind) && set.Components.Count > 0)
            .OrderByDescending(static set => set.Resolution ?? set.Components.Select(static component => component.Resolution).DefaultIfEmpty().Max() ?? 0)
            .ThenBy(static set => set.Kind)
            .ThenBy(static set => RelatedSet(set), Comparer)
            .FirstOrDefault();

    private static UnrealReadinessEvaluation Create(
        UnrealReadinessStatus status,
        List<UnrealReadinessReason> reasons,
        DateTimeOffset evaluatedAtUtc) =>
        new(status, CurrentRuleVersion, reasons
            .OrderBy(static reason => reason.Severity)
            .ThenBy(static reason => reason.RuleCode)
            .ThenBy(static reason => reason.RelatedInventoryItem, Comparer)
            .ThenBy(static reason => reason.Message, Comparer)
            .ToArray(), evaluatedAtUtc);

    private static AssetReadinessType NormalizeType(AssetSummary asset)
    {
        var value = string.IsNullOrWhiteSpace(asset.AssetType) ? asset.RawAssetType : asset.AssetType;
        return value?.Trim() switch
        {
            "3D Asset" => AssetReadinessType.ThreeDAsset,
            "3D Plant" => AssetReadinessType.ThreeDPlant,
            "Surface" => AssetReadinessType.Surface,
            "Atlas" => AssetReadinessType.Atlas,
            "Billboard" => AssetReadinessType.Billboard,
            "Decal" => AssetReadinessType.Decal,
            "Brush" => AssetReadinessType.Brush,
            _ => AssetReadinessType.Unknown
        };
    }

    private static string? FirstIssuePath(AssetContentInventory content) =>
        content.Issues.SelectMany(static issue => issue.Paths).Order(Comparer).FirstOrDefault();

    private static string? RelatedSet(AssetContentInventory content, TextureSetKind kind) =>
        content.TextureSets.Where(set => set.Kind == kind).Select(RelatedSet).Order(Comparer).FirstOrDefault();

    private static string? RelatedSet(TextureSetInventory set) =>
        set.Components.Select(static component => component.Path).Order(Comparer).FirstOrDefault();

    private static UnrealReadinessReason Blocking(UnrealReadinessRuleCode code, string message, string? related) =>
        new(code, UnrealReadinessSeverity.Blocking, message, related);

    private static UnrealReadinessReason Warning(UnrealReadinessRuleCode code, string message, string? related) =>
        new(code, UnrealReadinessSeverity.Warning, message, related);

    private static UnrealReadinessReason Info(UnrealReadinessRuleCode code, string message, string? related) =>
        new(code, UnrealReadinessSeverity.Information, message, related);

    private enum AssetReadinessType
    {
        Unknown,
        ThreeDAsset,
        ThreeDPlant,
        Surface,
        Atlas,
        Billboard,
        Decal,
        Brush
    }
}
