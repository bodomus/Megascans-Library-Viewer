using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Infrastructure.Scanning;

public sealed class LibraryScanService(
    IFileSystemScanner scanner,
    IAssetMetadataParser parser,
    IAssetIndex index,
    IScanBuildInfoProvider buildInfo,
    ILogger<LibraryScanService> logger,
    IAssetContentInventoryService? inventoryService = null) : ILibraryScanService
{
    private const int InventoryConcurrency = 4;

    public async Task<ScanResult> ScanAsync(
        LibrarySettings settings,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var validation = SettingsValidator.Validate(settings);
        if (!validation.IsValid) throw new ArgumentException(validation.Error, nameof(settings));

        var stopwatch = Stopwatch.StartNew();
        var root = PathPolicy.Normalize(settings.LibraryRoot);
        InfrastructureLog.ScanStarted(logger, root);
        var scanRunId = await index.BeginScanRunAsync(root, buildInfo.ApplicationVersion, buildInfo.CommitSha, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            progress?.Report(new(ScanPhase.Discovering, 0, 0, root));
            var discovery = await scanner.DiscoverAsync(root, progress, cancellationToken).ConfigureAwait(false);
            var parsedAssets = new List<AssetSummary>();
            var malformedPaths = new List<string>();
            var unrelated = 0;
            var processed = 0;

            foreach (var jsonPath in discovery.MetadataFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parseResult = await parser.ParseAsync(jsonPath, cancellationToken).ConfigureAwait(false);
                processed++;
                progress?.Report(new(ScanPhase.Parsing, discovery.MetadataFiles.Count, processed, jsonPath));
                switch (parseResult.Status)
                {
                    case AssetParseStatus.Success when parseResult.Asset is not null:
                        parsedAssets.Add(parseResult.Asset);
                        break;
                    case AssetParseStatus.MalformedJson:
                        malformedPaths.Add(jsonPath);
                        break;
                    case AssetParseStatus.UnrelatedJson:
                        unrelated++;
                        break;
                }
            }

            var resolution = DuplicateAssetResolver.Resolve(parsedAssets);
            foreach (var duplicate in resolution.DuplicateGroups)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                    InfrastructureLog.DuplicateId(logger, duplicate.AssetId, duplicate.WinnerJsonPath,
                        string.Join("; ", duplicate.SkippedCopyJsonPaths));
            }

            var allAssets = parsedAssets
                .OrderBy(static asset => asset.JsonPath, PathPolicy.Comparer)
                .ToArray();
            var winnerJsonPaths = resolution.Assets
                .Select(static asset => asset.JsonPath)
                .ToHashSet(PathPolicy.Comparer);
            var inventoryInaccessible = new ConcurrentBag<string>();
            if (inventoryService is not null && allAssets.Length > 0)
            {
                InfrastructureLog.InventoryStarted(logger, allAssets.Length);
                var inventoried = 0;
                await Parallel.ForEachAsync(
                    Enumerable.Range(0, allAssets.Length),
                    new ParallelOptions { MaxDegreeOfParallelism = InventoryConcurrency, CancellationToken = cancellationToken },
                    async (indexValue, token) =>
                    {
                        var result = await inventoryService.InventoryAsync(allAssets[indexValue], token).ConfigureAwait(false);
                        allAssets[indexValue] = allAssets[indexValue] with { Content = result.Inventory };
                        foreach (var directory in result.InaccessibleDirectories) inventoryInaccessible.Add(directory);
                        var current = Interlocked.Increment(ref inventoried);
                        progress?.Report(new(ScanPhase.Inventory, allAssets.Length, current, allAssets[indexValue].AssetFolderPath));
                    }).ConfigureAwait(false);
            }

            var assets = allAssets
                .Where(asset => winnerJsonPaths.Contains(asset.JsonPath))
                .OrderBy(static asset => asset.JsonPath, PathPolicy.Comparer)
                .ToArray();
            foreach (var asset in assets)
            {
                foreach (var issue in asset.Content.Issues.Where(static issue =>
                             issue.Code is AssetContentIssueCode.DuplicateMesh or
                                 AssetContentIssueCode.DuplicateTexture or
                                 AssetContentIssueCode.ConflictingName or
                                 AssetContentIssueCode.UnclassifiedFile))
                {
                    InfrastructureLog.InventoryIssue(logger, asset.Id, issue.Code, issue.Message, string.Join("; ", issue.Paths));
                }
            }
            var allInaccessible = discovery.InaccessibleDirectories
                .Concat(inventoryInaccessible)
                .Distinct(PathPolicy.Comparer)
                .OrderBy(static path => path, PathPolicy.Comparer)
                .ToArray();
            var draft = new ScanResult(
                0, 0, 0, assets.Length, malformedPaths.Count, unrelated, malformedPaths,
                allInaccessible, resolution.DuplicateGroups, stopwatch.Elapsed)
            {
                DuplicateAnalysisSources = allAssets,
                AssetsInventoried = inventoryService is null ? 0 : allAssets.Length,
                MeshFilesFound = allAssets.Sum(static asset => asset.Content.MeshCount),
                TextureFilesFound = allAssets.Sum(static asset => asset.Content.TextureCount),
                AmbiguousAssets = allAssets.Count(static asset => asset.Content.Completeness == AssetCompletenessStatus.Ambiguous),
                AssetsMissingCriticalFiles = allAssets.Count(static asset => asset.Content.Completeness == AssetCompletenessStatus.MissingCriticalFiles)
            };

            if (inventoryService is not null)
            {
                InfrastructureLog.InventoryCompleted(logger, draft.AssetsInventoried, draft.MeshFilesFound,
                    draft.TextureFilesFound, draft.AmbiguousAssets, draft.AssetsMissingCriticalFiles);
            }
            progress?.Report(new(ScanPhase.Committing, discovery.MetadataFiles.Count, processed, root));
            var update = await index.ReplaceLibraryAsync(root, assets, draft, scanRunId, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            var result = draft with
            {
                AddedAssets = update.AddedAssets,
                UpdatedAssets = update.UpdatedAssets,
                RemovedAssets = update.RemovedAssets,
                ChangedAssets = update.ChangedAssets,
                UnchangedAssets = update.UnchangedAssets,
                IsInitialBaseline = update.IsInitialBaseline,
                ScanRunId = update.ScanRunId,
                Elapsed = stopwatch.Elapsed
            };
            progress?.Report(new(ScanPhase.Completed, discovery.MetadataFiles.Count, processed, root));
            InfrastructureLog.ScanCompleted(logger, root, result.AddedAssets, result.UpdatedAssets, result.RemovedAssets, result.Elapsed);
            return result;
        }
        catch (OperationCanceledException)
        {
            await index.FinishScanRunAsync(scanRunId, ScanRunStatus.Cancelled, "Scan cancelled by user.", CancellationToken.None).ConfigureAwait(false);
            InfrastructureLog.ScanCancelled(logger, root);
            throw;
        }
        catch (Exception exception)
        {
            await index.FinishScanRunAsync(scanRunId, ScanRunStatus.Failed, exception.Message, CancellationToken.None).ConfigureAwait(false);
            InfrastructureLog.ScanFailed(logger, root, exception);
            throw;
        }
    }
}
