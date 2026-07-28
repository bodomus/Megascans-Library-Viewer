using Microsoft.Extensions.Logging;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Infrastructure.Scanning;

public sealed class AssetContentInventoryService(
    ILogger<AssetContentInventoryService> logger) : IAssetContentInventoryService
{
    public Task<AssetInventoryResult> InventoryAsync(AssetSummary asset, CancellationToken cancellationToken) =>
        Task.Run(() => Inventory(asset, cancellationToken), cancellationToken);

    private AssetInventoryResult Inventory(AssetSummary asset, CancellationToken cancellationToken)
    {
        var files = new List<AssetContentFileCandidate>();
        var inaccessible = new List<string>();
        var pending = new Stack<string>();
        pending.Push(asset.AssetFolderPath);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            try
            {
                var info = new DirectoryInfo(directory);
                if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                foreach (var file in info.EnumerateFiles().OrderBy(static value => value.Name, StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    files.Add(new(file.FullName, Path.GetRelativePath(asset.AssetFolderPath, file.FullName)));
                }

                var children = info.EnumerateDirectories()
                    .Where(static child => !child.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    .OrderByDescending(static child => child.FullName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                foreach (var child in children) pending.Push(child.FullName);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                inaccessible.Add(directory);
                InfrastructureLog.CannotEnumerate(logger, directory, exception);
            }
        }

        var inventory = AssetContentAnalyzer.Analyze(
            asset.AssetType,
            files,
            asset.ReferencedContentPaths,
            inaccessible);
        return new(inventory, inaccessible);
    }
}
