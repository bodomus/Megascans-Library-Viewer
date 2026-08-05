using System.Globalization;
using ScanVault.Core.Models;

namespace ScanVault.Infrastructure.Reporting;

internal static class ReportRowProjection
{
    public static IReadOnlyList<KeyValuePair<string, object?>> Fields(IReportRow row) => row switch
    {
        AssetCatalogRowDto value =>
        [
            Pair("AssetId", value.AssetId), Pair("Name", value.Name), Pair("AssetType", value.AssetType),
            Pair("LibraryRelativePath", value.LibraryRelativePath), Pair("AbsolutePath", value.AbsolutePath),
            Pair("Biome", value.Biome), Pair("Region", value.Region), Pair("ResolutionWidth", value.ResolutionWidth),
            Pair("ResolutionHeight", value.ResolutionHeight), Pair("TexelDensity", value.TexelDensity),
            Pair("CompletenessStatus", value.CompletenessStatus), Pair("HasIssues", value.HasIssues),
            Pair("HasFbx", value.HasFbx), Pair("HasAbc", value.HasAbc), Pair("HasLods", value.HasLods),
            Pair("LodCount", value.LodCount), Pair("HasVariants", value.HasVariants), Pair("VariantCount", value.VariantCount),
            Pair("HasAtlas", value.HasAtlas), Pair("HasBillboard", value.HasBillboard),
            Pair("TextureSetCount", value.TextureSetCount), Pair("FileCount", value.FileCount),
            Pair("LastIndexedAtUtc", value.LastIndexedAtUtc)
        ],
        AssetInventoryRowDto value =>
        [
            Pair("AssetId", value.AssetId), Pair("AssetName", value.AssetName), Pair("AssetType", value.AssetType),
            Pair("AssetRelativePath", value.AssetRelativePath), Pair("AssetAbsolutePath", value.AssetAbsolutePath),
            Pair("FileRelativePath", value.FileRelativePath), Pair("FileAbsolutePath", value.FileAbsolutePath),
            Pair("FileName", value.FileName), Pair("Extension", value.Extension), Pair("FileCategory", value.FileCategory),
            Pair("TextureMapType", value.TextureMapType), Pair("ResolutionWidth", value.ResolutionWidth),
            Pair("ResolutionHeight", value.ResolutionHeight), Pair("Variant", value.Variant), Pair("Lod", value.Lod),
            Pair("FileSizeBytes", value.FileSizeBytes), Pair("LastWriteTimeUtc", value.LastWriteTimeUtc),
            Pair("IssueFlags", value.IssueFlags)
        ],
        IssueReportRowDto value =>
        [
            Pair("AssetId", value.AssetId), Pair("Name", value.Name), Pair("AssetType", value.AssetType),
            Pair("LibraryRelativePath", value.LibraryRelativePath), Pair("AbsolutePath", value.AbsolutePath),
            Pair("CompletenessStatus", value.CompletenessStatus), Pair("IssueCode", value.IssueCode),
            Pair("IssueCategory", value.IssueCategory), Pair("IssueMessage", value.IssueMessage),
            Pair("RelatedFile", value.RelatedFile), Pair("Severity", value.Severity)
        ],
        ScanChangeRowDto value =>
        [
            Pair("ScanRunId", value.ScanRunId), Pair("ScanStartedAtUtc", value.ScanStartedAtUtc),
            Pair("ScanFinishedAtUtc", value.ScanFinishedAtUtc), Pair("ChangeKind", value.ChangeKind),
            Pair("AssetId", value.AssetId), Pair("Name", value.Name), Pair("AssetType", value.AssetType),
            Pair("PreviousPath", value.PreviousPath), Pair("CurrentPath", value.CurrentPath),
            Pair("ChangeFlags", value.ChangeFlags), Pair("PreviousCompleteness", value.PreviousCompleteness),
            Pair("CurrentCompleteness", value.CurrentCompleteness)
        ],
        _ => throw new ArgumentException($"Unsupported report row type: {row.GetType().Name}.", nameof(row))
    };

    public static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTimeOffset date => date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        DateTime date => date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        bool boolean => boolean ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static KeyValuePair<string, object?> Pair(string name, object? value) => new(name, value);
}
