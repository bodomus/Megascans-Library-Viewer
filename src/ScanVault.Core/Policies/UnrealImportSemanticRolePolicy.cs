using ScanVault.Core.Models;

namespace ScanVault.Core.Policies;

public static class UnrealImportSemanticRolePolicy
{
    public static UnrealImportSemanticRole Map(TextureMapType mapType) => mapType switch
    {
        TextureMapType.Albedo => UnrealImportSemanticRole.BaseColor,
        TextureMapType.Normal or TextureMapType.Bump => UnrealImportSemanticRole.Normal,
        TextureMapType.Roughness or TextureMapType.Gloss => UnrealImportSemanticRole.Roughness,
        TextureMapType.AmbientOcclusion or TextureMapType.Cavity => UnrealImportSemanticRole.AO,
        TextureMapType.Displacement => UnrealImportSemanticRole.Displacement,
        TextureMapType.Opacity => UnrealImportSemanticRole.Opacity,
        TextureMapType.Specular => UnrealImportSemanticRole.Specular,
        TextureMapType.Translucency => UnrealImportSemanticRole.Translucency,
        _ => UnrealImportSemanticRole.Other
    };

    public static int Order(UnrealImportSemanticRole role) => role switch
    {
        UnrealImportSemanticRole.BaseColor => 0,
        UnrealImportSemanticRole.Normal => 1,
        UnrealImportSemanticRole.Roughness => 2,
        UnrealImportSemanticRole.AO => 3,
        UnrealImportSemanticRole.Displacement => 4,
        UnrealImportSemanticRole.Opacity => 5,
        UnrealImportSemanticRole.Specular => 6,
        UnrealImportSemanticRole.Metalness => 7,
        UnrealImportSemanticRole.Emissive => 8,
        UnrealImportSemanticRole.Translucency => 9,
        _ => 99
    };
}
