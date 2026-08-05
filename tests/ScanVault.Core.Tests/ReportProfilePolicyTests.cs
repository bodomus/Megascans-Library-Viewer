using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Core.Tests;

public sealed class ReportProfilePolicyTests
{
    [Fact]
    public void CatalogUsesRelativePathsAndStableSchemaByDefault()
    {
        var root = Path.GetFullPath(Path.Combine("fixtures", "library"));
        var asset = TestAssetFactory.Create("asset-1", Path.Combine(root, "surface", "asset-1"));
        var request = CreateRequest(root, asset);

        var document = ReportProfilePolicy.CreateDocument(request, DateTimeOffset.UnixEpoch);
        var row = Assert.IsType<AssetCatalogRowDto>(Assert.Single(document.Rows));

        Assert.Equal(ReportContract.SchemaVersion, document.Metadata.ReportSchemaVersion);
        Assert.Equal("surface/asset-1", row.LibraryRelativePath);
        Assert.Null(row.AbsolutePath);
        Assert.Null(document.Metadata.LibraryRoot);
        Assert.Equal("scanvault-asset-catalog-20260730-181400.csv",
            ReportProfilePolicy.DefaultFileName(ReportProfile.AssetCatalog, ReportFormat.Csv,
                new DateTimeOffset(2026, 7, 30, 18, 14, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void ScanChangesRejectsNonCompletedRun()
    {
        var request = CreateRequest(Path.GetFullPath("fixtures")) with
        {
            Profile = ReportProfile.ScanChanges,
            ScanRun = CreateRun(ScanRunStatus.Cancelled)
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReportProfilePolicy.CreateDocument(request, DateTimeOffset.UtcNow));

        Assert.Contains("completed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SmartCollectionMetadataKeepsVersionedDefinition()
    {
        var collection = new SmartCollectionRecord(
            "user-1", SmartCollectionKind.User, "Forest", "Green assets",
            SmartCollectionDefinition.Empty with { SearchText = "forest" }, 0,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var request = CreateRequest(Path.GetFullPath("fixtures")) with
        {
            Profile = ReportProfile.SmartCollectionResult,
            SmartCollection = collection
        };

        var document = ReportProfilePolicy.CreateDocument(request, DateTimeOffset.UtcNow);

        Assert.Equal("Forest", document.Metadata.SmartCollectionName);
        Assert.Equal(SmartCollectionDefinition.CurrentVersion, document.Metadata.SmartCollectionDefinitionVersion);
        Assert.Equal("forest", document.Metadata.SmartCollectionConditions?.SearchText);
    }

    private static ReportExportRequest CreateRequest(string root, params AssetSummary[] assets) => new(
        ReportProfile.AssetCatalog, ReportFormat.Csv, ReportScope.EntireLibrary, Path.Combine(root, "report.csv"),
        root, false, true, true, false, [], assets, "None", AssetSortMode.NameAscending.ToString(),
        "1.0.0", "abcdef1", 4, 3);

    private static ScanRunSummary CreateRun(ScanRunStatus status) => new(
        "run-1", "library", "library", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, status,
        "1.0.0", "abcdef1", 4, 3, 1, 1, 1, 0, 0, 0, null, false);
}

