using ScanVault.Core.Models;

namespace ScanVault.Core.Policies;

public static class UnrealImportPackageValidationPolicy
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public static UnrealImportPackageValidation Validate(
        UnrealImportPackage package,
        Func<string, bool>? sourceExists = null)
    {
        var issues = new List<UnrealImportPackageIssue>();
        if (package.SchemaVersion != UnrealImportPackageSchema.CurrentVersion)
        {
            issues.Add(Error(UnrealImportPackageIssueCode.UnsupportedSchema,
                $"Unsupported manifest schema version {package.SchemaVersion}."));
        }

        if (string.IsNullOrWhiteSpace(package.Source.AssetId) ||
            string.IsNullOrWhiteSpace(package.Source.JsonPath) ||
            string.IsNullOrWhiteSpace(package.Source.AssetFolderPath))
        {
            issues.Add(Error(UnrealImportPackageIssueCode.MissingSourceAssetIdentity,
                "Source asset identity must include asset ID, JSON path, and asset folder path."));
        }

        if (package.Readiness.RuleVersion != UnrealReadinessPolicy.CurrentRuleVersion)
        {
            issues.Add(Error(UnrealImportPackageIssueCode.ReadinessStale,
                "Unreal readiness is stale. Rescan the library before creating a package."));
        }

        if (!package.Options.ReadinessOverride &&
            package.Readiness.Status is UnrealReadinessStatus.NotReady or UnrealReadinessStatus.Unknown or UnrealReadinessStatus.NotApplicable)
        {
            issues.Add(Error(UnrealImportPackageIssueCode.ReadinessBlocked,
                $"Package generation is blocked for readiness status {package.Readiness.Status}."));
        }

        if (package.Readiness.Status == UnrealReadinessStatus.ReadyWithWarnings)
        {
            issues.Add(Warning(UnrealImportPackageIssueCode.ReadinessWarning,
                "The asset is UE Ready With Warnings; review readiness reasons before import.", null));
        }

        if (!UnrealImportDestinationPolicy.IsValidGamePath(package.Destination.ContentPath))
        {
            issues.Add(Error(UnrealImportPackageIssueCode.InvalidDestinationPath,
                "Destination content path must use /Game/... syntax."));
        }

        if (RequiresMesh(package.Source.AssetType) && package.Mesh?.Lods.Count is null or 0)
        {
            issues.Add(Error(UnrealImportPackageIssueCode.MissingRequiredMesh,
                "A mesh asset package requires a selected primary mesh.", null));
        }

        if (!package.Textures.Any(static texture => texture.Role == UnrealImportSemanticRole.BaseColor))
        {
            issues.Add(Error(UnrealImportPackageIssueCode.MissingRequiredTexture,
                "A package requires a BaseColor texture role.", null));
        }

        if (package.Options.CreateMaterialInstance)
        {
            if (string.IsNullOrWhiteSpace(package.Material.Id))
            {
                issues.Add(Error(UnrealImportPackageIssueCode.MissingProfile,
                    "A material profile is required when creating a Material Instance.", null));
            }

            if (string.IsNullOrWhiteSpace(package.Material.MasterMaterialPath))
            {
                issues.Add(Warning(UnrealImportPackageIssueCode.MissingMasterMaterialPath,
                    "Master Material path is not configured; the UE consumer must choose one.", null));
            }
            else if (!UnrealImportDestinationPolicy.IsValidGamePath(package.Material.MasterMaterialPath))
            {
                issues.Add(Error(UnrealImportPackageIssueCode.InvalidDestinationPath,
                    "Master Material path must use /Game/... syntax.", package.Material.MasterMaterialPath));
            }
        }

        AddTextureMappingIssues(package, issues);
        AddSourceExistenceIssues(package, sourceExists, issues);

        return new(issues
            .OrderBy(static issue => issue.Severity)
            .ThenBy(static issue => issue.Code)
            .ThenBy(static issue => issue.RelatedPath, Comparer)
            .ThenBy(static issue => issue.Message, Comparer)
            .ToArray());
    }

    private static void AddTextureMappingIssues(
        UnrealImportPackage package,
        List<UnrealImportPackageIssue> issues)
    {
        var texturesByRole = package.Textures
            .Where(static texture => texture.Role != UnrealImportSemanticRole.Other)
            .GroupBy(static texture => texture.Role)
            .ToDictionary(static group => group.Key, static group => group.ToArray());
        foreach (var item in texturesByRole.Where(static item => item.Value.Length > 1).OrderBy(static item => item.Key))
        {
            issues.Add(Warning(UnrealImportPackageIssueCode.AmbiguousTextureRole,
                $"Multiple textures map to semantic role {item.Key}; the first deterministic candidate was selected.",
                item.Value[0].SourcePath));
        }

        foreach (var mapping in package.Material.TextureParameterMappings.OrderBy(static mapping => mapping.Role))
        {
            if (mapping.Role == UnrealImportSemanticRole.BaseColor)
            {
                continue;
            }

            if (!texturesByRole.ContainsKey(mapping.Role))
            {
                issues.Add(Warning(UnrealImportPackageIssueCode.MissingOptionalTexture,
                    $"Optional texture role {mapping.Role} is not present in the package.", null));
            }
        }
    }

    private static void AddSourceExistenceIssues(
        UnrealImportPackage package,
        Func<string, bool>? sourceExists,
        List<UnrealImportPackageIssue> issues)
    {
        if (sourceExists is null)
        {
            return;
        }

        foreach (var path in RequiredSourcePaths(package))
        {
            if (!sourceExists(path))
            {
                issues.Add(Error(UnrealImportPackageIssueCode.MissingSourcePath,
                    "A required source file is missing at preview/export time.", path));
            }
        }
    }

    private static IEnumerable<string> RequiredSourcePaths(UnrealImportPackage package)
    {
        if (!string.IsNullOrWhiteSpace(package.Source.JsonPath))
        {
            yield return package.Source.JsonPath;
        }

        if (package.Mesh is not null)
        {
            foreach (var lod in package.Mesh.Lods)
            {
                yield return lod.SourcePath;
            }
        }

        foreach (var texture in package.Textures)
        {
            yield return texture.SourcePath;
        }
    }

    private static bool RequiresMesh(string assetType) =>
        StringComparer.OrdinalIgnoreCase.Equals(assetType, "3D Asset") ||
        StringComparer.OrdinalIgnoreCase.Equals(assetType, "3D Plant");

    private static UnrealImportPackageIssue Error(UnrealImportPackageIssueCode code, string message, string? path = null) =>
        new(code, UnrealImportValidationSeverity.Error, message, path);

    private static UnrealImportPackageIssue Warning(UnrealImportPackageIssueCode code, string message, string? path) =>
        new(code, UnrealImportValidationSeverity.Warning, message, path);
}
