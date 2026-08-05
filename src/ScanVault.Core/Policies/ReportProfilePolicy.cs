using ScanVault.Core.Models;

namespace ScanVault.Core.Policies;

public static class ReportProfilePolicy
{
    public static ReportDocument CreateDocument(ReportExportRequest request, DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var rows = CreateRows(request);
        var rowCount = EstimateRowCount(request);
        var metadata = new ReportMetadataDto(
            ReportContract.SchemaVersion,
            request.Profile.ToString(),
            request.Format.ToString(),
            generatedAtUtc,
            request.ApplicationVersion,
            request.CommitSha,
            request.DatabaseSchemaVersion,
            request.NormalizationVersion,
            request.ScanRun?.FingerprintVersion,
            request.IncludeAbsolutePaths ? request.ScanRun?.LibraryIdentity ?? NormalizeRoot(request.LibraryRoot) : null,
            request.IncludeAbsolutePaths ? NormalizeRoot(request.LibraryRoot) : null,
            request.Scope.ToString(),
            request.FilterSummary,
            request.SortSummary,
            AssetCount(request),
            rowCount,
            request.SmartCollection?.Name,
            request.SmartCollection?.Description,
            request.SmartCollection?.Definition.DefinitionVersion,
            request.SmartCollection?.Definition);

        return new(Title(request.Profile), metadata, rows);
    }

    public static long EstimateRowCount(ReportExportRequest request) => request.Profile switch
    {
        ReportProfile.AssetCatalog or ReportProfile.SmartCollectionResult => request.Assets.Count,
        ReportProfile.AssetInventory => request.Assets.Sum(InventoryRowCount),
        ReportProfile.IssuesReport => request.Assets.Sum(static asset => asset.Content.Issues.Count),
        ReportProfile.CompletenessReport => request.Assets.Count(asset => request.CompletenessStatuses.Contains(asset.Content.Completeness)),
        ReportProfile.ScanChanges => (request.ScanChanges ?? []).LongCount(change =>
            request.IncludeUnchangedScanItems || change.Kind != AssetChangeKind.Unchanged),
        _ => throw new ArgumentOutOfRangeException(nameof(request), request.Profile, "Unsupported report profile.")
    };

    public static string DefaultFileName(ReportProfile profile, ReportFormat format, DateTimeOffset timestamp) =>
        $"scanvault-{Slug(profile)}-{timestamp:yyyyMMdd-HHmmss}.{Extension(format)}";

    public static string Extension(ReportFormat format) => format switch
    {
        ReportFormat.Csv => "csv",
        ReportFormat.Json => "json",
        ReportFormat.Markdown => "md",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported report format.")
    };

    private static void Validate(ReportExportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DestinationPath))
        {
            throw new ArgumentException("A destination file is required.", nameof(request));
        }

        if (request.Profile == ReportProfile.ScanChanges)
        {
            if (request.ScanRun is null)
            {
                throw new InvalidOperationException("A scan run is required for a Scan Changes report.");
            }

            if (request.ScanRun.Status != ScanRunStatus.Completed)
            {
                throw new InvalidOperationException("Only completed scan runs can be exported.");
            }
        }

        if (request.Profile == ReportProfile.SmartCollectionResult && request.SmartCollection is null)
        {
            throw new InvalidOperationException("A smart collection is required for a Smart Collection Result report.");
        }
    }

    private static int AssetCount(ReportExportRequest request) => request.Profile switch
    {
        ReportProfile.IssuesReport => request.Assets.Count(static asset => asset.Content.Issues.Count > 0),
        ReportProfile.CompletenessReport => request.Assets.Count(asset =>
            request.CompletenessStatuses.Contains(asset.Content.Completeness)),
        ReportProfile.ScanChanges => request.ScanRun?.TotalAssets ?? 0,
        _ => request.Assets.Count
    };

    private static IEnumerable<IReportRow> CreateRows(ReportExportRequest request) => request.Profile switch
    {
        ReportProfile.AssetCatalog or ReportProfile.SmartCollectionResult => request.Assets.Select(asset => CatalogRow(request, asset)),
        ReportProfile.AssetInventory => request.Assets.SelectMany(asset => InventoryRows(request, asset)),
        ReportProfile.IssuesReport => request.Assets.SelectMany(asset => IssueRows(request, asset)),
        ReportProfile.CompletenessReport => request.Assets.Where(asset => request.CompletenessStatuses.Contains(asset.Content.Completeness))
            .Select(asset => CatalogRow(request, asset)),
        ReportProfile.ScanChanges => (request.ScanChanges ?? [])
            .Where(change => request.IncludeUnchangedScanItems || change.Kind != AssetChangeKind.Unchanged)
            .Select(change => ScanRow(request.ScanRun!, change)),
        _ => throw new ArgumentOutOfRangeException(nameof(request), request.Profile, "Unsupported report profile.")
    };

    private static AssetCatalogRowDto CatalogRow(ReportExportRequest request, AssetSummary asset)
    {
        var meshes = asset.Content.Variants.SelectMany(static variant => variant.Meshes).ToArray();
        return new(
            asset.Id,
            asset.Name,
            asset.AssetType,
            Relative(request.LibraryRoot, asset.AssetFolderPath),
            request.IncludeAbsolutePaths ? asset.AssetFolderPath : null,
            asset.Biome,
            asset.Region,
            asset.MaxResolution?.Width,
            asset.MaxResolution?.Height,
            asset.TexelDensity,
            asset.Content.Completeness.ToString(),
            asset.Content.Issues.Count > 0,
            asset.Content.HasFbx,
            meshes.Any(static mesh => mesh.Format == MeshFormat.Abc),
            asset.Content.HasLods,
            asset.Content.LodCount,
            asset.Content.VariantCount > 1,
            asset.Content.VariantCount,
            asset.Content.HasAtlas,
            asset.Content.HasBillboard,
            asset.Content.TextureSetCount,
            InventoryRowCount(asset),
            null);
    }

    private static IEnumerable<IReportRow> InventoryRows(ReportExportRequest request, AssetSummary asset)
    {
        var assetRelativePath = Relative(request.LibraryRoot, asset.AssetFolderPath);
        foreach (var variant in asset.Content.Variants)
        {
            foreach (var mesh in variant.Meshes)
            {
                yield return new AssetInventoryRowDto(
                    asset.Id, asset.Name, asset.AssetType, assetRelativePath,
                    request.IncludeAbsolutePaths ? asset.AssetFolderPath : null,
                    Relative(request.LibraryRoot, mesh.Path),
                    request.IncludeAbsolutePaths ? mesh.Path : null,
                    mesh.FileName, Path.GetExtension(mesh.FileName), "Mesh", null, null, null,
                    variant.Name, mesh.Lod, null, null, IssueFlags(asset, mesh.Path));
            }
        }

        foreach (var set in asset.Content.TextureSets)
        {
            foreach (var texture in set.Components)
            {
                yield return new AssetInventoryRowDto(
                    asset.Id, asset.Name, asset.AssetType, assetRelativePath,
                    request.IncludeAbsolutePaths ? asset.AssetFolderPath : null,
                    Relative(request.LibraryRoot, texture.Path),
                    request.IncludeAbsolutePaths ? texture.Path : null,
                    texture.FileName, Path.GetExtension(texture.FileName), set.Kind.ToString(), texture.MapType.ToString(),
                    texture.Resolution, texture.Resolution, null, null, null, null, IssueFlags(asset, texture.Path));
            }
        }

        foreach (var file in asset.Content.UnclassifiedFiles)
        {
            yield return new AssetInventoryRowDto(
                asset.Id, asset.Name, asset.AssetType, assetRelativePath,
                request.IncludeAbsolutePaths ? asset.AssetFolderPath : null,
                Relative(request.LibraryRoot, file.Path),
                request.IncludeAbsolutePaths ? file.Path : null,
                Path.GetFileName(file.Path), Path.GetExtension(file.Path), "Unclassified", null, null, null,
                null, null, null, null, IssueFlags(asset, file.Path));
        }
    }

    private static IEnumerable<IReportRow> IssueRows(ReportExportRequest request, AssetSummary asset) =>
        asset.Content.Issues.Select(issue => (IReportRow)new IssueReportRowDto(
            asset.Id,
            asset.Name,
            asset.AssetType,
            Relative(request.LibraryRoot, asset.AssetFolderPath),
            request.IncludeAbsolutePaths ? asset.AssetFolderPath : null,
            asset.Content.Completeness.ToString(),
            issue.Code.ToString(),
            IssueCategory(issue.Code),
            issue.Message,
            issue.Paths.Count > 0 ? Relative(request.LibraryRoot, issue.Paths[0]) : null,
            "Warning"));

    private static ScanChangeRowDto ScanRow(ScanRunSummary run, ScanChangeSummary change) => new(
        run.Id,
        run.StartedAtUtc,
        run.FinishedAtUtc,
        change.Kind.ToString(),
        change.AssetId,
        change.Name,
        change.AssetType,
        change.PreviousPath,
        change.CurrentPath,
        change.Flags.ToString(),
        change.Kind is AssetChangeKind.Removed ? change.Completeness.ToString() : null,
        change.Kind is not AssetChangeKind.Removed ? change.Completeness.ToString() : null);

    private static int InventoryRowCount(AssetSummary asset) =>
        asset.Content.MeshCount + asset.Content.TextureCount + asset.Content.UnclassifiedFiles.Count;

    private static string IssueFlags(AssetSummary asset, string path) => string.Join(
        ';',
        asset.Content.Issues
            .Where(issue => issue.Paths.Any(issuePath => PathPolicy.Comparer.Equals(issuePath, path)))
            .Select(static issue => issue.Code)
            .Distinct());

    private static string IssueCategory(AssetContentIssueCode code) => code switch
    {
        AssetContentIssueCode.DuplicateMesh or AssetContentIssueCode.DuplicateTexture or AssetContentIssueCode.ConflictingName => "Conflict",
        AssetContentIssueCode.MissingReference or AssetContentIssueCode.MissingCriticalFile => "MissingContent",
        AssetContentIssueCode.InaccessibleDirectory => "Access",
        AssetContentIssueCode.UnclassifiedFile => "Classification",
        _ => "Content"
    };

    private static string Relative(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var relative = Path.GetRelativePath(NormalizeRoot(root), Path.GetFullPath(path));
        return relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string NormalizeRoot(string root) =>
        Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string Title(ReportProfile profile) => profile switch
    {
        ReportProfile.AssetCatalog => "ScanVault Asset Catalog",
        ReportProfile.AssetInventory => "ScanVault Asset Inventory",
        ReportProfile.IssuesReport => "ScanVault Issues Report",
        ReportProfile.CompletenessReport => "ScanVault Completeness Report",
        ReportProfile.ScanChanges => "ScanVault Scan Changes",
        ReportProfile.SmartCollectionResult => "ScanVault Smart Collection Result",
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
    };

    private static string Slug(ReportProfile profile) => profile switch
    {
        ReportProfile.AssetCatalog => "asset-catalog",
        ReportProfile.AssetInventory => "asset-inventory",
        ReportProfile.IssuesReport => "issues-report",
        ReportProfile.CompletenessReport => "completeness-report",
        ReportProfile.ScanChanges => "scan-changes",
        ReportProfile.SmartCollectionResult => "smart-collection-result",
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
    };
}
