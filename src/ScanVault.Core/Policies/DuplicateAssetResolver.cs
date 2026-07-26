using ScanVault.Core.Models;

namespace ScanVault.Core.Policies;

public static class DuplicateAssetResolver
{
    public static DuplicateResolution Resolve(IEnumerable<AssetSummary> assets)
    {
        var winners = new List<AssetSummary>();
        var duplicates = new List<DuplicateAssetGroup>();

        foreach (var group in assets
                     .GroupBy(static asset => asset.Id, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            // The path rule is stable across scan order and timestamp changes.
            var ordered = group
                .OrderBy(static asset => PathPolicy.Normalize(asset.JsonPath), PathPolicy.Comparer)
                .ToArray();
            winners.Add(ordered[0]);

            if (ordered.Length > 1)
            {
                duplicates.Add(new(
                    ordered[0].Id,
                    PathPolicy.Normalize(ordered[0].JsonPath),
                    ordered.Skip(1).Select(static asset => PathPolicy.Normalize(asset.JsonPath)).ToArray()));
            }
        }

        return new(winners, duplicates);
    }
}
