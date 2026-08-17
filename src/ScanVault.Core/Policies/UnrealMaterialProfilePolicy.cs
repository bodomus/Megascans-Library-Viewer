using ScanVault.Core.Models;

namespace ScanVault.Core.Policies;

public static class UnrealMaterialProfilePolicy
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public static IReadOnlyList<UnrealMaterialProfile> BuiltInProfiles { get; } =
    [
        CreateBuiltIn("default-surface", "Default Surface", "Surface", "/Game/Materials/M_Master_Megascans_Surface", false),
        CreateBuiltIn("default-3d-asset", "Default 3D Asset", "3D Asset", "/Game/Materials/M_Master_Megascans_3D", true),
        CreateBuiltIn("default-3d-plant", "Default 3D Plant", "3D Plant", "/Game/Materials/M_Master_Megascans_Foliage", true),
        CreateBuiltIn("default-atlas", "Default Atlas", "Atlas", "/Game/Materials/M_Master_Megascans_Atlas", false),
        CreateBuiltIn("default-decal", "Default Decal", "Decal", "/Game/Materials/M_Master_Megascans_Decal", false)
    ];

    public static IReadOnlyList<UnrealMaterialProfile> MergeWithBuiltIns(IEnumerable<UnrealMaterialProfile> userProfiles) =>
        BuiltInProfiles
            .Concat(userProfiles.Where(static profile => !profile.IsBuiltIn))
            .OrderBy(static profile => profile.IsBuiltIn ? 0 : 1)
            .ThenBy(static profile => profile.Name, Comparer)
            .ToArray();

    public static IReadOnlyList<UnrealMaterialProfile> CompatibleProfiles(
        IEnumerable<UnrealMaterialProfile> profiles,
        string assetType) =>
        profiles
            .Where(profile => IsCompatible(profile, assetType))
            .OrderBy(static profile => profile.IsBuiltIn ? 0 : 1)
            .ThenBy(static profile => profile.Name, Comparer)
            .ToArray();

    public static bool IsCompatible(UnrealMaterialProfile profile, string assetType) =>
        profile.AssetTypes.Any(type => Comparer.Equals(type, assetType));

    public static UnrealMaterialProfile SelectDefault(
        IReadOnlyList<UnrealMaterialProfile> profiles,
        string assetType,
        IReadOnlyList<UnrealImportDefaultProfile> defaults)
    {
        var compatible = CompatibleProfiles(profiles, assetType);
        var defaultProfileId = defaults.FirstOrDefault(item => Comparer.Equals(item.AssetType, assetType))?.ProfileId;
        return compatible.FirstOrDefault(profile => Comparer.Equals(profile.Id, defaultProfileId)) ??
               (compatible.Count > 0 ? compatible[0] : null) ??
               profiles.OrderBy(static profile => profile.Name, Comparer).First();
    }

    public static IReadOnlyList<UnrealImportPackageIssue> Validate(
        UnrealMaterialProfile profile,
        IEnumerable<UnrealMaterialProfile> existingProfiles)
    {
        var issues = new List<UnrealImportPackageIssue>();
        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            issues.Add(Error(UnrealImportPackageIssueCode.MissingProfile, "Profile ID is required."));
        }

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            issues.Add(Error(UnrealImportPackageIssueCode.MissingProfile, "Profile name is required."));
        }

        if (profile.AssetTypes.Count == 0)
        {
            issues.Add(Error(UnrealImportPackageIssueCode.MissingProfile, "At least one compatible asset type is required."));
        }

        if (!string.IsNullOrWhiteSpace(profile.MasterMaterialPath) &&
            !UnrealImportDestinationPolicy.IsValidGamePath(profile.MasterMaterialPath))
        {
            issues.Add(Error(UnrealImportPackageIssueCode.InvalidDestinationPath,
                "Master Material path must use /Game/... syntax."));
        }

        if (string.IsNullOrWhiteSpace(profile.MaterialInstancePrefix))
        {
            issues.Add(Error(UnrealImportPackageIssueCode.MissingProfile, "Material Instance prefix is required."));
        }

        var duplicateRoles = profile.TextureParameterMappings
            .GroupBy(static mapping => mapping.Role)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();
        foreach (var role in duplicateRoles)
        {
            issues.Add(Error(UnrealImportPackageIssueCode.MissingProfile,
                $"Texture role '{role}' has duplicate parameter mappings."));
        }

        if (profile.TextureParameterMappings.Any(static mapping => string.IsNullOrWhiteSpace(mapping.ParameterName)))
        {
            issues.Add(Error(UnrealImportPackageIssueCode.MissingProfile,
                "Texture parameter names must be non-empty."));
        }

        if (existingProfiles.Any(other =>
                !ReferenceEquals(other, profile) &&
                Comparer.Equals(other.Id, profile.Id)))
        {
            issues.Add(Error(UnrealImportPackageIssueCode.MissingProfile,
                $"Profile ID '{profile.Id}' is already used."));
        }

        return issues.OrderBy(static issue => issue.Code).ThenBy(static issue => issue.Message, Comparer).ToArray();
    }

    public static UnrealMaterialProfile Duplicate(UnrealMaterialProfile profile, string id, string name) =>
        profile with { Id = id, Name = name, IsBuiltIn = false };

    private static UnrealMaterialProfile CreateBuiltIn(
        string id,
        string name,
        string assetType,
        string masterMaterialPath,
        bool enableNanite)
    {
        var options = new UnrealImportOptions(
            ImportLods: true,
            EnableNanite: enableNanite,
            CreateMaterialInstance: true,
            ReadinessOverride: false);
        return new(
            id,
            name,
            $"Built-in template for {assetType} imports.",
            [assetType],
            masterMaterialPath,
            true,
            "MI_",
            DefaultMappings(),
            options,
            true);
    }

    private static IReadOnlyList<UnrealImportTextureParameterMapping> DefaultMappings() =>
    [
        new(UnrealImportSemanticRole.BaseColor, "BaseColorTexture"),
        new(UnrealImportSemanticRole.Normal, "NormalTexture"),
        new(UnrealImportSemanticRole.Roughness, "RoughnessTexture"),
        new(UnrealImportSemanticRole.AO, "AOTexture"),
        new(UnrealImportSemanticRole.Displacement, "HeightTexture"),
        new(UnrealImportSemanticRole.Opacity, "OpacityTexture")
    ];

    private static UnrealImportPackageIssue Error(UnrealImportPackageIssueCode code, string message) =>
        new(code, UnrealImportValidationSeverity.Error, message, null);
}
