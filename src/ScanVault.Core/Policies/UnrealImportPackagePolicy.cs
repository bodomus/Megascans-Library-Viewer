using System.Security.Cryptography;
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
        var textures = SelectTextures(asset.Content).ToArray();
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
            textures,
            material,
            options,
            new([]));
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
        builder.Append("json=").Append(package.Source.JsonPath).Append('\n');
        builder.Append("folder=").Append(package.Source.AssetFolderPath).Append('\n');
        builder.Append("destination=").Append(package.Destination.ContentPath).Append('\n');
        builder.Append("profile=").Append(package.Material.Id).Append('\n');
        builder.Append("variant=").Append(package.Mesh?.PrimaryVariant ?? string.Empty).Append('\n');
        builder.Append("options=").Append(package.Options.ImportLods).Append('|')
            .Append(package.Options.EnableNanite).Append('|')
            .Append(package.Options.CreateMaterialInstance).Append('|')
            .Append(package.Options.ReadinessOverride).Append('\n');
        foreach (var texture in package.Textures.OrderBy(static texture => UnrealImportSemanticRolePolicy.Order(texture.Role)).ThenBy(static texture => texture.SourcePath, Comparer))
        {
            builder.Append("texture=").Append(texture.Role).Append('|').Append(texture.MapType).Append('|').Append(texture.SourcePath).Append('\n');
        }

        foreach (var lod in package.Mesh?.Lods ?? [])
        {
            builder.Append("lod=").Append(lod.Variant).Append('|').Append(lod.Lod).Append('|').Append(lod.Format).Append('|').Append(lod.SourcePath).Append('\n');
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..32];
    }

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

    private static IEnumerable<UnrealImportTexture> SelectTextures(AssetContentInventory content)
    {
        var primary = AssetContentSelectionPolicy.SelectPrimaryTextureSet(
            content,
            [TextureSetKind.General, TextureSetKind.Unknown, TextureSetKind.Atlas, TextureSetKind.Billboard]);
        if (primary is null)
        {
            yield break;
        }

        foreach (var component in primary.Components
                     .OrderBy(static component => UnrealImportSemanticRolePolicy.Order(UnrealImportSemanticRolePolicy.Map(component.MapType)))
                     .ThenByDescending(component => component.Resolution ?? primary.Resolution ?? 0)
                     .ThenBy(static component => component.MapType)
                     .ThenBy(static component => component.Path, Comparer)
                     .GroupBy(static component => UnrealImportSemanticRolePolicy.Map(component.MapType))
                     .Select(static group => group.First())
                     .OrderBy(static component => UnrealImportSemanticRolePolicy.Order(UnrealImportSemanticRolePolicy.Map(component.MapType)))
                     .ThenBy(static component => component.Path, Comparer))
        {
            yield return new(
                UnrealImportSemanticRolePolicy.Map(component.MapType),
                component.Path,
                component.MapType,
                primary.Kind,
                component.Resolution ?? primary.Resolution,
                component.Format);
        }
    }
}
