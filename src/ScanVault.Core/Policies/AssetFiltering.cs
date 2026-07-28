using ScanVault.Core.Models;

namespace ScanVault.Core.Policies;

public static class AssetFiltering
{
    public static bool IsInFolder(AssetSummary asset, string selectedFolder)
    {
        var assetFolder = PathPolicy.Normalize(asset.AssetFolderPath);
        var selected = PathPolicy.Normalize(selectedFolder);

        if (PathPolicy.Comparer.Equals(assetFolder, selected))
        {
            return true;
        }

        return assetFolder.StartsWith(
            selected + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesSearch(AssetSummary asset, string searchText)
    {
        var term = searchText.Trim();
        return term.Length == 0 ||
               asset.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               asset.Id.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               asset.AssetType.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               asset.Categories.Any(value => value.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
               asset.Tags.Any(tag => tag.Value.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
               asset.Biome?.Contains(term, StringComparison.OrdinalIgnoreCase) == true ||
               asset.Region?.Contains(term, StringComparison.OrdinalIgnoreCase) == true ||
               asset.Content.Completeness.ToString().Contains(term, StringComparison.OrdinalIgnoreCase) ||
               asset.Content.Variants.Any(variant =>
                   variant.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                   variant.Meshes.Any(mesh => mesh.FileName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                       $"LOD{mesh.Lod}".Contains(term, StringComparison.OrdinalIgnoreCase))) ||
               asset.Content.TextureSets.SelectMany(static set => set.Components).Any(component =>
                   component.FileName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                   component.MapType.ToString().Contains(term, StringComparison.OrdinalIgnoreCase)) ||
               asset.Content.Issues.Any(issue => issue.Message.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public static bool MatchesInventoryFilter(AssetSummary asset, AssetInventoryFilter filter)
    {
        var content = asset.Content;
        return (!filter.HasFlag(AssetInventoryFilter.HasFbx) || content.HasFbx) &&
               (!filter.HasFlag(AssetInventoryFilter.HasLods) || content.HasLods) &&
               (!filter.HasFlag(AssetInventoryFilter.HasBillboard) || content.HasBillboard) &&
               (!filter.HasFlag(AssetInventoryFilter.HasAtlas) || content.HasAtlas) &&
               (!filter.HasFlag(AssetInventoryFilter.Complete) || content.Completeness == AssetCompletenessStatus.Complete) &&
               (!filter.HasFlag(AssetInventoryFilter.Incomplete) || content.Completeness is not AssetCompletenessStatus.Complete and not AssetCompletenessStatus.Unknown) &&
               (!filter.HasFlag(AssetInventoryFilter.Ambiguous) || content.Completeness == AssetCompletenessStatus.Ambiguous);
    }
    public static IReadOnlyList<FolderNode> BuildFolderTree(
        string libraryRoot,
        IEnumerable<AssetSummary> assets)
    {
        var normalizedRoot = PathPolicy.Normalize(libraryRoot);
        var assetList = assets.ToArray();
        var root = new FolderNode(new DirectoryInfo(normalizedRoot).Name, normalizedRoot);
        var nodes = new Dictionary<string, FolderNode>(PathPolicy.Comparer)
        {
            [normalizedRoot] = root
        };

        foreach (var folder in assetList
                     .Select(static asset => PathPolicy.Normalize(asset.AssetFolderPath))
                     .Distinct(PathPolicy.Comparer)
                     .OrderBy(static path => path, PathPolicy.Comparer))
        {
            if (!IsBelowRoot(folder, normalizedRoot))
            {
                continue;
            }

            var relative = Path.GetRelativePath(normalizedRoot, folder);
            if (relative == ".")
            {
                continue;
            }

            var currentPath = normalizedRoot;
            var parent = root;
            foreach (var segment in relative.Split(
                         [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                currentPath = Path.Combine(currentPath, segment);
                if (!nodes.TryGetValue(currentPath, out var child))
                {
                    child = new FolderNode(segment, currentPath);
                    nodes[currentPath] = child;
                    parent.Children.Add(child);
                }

                parent = child;
            }
        }

        foreach (var asset in assetList)
        {
            var folder = PathPolicy.Normalize(asset.AssetFolderPath);
            if (!IsBelowRoot(folder, normalizedRoot))
            {
                continue;
            }

            root.AssetCount++;
            var relative = Path.GetRelativePath(normalizedRoot, folder);
            if (relative == ".")
            {
                continue;
            }

            var currentPath = normalizedRoot;
            foreach (var segment in relative.Split(
                         [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                currentPath = Path.Combine(currentPath, segment);
                if (nodes.TryGetValue(currentPath, out var node))
                {
                    node.AssetCount++;
                }
            }
        }

        SortRecursively(root);
        return [root];
    }

    private static bool IsBelowRoot(string path, string root) =>
        PathPolicy.Comparer.Equals(path, root) ||
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void SortRecursively(FolderNode node)
    {
        if (node.Children is List<FolderNode> children)
        {
            children.Sort(static (left, right) =>
                StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
        }

        foreach (var child in node.Children)
        {
            SortRecursively(child);
        }
    }
}
