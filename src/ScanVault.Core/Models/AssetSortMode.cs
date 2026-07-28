namespace ScanVault.Core.Models;

/// <summary>Stable logic keys for catalog ordering. UI labels are defined separately.</summary>
public enum AssetSortMode
{
    NameAscending,
    NameDescending,
    TypeAscending,
    ResolutionDescending,
    ResolutionAscending,
    RecentlyModified,
    OldestModified,
    AssetIdAscending,
    Completeness,
    VariantCountDescending,
    LodCountDescending,
    TextureSetCountDescending
}
