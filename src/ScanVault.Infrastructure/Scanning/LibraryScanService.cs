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
    ILogger<LibraryScanService> logger) : ILibraryScanService
{
    public async Task<ScanResult> ScanAsync(
        LibrarySettings settings,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var validation = SettingsValidator.Validate(settings);
        if (!validation.IsValid)
        {
            throw new ArgumentException(validation.Error, nameof(settings));
        }

        var stopwatch = Stopwatch.StartNew();
        var root = PathPolicy.Normalize(settings.LibraryRoot);
        InfrastructureLog.ScanStarted(logger, root);

        try
        {
            progress?.Report(new(ScanPhase.Discovering, 0, 0, root));
            var discovery = await scanner
                .DiscoverAsync(root, progress, cancellationToken)
                .ConfigureAwait(false);
            var assets = new List<AssetSummary>();
            var malformedPaths = new List<string>();
            var unrelated = 0;
            var processed = 0;

            foreach (var jsonPath in discovery.MetadataFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parseResult = await parser.ParseAsync(jsonPath, cancellationToken)
                    .ConfigureAwait(false);
                processed++;
                progress?.Report(new(
                    ScanPhase.Parsing,
                    discovery.MetadataFiles.Count,
                    processed,
                    jsonPath));

                switch (parseResult.Status)
                {
                    case AssetParseStatus.Success when parseResult.Asset is not null:
                        assets.Add(parseResult.Asset);
                        break;
                    case AssetParseStatus.MalformedJson:
                        malformedPaths.Add(jsonPath);
                        break;
                    case AssetParseStatus.UnrelatedJson:
                        unrelated++;
                        break;
                }
            }

            var resolution = DuplicateAssetResolver.Resolve(assets);
            foreach (var duplicate in resolution.DuplicateGroups)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    InfrastructureLog.DuplicateId(
                        logger,
                        duplicate.AssetId,
                        duplicate.WinnerJsonPath,
                        string.Join("; ", duplicate.SkippedCopyJsonPaths));
                }
            }

            var draft = new ScanResult(
                0,
                0,
                0,
                resolution.Assets.Count,
                malformedPaths.Count,
                unrelated,
                malformedPaths,
                discovery.InaccessibleDirectories,
                resolution.DuplicateGroups,
                stopwatch.Elapsed);

            progress?.Report(new(
                ScanPhase.Committing,
                discovery.MetadataFiles.Count,
                processed,
                root));
            var update = await index
                .ReplaceLibraryAsync(root, resolution.Assets, draft, cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();
            var result = draft with
            {
                AddedAssets = update.AddedAssets,
                UpdatedAssets = update.UpdatedAssets,
                RemovedAssets = update.RemovedAssets,
                Elapsed = stopwatch.Elapsed
            };
            progress?.Report(new(
                ScanPhase.Completed,
                discovery.MetadataFiles.Count,
                processed,
                root));
            InfrastructureLog.ScanCompleted(
                logger,
                root,
                result.AddedAssets,
                result.UpdatedAssets,
                result.RemovedAssets,
                result.Elapsed);
            return result;
        }
        catch (OperationCanceledException)
        {
            InfrastructureLog.ScanCancelled(logger, root);
            throw;
        }
        catch (Exception exception)
        {
            InfrastructureLog.ScanFailed(logger, root, exception);
            throw;
        }
    }
}
