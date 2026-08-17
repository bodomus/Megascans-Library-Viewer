using System.Text.Json;
using ScanVault.Core.Models;

namespace ScanVault.Core.Policies;

public static class DuplicateAnalysisPolicy
{
    public static IReadOnlyList<DuplicateGroupResult> Classify(IReadOnlyList<DuplicateAssetFingerprint> assets)
    {
        var groups = new List<DuplicateGroupResult>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in assets
                     .GroupBy(static asset => asset.Asset.Id, StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1)
                     .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var members = group.OrderBy(static asset => asset.LibraryRelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
            var sameContent = members.Select(ContentSignature).Distinct(StringComparer.Ordinal).Count() == 1;
            groups.Add(CreateGroup(
                sameContent ? DuplicateCategory.ExactIdDuplicate : DuplicateCategory.ConflictingIdDuplicate,
                sameContent ? DuplicateConfidence.Exact : DuplicateConfidence.High,
                members,
                sameContent
                    ? [new("Asset ID", "Same normalized Asset ID and equivalent indexed file content.")]
                    : [new("Asset ID", "Same normalized Asset ID but indexed file content differs.")],
                ["Asset ID"],
                sameContent ? [] : ["Content inventory", "File fingerprints"]));
            foreach (var member in members) used.Add(member.Asset.JsonPath);
        }

        foreach (var group in assets
                     .Where(asset => asset.Files.Count > 0)
                     .GroupBy(ContentSignature, StringComparer.Ordinal)
                     .Where(static group => group.Count() > 1)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            var members = group.OrderBy(static asset => asset.LibraryRelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
            if (members.All(member => used.Contains(member.Asset.JsonPath))) continue;
            groups.Add(CreateGroup(
                DuplicateCategory.ExactContentDuplicate,
                DuplicateConfidence.Exact,
                members,
                [new("Content", "Different paths or IDs have identical indexed file hash sets.")],
                ["File hashes", "File sizes", "File count"],
                DifferentIdOrPath(members)));
            foreach (var member in members) used.Add(member.Asset.JsonPath);
        }

        foreach (var group in assets
                     .GroupBy(MetadataSignature, StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1)
                     .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var candidates = group
                .Where(asset => !used.Contains(asset.Asset.JsonPath))
                .OrderBy(static asset => asset.LibraryRelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (candidates.Length < 2) continue;

            var allPairs = candidates.SelectMany((left, index) => candidates.Skip(index + 1).Select(right => (left, right)));
            foreach (var pair in allPairs)
            {
                var overlap = Overlap(pair.left.ContentHashes, pair.right.ContentHashes);
                var fileNameOverlap = Overlap(pair.left.FileNames, pair.right.FileNames);
                var partial = overlap > 0 || fileNameOverlap >= 0.5;
                var probable = NamesSimilar(pair.left.Asset.Name, pair.right.Asset.Name) &&
                    pair.left.Asset.AssetType.Equals(pair.right.Asset.AssetType, StringComparison.OrdinalIgnoreCase);

                if (!partial && !probable) continue;

                var category = partial && overlap < 1 ? DuplicateCategory.PartialDuplicate : DuplicateCategory.ProbableDuplicate;
                var confidence = overlap >= 0.75 ? DuplicateConfidence.High : probable ? DuplicateConfidence.Medium : DuplicateConfidence.Low;
                groups.Add(CreateGroup(
                    category,
                    confidence,
                    [pair.left, pair.right],
                    [new("Similarity", ExplainSimilarity(overlap, fileNameOverlap, probable))],
                    Matched(pair.left, pair.right, overlap, fileNameOverlap, probable),
                    DifferentIdOrPath([pair.left, pair.right])));
            }
        }

        return groups
            .GroupBy(static group => string.Join("|", group.Members.Select(static member => member.RelativePath).Order(StringComparer.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group
                .OrderBy(static item => item.Category)
                .ThenByDescending(static item => item.Confidence)
                .First())
            .OrderBy(static group => group.Category)
            .ThenByDescending(static group => group.EstimatedDuplicateSizeBytes)
            .ThenBy(static group => group.GroupId, StringComparer.Ordinal)
            .ToArray();
    }

    private static DuplicateGroupResult CreateGroup(
        DuplicateCategory category,
        DuplicateConfidence confidence,
        IReadOnlyList<DuplicateAssetFingerprint> assets,
        IReadOnlyList<DuplicateReason> reasons,
        IReadOnlyList<string> matchedFields,
        IReadOnlyList<string> differentFields)
    {
        var members = assets
            .OrderBy(static asset => asset.LibraryRelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(static asset => new DuplicateGroupMember(
                asset.Asset.Id,
                asset.Asset.Name,
                asset.Asset.AssetType,
                asset.LibraryRelativePath,
                asset.Asset.AssetFolderPath,
                asset.Asset.Content.Completeness,
                asset.FileCount,
                asset.TotalSizeBytes,
                MergeHashStatus(asset.Files)))
            .ToArray();
        var duplicateSize = members.Length == 0 ? 0 : Math.Max(0, members.Sum(static member => member.TotalSizeBytes) - members.Max(static member => member.TotalSizeBytes));
        var identity = JsonSerializer.Serialize(new
        {
            category,
            members = members.Select(static member => member.RelativePath).Order(StringComparer.OrdinalIgnoreCase)
        });
        var groupId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)))[..16];
        return new(groupId, category, confidence, reasons, matchedFields, differentFields, duplicateSize, members);
    }

    private static string ContentSignature(DuplicateAssetFingerprint asset) =>
        string.Join("\n", asset.Files
            .OrderBy(static file => file.ContentHash, StringComparer.Ordinal)
            .ThenBy(static file => file.SizeBytes)
            .Select(static file => $"{file.ContentHash}:{file.SizeBytes}"));

    private static string MetadataSignature(DuplicateAssetFingerprint asset) =>
        string.Join("|", NormalizeName(asset.Asset.Name), asset.Asset.AssetType, asset.Asset.MaxResolution?.MaxDimension.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "", asset.Asset.Content.TextureSetCount, asset.Asset.Content.LodCount);

    private static string NormalizeName(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool NamesSimilar(string left, string right) =>
        NormalizeName(left).Equals(NormalizeName(right), StringComparison.OrdinalIgnoreCase);

    private static double Overlap(IReadOnlySet<string> left, IReadOnlySet<string> right)
    {
        if (left.Count == 0 || right.Count == 0) return 0;
        var intersection = left.Count(right.Contains);
        return (double)intersection / Math.Min(left.Count, right.Count);
    }

    private static string ExplainSimilarity(double hashOverlap, double fileNameOverlap, bool probable) =>
        $"Hash overlap {hashOverlap:P0}; filename overlap {fileNameOverlap:P0}; metadata match: {probable}.";

    private static List<string> Matched(DuplicateAssetFingerprint left, DuplicateAssetFingerprint right, double hashOverlap, double fileNameOverlap, bool probable)
    {
        var matched = new List<string>();
        if (probable) matched.Add("Metadata");
        if (hashOverlap > 0) matched.Add("File hashes");
        if (fileNameOverlap > 0) matched.Add("File names");
        if (left.Asset.AssetType.Equals(right.Asset.AssetType, StringComparison.OrdinalIgnoreCase)) matched.Add("Asset type");
        return matched;
    }

    private static List<string> DifferentIdOrPath(IReadOnlyList<DuplicateAssetFingerprint> members)
    {
        var different = new List<string>();
        if (members.Select(static item => item.Asset.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1) different.Add("Asset ID");
        if (members.Select(static item => item.LibraryRelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1) different.Add("Relative path");
        return different;
    }

    private static DuplicateHashStatus MergeHashStatus(IReadOnlyList<DuplicateFileFingerprint> files)
    {
        if (files.Count == 0) return DuplicateHashStatus.NotRequired;
        if (files.Any(static file => file.HashStatus == DuplicateHashStatus.Failed)) return DuplicateHashStatus.Failed;
        if (files.Any(static file => file.HashStatus == DuplicateHashStatus.Missing)) return DuplicateHashStatus.Missing;
        if (files.Any(static file => file.HashStatus == DuplicateHashStatus.Computed)) return DuplicateHashStatus.Computed;
        return DuplicateHashStatus.CacheHit;
    }
}
