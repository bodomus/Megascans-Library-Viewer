using ScanVault.Core.Models;

namespace ScanVault.Core.Policies;

public static class AssetContentSelectionPolicy
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public static TextureSetInventory? SelectPrimaryTextureSet(
        AssetContentInventory content,
        IReadOnlyList<TextureSetKind> allowedKinds) =>
        content.TextureSets
            .Where(set => allowedKinds.Contains(set.Kind) && set.Components.Count > 0)
            .OrderByDescending(static set => set.Resolution ?? set.Components.Select(static component => component.Resolution).DefaultIfEmpty().Max() ?? 0)
            .ThenBy(static set => set.Kind)
            .ThenBy(static set => RelatedSet(set), Comparer)
            .FirstOrDefault();

    public static IReadOnlyList<MeshVariantInventory> OrderVariants(AssetContentInventory content) =>
        content.Variants
            .OrderBy(static variant => variant.Name, Comparer)
            .Select(static variant => variant with
            {
                Meshes = variant.Meshes
                    .OrderBy(static mesh => mesh.Lod)
                    .ThenBy(static mesh => mesh.Format)
                    .ThenBy(static mesh => mesh.Path, Comparer)
                    .ToArray()
            })
            .ToArray();

    public static MeshVariantInventory? SelectPrimaryVariant(AssetContentInventory content) =>
        OrderVariants(content)
            .OrderByDescending(static variant => variant.Meshes.Any(static mesh => mesh.Lod == 0))
            .ThenBy(static variant => variant.Name, Comparer)
            .FirstOrDefault(static variant => variant.Meshes.Count > 0);

    private static string? RelatedSet(TextureSetInventory set) =>
        set.Components.Select(static component => component.Path).Order(Comparer).FirstOrDefault();
}
