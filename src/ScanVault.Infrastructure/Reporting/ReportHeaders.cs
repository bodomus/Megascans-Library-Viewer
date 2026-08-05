using ScanVault.Core.Models;

namespace ScanVault.Infrastructure.Reporting;

internal static class ReportHeaders
{
    private static readonly string[] Catalog =
    [
        "AssetId", "Name", "AssetType", "LibraryRelativePath", "AbsolutePath", "Biome", "Region",
        "ResolutionWidth", "ResolutionHeight", "TexelDensity", "CompletenessStatus", "HasIssues", "HasFbx",
        "HasAbc", "HasLods", "LodCount", "HasVariants", "VariantCount", "HasAtlas", "HasBillboard",
        "TextureSetCount", "FileCount", "LastIndexedAtUtc"
    ];

    private static readonly string[] Inventory =
    [
        "AssetId", "AssetName", "AssetType", "AssetRelativePath", "AssetAbsolutePath", "FileRelativePath",
        "FileAbsolutePath", "FileName", "Extension", "FileCategory", "TextureMapType", "ResolutionWidth",
        "ResolutionHeight", "Variant", "Lod", "FileSizeBytes", "LastWriteTimeUtc", "IssueFlags"
    ];

    private static readonly string[] Issues =
    [
        "AssetId", "Name", "AssetType", "LibraryRelativePath", "AbsolutePath", "CompletenessStatus",
        "IssueCode", "IssueCategory", "IssueMessage", "RelatedFile", "Severity"
    ];

    private static readonly string[] ScanChanges =
    [
        "ScanRunId", "ScanStartedAtUtc", "ScanFinishedAtUtc", "ChangeKind", "AssetId", "Name", "AssetType",
        "PreviousPath", "CurrentPath", "ChangeFlags", "PreviousCompleteness", "CurrentCompleteness"
    ];

    public static IReadOnlyList<string> For(ReportProfile profile) => profile switch
    {
        ReportProfile.AssetCatalog or ReportProfile.CompletenessReport or ReportProfile.SmartCollectionResult => Catalog,
        ReportProfile.AssetInventory => Inventory,
        ReportProfile.IssuesReport => Issues,
        ReportProfile.ScanChanges => ScanChanges,
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unsupported report profile.")
    };
}
