using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Core.Tests;

public sealed class DuplicateAnalysisPolicyTests
{
    [Fact]
    public void SameIdAndEquivalentContentIsExactIdDuplicate()
    {
        var left = Fingerprint("same", "Oak Rock", @"A\asset.json", [File("a.fbx", "H1", 10)]);
        var right = Fingerprint("SAME", "Oak Rock Copy", @"B\asset.json", [File("copy.fbx", "H1", 10)]);

        var group = Assert.Single(DuplicateAnalysisPolicy.Classify([right, left]));

        Assert.Equal(DuplicateCategory.ExactIdDuplicate, group.Category);
        Assert.Equal(DuplicateConfidence.Exact, group.Confidence);
        Assert.Equal(10, group.EstimatedDuplicateSizeBytes);
    }

    [Fact]
    public void SameIdWithDifferentContentIsConflictingIdDuplicate()
    {
        var left = Fingerprint("same", "Oak Rock", @"A\asset.json", [File("a.fbx", "H1", 10)]);
        var right = Fingerprint("same", "Oak Rock", @"B\asset.json", [File("b.fbx", "H2", 10)]);

        var group = Assert.Single(DuplicateAnalysisPolicy.Classify([left, right]));

        Assert.Equal(DuplicateCategory.ConflictingIdDuplicate, group.Category);
        Assert.Equal(DuplicateConfidence.High, group.Confidence);
        Assert.Contains("File fingerprints", group.DifferentFields);
    }

    [Fact]
    public void ExactContentWithDifferentIdsIsExactContentDuplicateRegardlessOfFileOrder()
    {
        var left = Fingerprint("left", "Oak Rock", @"A\asset.json", [File("b.png", "H2", 2), File("a.fbx", "H1", 10)]);
        var right = Fingerprint("right", "Different Name", @"B\asset.json", [File("x.fbx", "H1", 10), File("y.png", "H2", 2)]);

        var group = Assert.Single(DuplicateAnalysisPolicy.Classify([left, right]));

        Assert.Equal(DuplicateCategory.ExactContentDuplicate, group.Category);
        Assert.Equal(DuplicateConfidence.Exact, group.Confidence);
        Assert.Equal(12, group.EstimatedDuplicateSizeBytes);
    }

    [Fact]
    public void MissingOrFailedHashesDoNotProduceExactContentDuplicate()
    {
        var left = Fingerprint("left", "Oak Rock", @"A\asset.json", [File("a.fbx", "", 10, DuplicateHashStatus.Failed)]);
        var right = Fingerprint("right", "Oak Rock", @"B\asset.json", [File("b.fbx", "", 10, DuplicateHashStatus.Missing)]);

        var groups = DuplicateAnalysisPolicy.Classify([left, right]);

        Assert.DoesNotContain(groups, group => group.Confidence == DuplicateConfidence.Exact);
    }

    [Fact]
    public void SameIdWithIncompleteHashEvidenceFallsBackToConflictingId()
    {
        var left = Fingerprint("same", "Oak Rock", @"A\asset.json", [File("a.fbx", "H1", 10)]);
        var right = Fingerprint("same", "Oak Rock", @"B\asset.json", [File("b.fbx", "", 10, DuplicateHashStatus.Failed)]);

        var group = Assert.Single(DuplicateAnalysisPolicy.Classify([left, right]));

        Assert.Equal(DuplicateCategory.ConflictingIdDuplicate, group.Category);
        Assert.Equal(DuplicateConfidence.High, group.Confidence);
        Assert.Contains(group.Reasons, reason => reason.Message.Contains("incomplete", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Hash availability", group.DifferentFields);
    }

    [Fact]
    public void SameIdAcceptsComputedAndCacheHitHashesAsExactEvidence()
    {
        var left = Fingerprint("same", "Oak Rock", @"A\asset.json", [File("a.fbx", "H1", 10, DuplicateHashStatus.Computed)]);
        var right = Fingerprint("same", "Oak Rock", @"B\asset.json", [File("b.fbx", "H1", 10, DuplicateHashStatus.CacheHit)]);

        var group = Assert.Single(DuplicateAnalysisPolicy.Classify([left, right]));

        Assert.Equal(DuplicateCategory.ExactIdDuplicate, group.Category);
        Assert.Equal(DuplicateConfidence.Exact, group.Confidence);
    }

    [Fact]
    public void MixedValidAndFailedHashEvidenceIsNotExact()
    {
        var left = Fingerprint("left", "Oak Rock", @"A\asset.json", [File("a.fbx", "H1", 10), File("a.png", "", 2, DuplicateHashStatus.Failed)]);
        var right = Fingerprint("right", "Oak Rock", @"B\asset.json", [File("b.fbx", "H1", 10), File("b.png", "", 2, DuplicateHashStatus.Failed)]);

        var groups = DuplicateAnalysisPolicy.Classify([left, right]);

        Assert.DoesNotContain(groups, group => group.Confidence == DuplicateConfidence.Exact);
    }

    [Fact]
    public void PartialOverlapIsNotReportedAsExact()
    {
        var left = Fingerprint("left", "Oak Rock", @"A\asset.json", [File("mesh.fbx", "H1", 10), File("albedo.png", "H2", 2)]);
        var right = Fingerprint("right", "Oak Rock", @"B\asset.json", [File("mesh.fbx", "H1", 10), File("normal.png", "H3", 3)]);

        var group = Assert.Single(DuplicateAnalysisPolicy.Classify([left, right]));

        Assert.Equal(DuplicateCategory.PartialDuplicate, group.Category);
        Assert.NotEqual(DuplicateConfidence.Exact, group.Confidence);
        Assert.Contains("File hashes", group.MatchedFields);
    }

    private static DuplicateAssetFingerprint Fingerprint(
        string id,
        string name,
        string relativePath,
        IReadOnlyList<DuplicateFileFingerprint> files)
    {
        var asset = TestAssetFactory.Create(id, Path.GetDirectoryName(relativePath) ?? ".") with
        {
            Name = name,
            AssetType = "Surface"
        };
        return new(asset, relativePath, files);
    }

    private static DuplicateFileFingerprint File(
        string relativePath,
        string hash,
        long size,
        DuplicateHashStatus status = DuplicateHashStatus.Computed) =>
        new(relativePath, size, DateTimeOffset.UnixEpoch, hash, status);
}
