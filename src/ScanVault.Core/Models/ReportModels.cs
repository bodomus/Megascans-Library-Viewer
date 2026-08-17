namespace ScanVault.Core.Models;

public enum ReportProfile
{
    AssetCatalog,
    AssetInventory,
    IssuesReport,
    CompletenessReport,
    ScanChanges,
    SmartCollectionResult
}

public enum ReportFormat { Csv, Json, Markdown }
public enum ReportScope { EntireLibrary, CurrentView, SelectedAsset }
public enum ReportExportPhase { PreparingQuery, ReadingAssets, WritingReport, Finalizing, Completed }

public static class ReportContract
{
    public const int SchemaVersion = 2;
}

public sealed record ReportMetadataDto(
    int ReportSchemaVersion,
    string ReportType,
    string ExportFormat,
    DateTimeOffset GeneratedAtUtc,
    string ApplicationVersion,
    string CommitSha,
    int? DatabaseSchemaVersion,
    int? NormalizationVersion,
    int? FingerprintVersion,
    string? LibraryIdentity,
    string? LibraryRoot,
    string SourceScope,
    string FilterSummary,
    string SortSummary,
    int AssetCount,
    long RowCount,
    string? SmartCollectionName = null,
    string? SmartCollectionDescription = null,
    int? SmartCollectionDefinitionVersion = null,
    SmartCollectionDefinition? SmartCollectionConditions = null);

public interface IReportRow;

public sealed record AssetCatalogRowDto(
    string AssetId,
    string Name,
    string AssetType,
    string LibraryRelativePath,
    string? AbsolutePath,
    string? Biome,
    string? Region,
    int? ResolutionWidth,
    int? ResolutionHeight,
    double? TexelDensity,
    string CompletenessStatus,
    bool HasIssues,
    bool HasFbx,
    bool HasAbc,
    bool HasLods,
    int LodCount,
    bool HasVariants,
    int VariantCount,
    bool HasAtlas,
    bool HasBillboard,
    int TextureSetCount,
    int FileCount,
    string UnrealReadinessStatus,
    int ReadinessRuleVersion,
    int BlockingIssueCount,
    int WarningCount,
    string ReadinessReasons,
    DateTimeOffset? LastIndexedAtUtc) : IReportRow;

public sealed record AssetInventoryRowDto(
    string AssetId,
    string AssetName,
    string AssetType,
    string AssetRelativePath,
    string? AssetAbsolutePath,
    string FileRelativePath,
    string? FileAbsolutePath,
    string FileName,
    string Extension,
    string FileCategory,
    string? TextureMapType,
    int? ResolutionWidth,
    int? ResolutionHeight,
    string? Variant,
    int? Lod,
    long? FileSizeBytes,
    DateTimeOffset? LastWriteTimeUtc,
    string IssueFlags) : IReportRow;

public sealed record IssueReportRowDto(
    string AssetId,
    string Name,
    string AssetType,
    string LibraryRelativePath,
    string? AbsolutePath,
    string CompletenessStatus,
    string IssueCode,
    string IssueCategory,
    string IssueMessage,
    string? RelatedFile,
    string Severity) : IReportRow;

public sealed record ScanChangeRowDto(
    string ScanRunId,
    DateTimeOffset ScanStartedAtUtc,
    DateTimeOffset? ScanFinishedAtUtc,
    string ChangeKind,
    string AssetId,
    string Name,
    string AssetType,
    string? PreviousPath,
    string? CurrentPath,
    string ChangeFlags,
    string? PreviousCompleteness,
    string? CurrentCompleteness) : IReportRow;

public sealed record ReportProgress(
    ReportExportPhase Phase,
    int ProcessedAssets,
    long WrittenRows,
    TimeSpan Elapsed);

public sealed record ReportExportResult(
    string DestinationPath,
    string? MetadataPath,
    int AssetCount,
    long RowCount,
    long OutputSizeBytes,
    TimeSpan Duration);

public sealed record ReportExportRequest(
    ReportProfile Profile,
    ReportFormat Format,
    ReportScope Scope,
    string DestinationPath,
    string LibraryRoot,
    bool IncludeAbsolutePaths,
    bool IncludeMetadata,
    bool PrettyJson,
    bool IncludeUnchangedScanItems,
    IReadOnlyList<AssetCompletenessStatus> CompletenessStatuses,
    IReadOnlyList<AssetSummary> Assets,
    string FilterSummary,
    string SortSummary,
    string ApplicationVersion,
    string CommitSha,
    int? DatabaseSchemaVersion,
    int? NormalizationVersion,
    ScanRunSummary? ScanRun = null,
    IReadOnlyList<ScanChangeSummary>? ScanChanges = null,
    SmartCollectionRecord? SmartCollection = null);

public sealed record ReportDocument(
    string Title,
    ReportMetadataDto Metadata,
    IEnumerable<IReportRow> Rows);
