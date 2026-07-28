using ScanVault.Core.Models;

namespace ScanVault.Core.Policies;

public static class AssetSorting
{
    public static IReadOnlyList<AssetSummary> Apply(
        IEnumerable<AssetSummary> assets,
        AssetSortMode mode)
    {
        IOrderedEnumerable<AssetSummary> ordered = mode switch
        {
            AssetSortMode.NameDescending => assets.OrderByDescending(
                static asset => asset.Name,
                StringComparer.OrdinalIgnoreCase),
            AssetSortMode.TypeAscending => assets.OrderBy(
                static asset => asset.AssetType,
                StringComparer.OrdinalIgnoreCase),
            AssetSortMode.ResolutionDescending => assets
                .OrderBy(static asset => asset.MaxResolution is null)
                .ThenByDescending(static asset => asset.MaxResolution?.MaxDimension ?? 0)
                .ThenByDescending(static asset => asset.MaxResolution?.PixelCount ?? 0),
            AssetSortMode.ResolutionAscending => assets
                .OrderBy(static asset => asset.MaxResolution is null)
                .ThenBy(static asset => asset.MaxResolution?.MaxDimension ?? 0)
                .ThenBy(static asset => asset.MaxResolution?.PixelCount ?? 0),
            AssetSortMode.RecentlyModified => assets.OrderByDescending(
                static asset => asset.LastWriteTimeUtc),
            AssetSortMode.OldestModified => assets.OrderBy(
                static asset => asset.LastWriteTimeUtc),
            AssetSortMode.AssetIdAscending => assets.OrderBy(
                static asset => asset.Id,
                StringComparer.OrdinalIgnoreCase),
            AssetSortMode.Completeness => assets.OrderBy(static asset => CompletenessRank(asset.Content.Completeness)),
            AssetSortMode.VariantCountDescending => assets.OrderByDescending(static asset => asset.Content.VariantCount),
            AssetSortMode.LodCountDescending => assets.OrderByDescending(static asset => asset.Content.LodCount),
            AssetSortMode.TextureSetCountDescending => assets.OrderByDescending(static asset => asset.Content.TextureSetCount),
            _ => assets.OrderBy(
                static asset => asset.Name,
                StringComparer.OrdinalIgnoreCase)
        };

        // Stable identity tie-breakers make every refresh deterministic.
        return ordered
            .ThenBy(static asset => asset.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static asset => asset.AssetFolderPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int CompletenessRank(AssetCompletenessStatus status) => status switch
    {
        AssetCompletenessStatus.Complete => 0,
        AssetCompletenessStatus.Usable => 1,
        AssetCompletenessStatus.Partial => 2,
        AssetCompletenessStatus.MissingCriticalFiles => 3,
        AssetCompletenessStatus.Ambiguous => 4,
        _ => 5
    };
}
