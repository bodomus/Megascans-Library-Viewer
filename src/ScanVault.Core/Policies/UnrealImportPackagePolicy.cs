using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using ScanVault.Core.Models;

namespace ScanVault.Core.Policies;

public static class UnrealImportPackagePolicy
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public static bool IsEligibleByDefault(UnrealReadinessEvaluation readiness) =>
        UnrealReadinessPolicy.IsCurrent(readiness) &&
        readiness.Status is UnrealReadinessStatus.Ready or UnrealReadinessStatus.ReadyWithWarnings;

    public static UnrealImportPackage Create(UnrealImportPackageRequest request, Func<string, bool>? sourceExists = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var asset = request.Asset;
        var destination = UnrealImportDestinationPolicy.Create(request.DestinationBasePath, asset);
        var material = CreateMaterialSnapshot(request.Profile, destination.AssetBaseName);
        var textureSelection = SelectTextures(asset.Content);
        var mesh = CreateMesh(asset.Content);
        var options = request.Options with { CreateMaterialInstance = request.Profile.CreateMaterialInstance && request.Options.CreateMaterialInstance };
        var package = new UnrealImportPackage(
            UnrealImportPackageSchema.CurrentVersion,
            string.Empty,
            new("ScanVault", request.ApplicationVersion, request.CommitSha, request.GeneratedAtUtc.ToUniversalTime()),
            new(asset.Id, asset.Name, asset.AssetType, asset.JsonPath, asset.AssetFolderPath, asset.LastWriteTimeUtc.ToUniversalTime()),
            CreateReadinessSnapshot(asset.UnrealReadiness),
            destination,
            mesh,
            textureSelection.SelectedTextures,
            material,
            options,
            new(textureSelection.Issues));
        package = package with { PackageId = ComputePackageId(package) };
        return package with { Validation = UnrealImportPackageValidationPolicy.Validate(package, sourceExists) };
    }

    public static string DefaultFileName(AssetSummary asset) =>
        $"{UnrealImportNamePolicy.SanitizeSegment(asset.Name)}.scanvault-ue.json";

    public static string ComputePackageId(UnrealImportPackage package)
    {
        var builder = new StringBuilder();
        builder.Append("schema=").Append(package.SchemaVersion).Append('\n');
        builder.Append("assetId=").Append(package.Source.AssetId).Append('\n');
        builder.Append("json=").Append(NormalizePath(package.Source.JsonPath)).Append('\n');
        builder.Append("folder=").Append(NormalizePath(package.Source.AssetFolderPath)).Append('\n');
        builder.Append("sourceLastWriteUtc=").Append(package.Source.LastWriteTimeUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("readiness=").Append(package.Readiness.Status).Append('|').Append(package.Readiness.RuleVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("destination=").Append(NormalizePath(package.Destination.ContentPath)).Append('\n');
        builder.Append("assetBaseName=").Append(package.Destination.AssetBaseName).Append('\n');
        builder.Append("profile=").Append(package.Material.Id).Append('\n');
        builder.Append("masterMaterial=").Append(NormalizePath(package.Material.MasterMaterialPath ?? string.Empty)).Append('\n');
        builder.Append("materialInstancePrefix=").Append(package.Material.MaterialInstancePrefix).Append('\n');
        builder.Append("materialInstanceName=").Append(package.Material.MaterialInstanceName).Append('\n');
        foreach (var mapping in package.Material.TextureParameterMappings
                     .OrderBy(static mapping => UnrealImportSemanticRolePolicy.Order(mapping.Role))
                     .ThenBy(static mapping => mapping.ParameterName, Comparer))
        {
            builder.Append("mapping=").Append(mapping.Role).Append('|').Append(mapping.ParameterName).Append('\n');
        }

        builder.Append("variant=").Append(package.Mesh?.PrimaryVariant ?? string.Empty).Append('\n');
        builder.Append("options=").Append(NormalizeBool(package.Options.ImportLods)).Append('|')
            .Append(NormalizeBool(package.Options.EnableNanite)).Append('|')
            .Append(NormalizeBool(package.Options.CreateMaterialInstance)).Append('|')
            .Append(NormalizeBool(package.Options.ReadinessOverride)).Append('\n');
        foreach (var texture in package.Textures
                     .OrderBy(static texture => UnrealImportSemanticRolePolicy.Order(texture.Role))
                     .ThenBy(static texture => texture.MapType)
                     .ThenBy(static texture => texture.SourcePath, Comparer))
        {
            builder.Append("texture=").Append(texture.Role)
                .Append('|').Append(texture.MapType)
                .Append('|').Append(NormalizePath(texture.SourcePath))
                .Append('|').Append(texture.SetKind)
                .Append('|').Append(texture.Resolution?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)
                .Append('|').Append(texture.Format)
                .Append('\n');
        }

        foreach (var lod in (package.Mesh?.Lods ?? [])
                     .OrderBy(static lod => lod.Variant, Comparer)
                     .ThenBy(static lod => lod.Lod)
                     .ThenBy(static lod => lod.Format)
                     .ThenBy(static lod => lod.SourcePath, Comparer))
        {
            builder.Append("lod=").Append(lod.Variant)
                .Append('|').Append(lod.Lod.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(lod.Format)
                .Append('|').Append(NormalizePath(lod.SourcePath))
                .Append('\n');
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..32];
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static string NormalizeBool(bool value) => value ? "true" : "false";

    private static UnrealImportReadinessSnapshot CreateReadinessSnapshot(UnrealReadinessEvaluation readiness) =>
        new(
            readiness.Status,
            readiness.ReadinessRuleVersion,
            readiness.BlockingCount,
            readiness.WarningCount,
            readiness.Reasons
                .OrderBy(static reason => reason.Severity)
                .ThenBy(static reason => reason.RuleCode)
                .ThenBy(static reason => reason.RelatedInventoryItem, Comparer)
                .ThenBy(static reason => reason.Message, Comparer)
                .Select(static reason => new UnrealImportReadinessReasonSnapshot(
                    reason.Code,
                    reason.Severity,
                    reason.Message,
                    reason.RelatedInventoryItem))
                .ToArray());

    private static UnrealImportMaterialProfileSnapshot CreateMaterialSnapshot(
        UnrealMaterialProfile profile,
        string assetBaseName) =>
        new(
            profile.Id,
            profile.Name,
            profile.Description,
            profile.MasterMaterialPath,
            UnrealImportNamePolicy.MaterialInstanceName(profile.MaterialInstancePrefix, assetBaseName),
            UnrealImportNamePolicy.SanitizePrefix(profile.MaterialInstancePrefix),
            profile.TextureParameterMappings
                .OrderBy(static mapping => UnrealImportSemanticRolePolicy.Order(mapping.Role))
                .ThenBy(static mapping => mapping.ParameterName, Comparer)
                .ToArray(),
            profile.IsBuiltIn);

    private static UnrealImportMesh? CreateMesh(AssetContentInventory content)
    {
        var variant = AssetContentSelectionPolicy.SelectPrimaryVariant(content);
        if (variant is null)
        {
            return null;
        }

        var lods = variant.Meshes
            .OrderBy(static mesh => mesh.Lod)
            .ThenBy(static mesh => mesh.Format)
            .ThenBy(static mesh => mesh.Path, Comparer)
            .Select(static mesh => new UnrealImportMeshLod(mesh.Variant, mesh.Lod, mesh.Path, mesh.Format))
            .ToArray();
        return new(variant.Name, lods);
    }

    private static UnrealImportTextureSelectionResult SelectTextures(AssetContentInventory content)
    {
        var primary = AssetContentSelectionPolicy.SelectPrimaryTextureSet(
            content,
            [TextureSetKind.General, TextureSetKind.Unknown, TextureSetKind.Atlas, TextureSetKind.Billboard]);
        if (primary is null)
        {
            return new([], []);
        }

        var selected = new List<UnrealImportTexture>();
        var issues = new List<UnrealImportPackageIssue>();
        foreach (var group in primary.Components
                     .Select(component => new TextureCandidate(
                         UnrealImportSemanticRolePolicy.Map(component.MapType),
                         component))
                     .Where(static candidate => candidate.Role != UnrealImportSemanticRole.Other)
                     .GroupBy(static candidate => candidate.Role)
                     .OrderBy(static group => UnrealImportSemanticRolePolicy.Order(group.Key)))
        {
            var ordered = group
                .OrderBy(static candidate => PreferredMapRank(candidate.Role, candidate.Component.MapType))
                .ThenByDescending(candidate => candidate.Component.Resolution ?? primary.Resolution ?? 0)
                .ThenBy(static candidate => candidate.Component.MapType)
                .ThenBy(static candidate => candidate.Component.Format, Comparer)
                .ThenBy(static candidate => candidate.Component.Path, Comparer)
                .ToArray();
            if (ordered.Length > 1)
            {
                issues.Add(new(
                    UnrealImportPackageIssueCode.AmbiguousTextureRole,
                    UnrealImportValidationSeverity.Warning,
                    $"Multiple textures map to semantic role {group.Key}; deterministic priority selected {ordered[0].Component.MapType}.",
                    ordered[0].Component.Path));
            }

            var component = ordered[0].Component;
            selected.Add(new(
                group.Key,
                component.Path,
                component.MapType,
                primary.Kind,
                component.Resolution ?? primary.Resolution,
                component.Format));
        }

        return new(selected
            .OrderBy(static texture => UnrealImportSemanticRolePolicy.Order(texture.Role))
            .ThenBy(static texture => texture.SourcePath, Comparer)
            .ToArray(), issues
            .OrderBy(static issue => issue.Code)
            .ThenBy(static issue => issue.RelatedPath, Comparer)
            .ThenBy(static issue => issue.Message, Comparer)
            .ToArray());
    }

    private static int PreferredMapRank(UnrealImportSemanticRole role, TextureMapType mapType) => role switch
    {
        UnrealImportSemanticRole.BaseColor => mapType == TextureMapType.Albedo ? 0 : 10,
        UnrealImportSemanticRole.Normal => mapType == TextureMapType.Normal ? 0 :
            mapType == TextureMapType.Bump ? 1 : 10,
        UnrealImportSemanticRole.Roughness => mapType == TextureMapType.Roughness ? 0 :
            mapType == TextureMapType.Gloss ? 1 : 10,
        UnrealImportSemanticRole.AO => mapType == TextureMapType.AmbientOcclusion ? 0 :
            mapType == TextureMapType.Cavity ? 1 : 10,
        UnrealImportSemanticRole.Displacement => mapType == TextureMapType.Displacement ? 0 : 10,
        UnrealImportSemanticRole.Opacity => mapType == TextureMapType.Opacity ? 0 : 10,
        UnrealImportSemanticRole.Specular => mapType == TextureMapType.Specular ? 0 : 10,
        UnrealImportSemanticRole.Translucency => mapType == TextureMapType.Translucency ? 0 : 10,
        _ => 10
    };

    private sealed record TextureCandidate(UnrealImportSemanticRole Role, TextureComponentEntry Component);
    private sealed record UnrealImportTextureSelectionResult(
        IReadOnlyList<UnrealImportTexture> SelectedTextures,
        IReadOnlyList<UnrealImportPackageIssue> Issues);
}
