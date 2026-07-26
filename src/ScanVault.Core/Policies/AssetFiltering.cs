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

    public static IReadOnlyList<FolderNode> BuildFolderTree(
        string libraryRoot,
        IEnumerable<AssetSummary> assets)
    {
        var normalizedRoot = PathPolicy.Normalize(libraryRoot);
        var root = new FolderNode(new DirectoryInfo(normalizedRoot).Name, normalizedRoot);
        var nodes = new Dictionary<string, FolderNode>(PathPolicy.Comparer)
        {
            [normalizedRoot] = root
        };

        foreach (var folder in assets
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
