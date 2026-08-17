using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Infrastructure.Scanning;

public sealed class DuplicateAnalysisService(
    IAssetIndex index,
    ILogger<DuplicateAnalysisService> logger) : IDuplicateAnalysisService
{
    private const string HashAlgorithm = "SHA-256";
    private const int HashAlgorithmVersion = 1;
    private const int HashConcurrency = 2;

    public async Task<DuplicateAnalysisResult> AnalyzeAsync(
        LibrarySettings settings,
        IProgress<DuplicateAnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        var validation = SettingsValidator.Validate(settings);
        if (!validation.IsValid) throw new ArgumentException(validation.Error, nameof(settings));

        var stopwatch = Stopwatch.StartNew();
        var libraryRoot = PathPolicy.Normalize(settings.LibraryRoot);
        progress?.Report(new(DuplicateAnalysisPhase.LoadingAssets, 0, 0, 0, 0, 0, libraryRoot));
        var assets = await index.GetAssetsAsync(cancellationToken).ConfigureAwait(false);
        var run = await index.BeginDuplicateAnalysisRunAsync(libraryRoot, assets.Count, cancellationToken).ConfigureAwait(false);

        try
        {
            progress?.Report(new(DuplicateAnalysisPhase.GeneratingCandidates, 0, 0, 0, 0, 0, libraryRoot));
            var candidates = SelectCandidates(assets);
            var files = candidates.SelectMany(CollectCandidateFiles).DistinctBy(static file => file.FullPath, PathPolicy.Comparer).ToArray();
            var totalBytes = files.Sum(static file => file.SizeBytes);
            var hashes = new Dictionary<string, DuplicateFileFingerprint>(PathPolicy.Comparer);
            var processedFiles = 0;
            var processedBytes = 0L;
            var computed = 0;
            var cacheHits = 0;

            await Parallel.ForEachAsync(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = HashConcurrency, CancellationToken = cancellationToken },
                async (file, token) =>
                {
                    var fingerprint = await HashFileAsync(file, token).ConfigureAwait(false);
                    lock (hashes)
                    {
                        hashes[file.FullPath] = fingerprint;
                        if (fingerprint.HashStatus == DuplicateHashStatus.CacheHit) cacheHits++;
                        if (fingerprint.HashStatus == DuplicateHashStatus.Computed) computed++;
                    }

                    var currentFiles = Interlocked.Increment(ref processedFiles);
                    var currentBytes = Interlocked.Add(ref processedBytes, file.SizeBytes);
                    progress?.Report(new(DuplicateAnalysisPhase.Hashing, currentFiles, files.Length, currentBytes, totalBytes, 0, file.FullPath));
                }).ConfigureAwait(false);

            progress?.Report(new(DuplicateAnalysisPhase.Classifying, processedFiles, files.Length, processedBytes, totalBytes, 0));
            var fingerprints = candidates.Select(asset => new DuplicateAssetFingerprint(
                    asset,
                    NormalizeRelativePath(libraryRoot, asset.AssetFolderPath),
                    CollectCandidateFiles(asset)
                        .Select(file => hashes.TryGetValue(file.FullPath, out var value) ? value : MissingFingerprint(file, libraryRoot))
                        .OrderBy(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                        .ToArray()))
                .ToArray();
            var groups = DuplicateAnalysisPolicy.Classify(fingerprints);
            var summary = CreateSummary(groups);
            stopwatch.Stop();
            var completedRun = run with
            {
                FinishedAtUtc = DateTimeOffset.UtcNow,
                Status = DuplicateAnalysisStatus.Completed,
                CandidateAssets = candidates.Length,
                FilesHashed = computed,
                BytesHashed = fingerprints.SelectMany(static asset => asset.Files)
                    .Where(static file => file.HashStatus == DuplicateHashStatus.Computed)
                    .Sum(static file => file.SizeBytes),
                CacheHits = cacheHits,
                Duration = stopwatch.Elapsed,
                Summary = summary
            };
            var result = new DuplicateAnalysisResult(completedRun, groups);
            progress?.Report(new(DuplicateAnalysisPhase.Persisting, processedFiles, files.Length, processedBytes, totalBytes, groups.Count));
            await index.PersistDuplicateAnalysisAsync(new(completedRun, groups), cancellationToken).ConfigureAwait(false);
            progress?.Report(new(DuplicateAnalysisPhase.Completed, processedFiles, files.Length, processedBytes, totalBytes, groups.Count));
            return result;
        }
        catch (OperationCanceledException)
        {
            await index.FinishDuplicateAnalysisRunAsync(run.Id, DuplicateAnalysisStatus.Cancelled, "Duplicate analysis cancelled by user.", CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await index.FinishDuplicateAnalysisRunAsync(run.Id, DuplicateAnalysisStatus.Failed, exception.Message, CancellationToken.None).ConfigureAwait(false);
            InfrastructureLog.DuplicateAnalysisFailed(logger, libraryRoot, exception);
            throw;
        }
    }

    private async Task<DuplicateFileFingerprint> HashFileAsync(FileCandidate file, CancellationToken cancellationToken)
    {
        var cached = await index.GetFileHashAsync(file.FullPath, HashAlgorithm, HashAlgorithmVersion, cancellationToken).ConfigureAwait(false);
        if (cached is not null &&
            cached.FileSizeBytes == file.SizeBytes &&
            cached.LastWriteTimeUtc == file.LastWriteTimeUtc)
        {
            return new(file.RelativePath, file.SizeBytes, file.LastWriteTimeUtc, cached.ContentHash, DuplicateHashStatus.CacheHit);
        }

        try
        {
            await using var stream = new FileStream(
                file.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                1024 * 128,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            await index.UpsertFileHashAsync(
                new(file.FullPath, file.SizeBytes, file.LastWriteTimeUtc, HashAlgorithm, HashAlgorithmVersion, hash, DateTimeOffset.UtcNow),
                cancellationToken).ConfigureAwait(false);
            return new(file.RelativePath, file.SizeBytes, file.LastWriteTimeUtc, hash, DuplicateHashStatus.Computed);
        }
        catch (FileNotFoundException)
        {
            return new(file.RelativePath, file.SizeBytes, file.LastWriteTimeUtc, string.Empty, DuplicateHashStatus.Missing);
        }
        catch (DirectoryNotFoundException)
        {
            return new(file.RelativePath, file.SizeBytes, file.LastWriteTimeUtc, string.Empty, DuplicateHashStatus.Missing);
        }
        catch (IOException)
        {
            return new(file.RelativePath, file.SizeBytes, file.LastWriteTimeUtc, string.Empty, DuplicateHashStatus.Failed);
        }
        catch (UnauthorizedAccessException)
        {
            return new(file.RelativePath, file.SizeBytes, file.LastWriteTimeUtc, string.Empty, DuplicateHashStatus.Failed);
        }
    }

    private static AssetSummary[] SelectCandidates(IReadOnlyList<AssetSummary> assets)
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in assets.GroupBy(static asset => asset.Id, StringComparer.OrdinalIgnoreCase).Where(static group => group.Count() > 1))
            foreach (var asset in group) selected.Add(asset.JsonPath);
        foreach (var group in assets.GroupBy(FastSignature, StringComparer.OrdinalIgnoreCase).Where(static group => group.Count() > 1))
            foreach (var asset in group) selected.Add(asset.JsonPath);
        foreach (var group in assets.GroupBy(static asset => NormalizeName(asset.Name), StringComparer.OrdinalIgnoreCase).Where(static group => group.Count() > 1))
            foreach (var asset in group) selected.Add(asset.JsonPath);
        return assets.Where(asset => selected.Contains(asset.JsonPath)).OrderBy(static asset => asset.JsonPath, PathPolicy.Comparer).ToArray();
    }

    private static string FastSignature(AssetSummary asset) => string.Join("|",
        asset.AssetType,
        asset.Content.MeshCount.ToString(CultureInfo.InvariantCulture),
        asset.Content.TextureCount.ToString(CultureInfo.InvariantCulture),
        asset.Content.TextureSetCount.ToString(CultureInfo.InvariantCulture),
        asset.Content.LodCount.ToString(CultureInfo.InvariantCulture),
        asset.MaxResolution?.MaxDimension.ToString(CultureInfo.InvariantCulture) ?? "");

    private static IEnumerable<FileCandidate> CollectCandidateFiles(AssetSummary asset)
    {
        foreach (var mesh in asset.Content.Variants.SelectMany(static item => item.Meshes))
            if (TryCreateFile(asset, mesh.Path, out var file)) yield return file;
        foreach (var component in asset.Content.TextureSets.SelectMany(static item => item.Components))
            if (TryCreateFile(asset, component.Path, out var file)) yield return file;
        foreach (var item in asset.Content.UnclassifiedFiles)
            if (TryCreateFile(asset, item.Path, out var file)) yield return file;
    }

    private static bool TryCreateFile(AssetSummary asset, string fullPath, out FileCandidate file)
    {
        file = null!;
        try
        {
            var info = new FileInfo(fullPath);
            if (!info.Exists) return false;
            file = new(fullPath, NormalizeRelativePath(asset.AssetFolderPath, fullPath), info.Length, info.LastWriteTimeUtc);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static DuplicateFileFingerprint MissingFingerprint(FileCandidate file, string libraryRoot) =>
        new(NormalizeRelativePath(libraryRoot, file.FullPath), file.SizeBytes, file.LastWriteTimeUtc, string.Empty, DuplicateHashStatus.Missing);

    private static DuplicateAnalysisSummary CreateSummary(IReadOnlyList<DuplicateGroupResult> groups) =>
        new(
            groups.Count(static group => group.Category is DuplicateCategory.ExactIdDuplicate or DuplicateCategory.ExactContentDuplicate),
            groups.Count(static group => group.Category == DuplicateCategory.ConflictingIdDuplicate),
            groups.Count(static group => group.Category == DuplicateCategory.ProbableDuplicate),
            groups.Count(static group => group.Category == DuplicateCategory.PartialDuplicate),
            groups.SelectMany(static group => group.Members.Select(static member => member.AssetId + "\0" + member.RelativePath)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            groups.Sum(static group => group.EstimatedDuplicateSizeBytes));

    private static string NormalizeName(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string NormalizeRelativePath(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private sealed record FileCandidate(string FullPath, string RelativePath, long SizeBytes, DateTimeOffset LastWriteTimeUtc);
}
