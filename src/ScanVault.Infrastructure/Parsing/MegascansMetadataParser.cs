using System.Text.Json;
using Microsoft.Extensions.Logging;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Infrastructure.Parsing;

public sealed class MegascansMetadataParser(
    ILogger<MegascansMetadataParser> logger) : IAssetMetadataParser
{
    public async Task<AssetParseResult> ParseAsync(
        string jsonPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                jsonPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var document = await JsonDocument.ParseAsync(
                stream,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                    MaxDepth = 128
                },
                cancellationToken).ConfigureAwait(false);

            var root = document.RootElement;
            var id = JsonElementSearch.FindString(root, "id", "assetId", "asset_id");
            var name = JsonElementSearch.FindString(root, "name", "assetName", "title");
            var assetType = JsonElementSearch.FindString(root, "assetType", "type");

            // Requiring an ID plus a recognizable descriptive field prevents
            // unrelated application JSON from becoming a library asset.
            if (string.IsNullOrWhiteSpace(id) ||
                string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(assetType))
            {
                return AssetParseResult.Unrelated();
            }

            var folder = PathPolicy.Normalize(Path.GetDirectoryName(jsonPath)
                ?? throw new InvalidDataException("Metadata path has no parent directory."));
            var paths = PreviewPathResolver.Resolve(root, folder, id);
            var categories = JsonElementSearch.FindStrings(
                root,
                "categories",
                "assetCategories",
                "category");
            var tags = BuildTags(root, categories);

            var asset = new AssetSummary(
                id,
                name ?? id,
                assetType ?? "Unknown",
                folder,
                PathPolicy.Normalize(jsonPath),
                paths.ThumbnailPath,
                paths.PreviewPath,
                JsonElementSearch.FindString(root, "biome"),
                JsonElementSearch.FindString(root, "region"),
                JsonElementSearch.FindString(root, "physicalSize", "physical_size"),
                JsonElementSearch.FindResolution(root, "maxResolution", "maximumResolution", "resolution"),
                JsonElementSearch.FindDouble(root, "texelDensity", "texel_density"),
                JsonElementSearch.FindString(root, "averageColor", "average_color"),
                categories,
                tags,
                new FileInfo(jsonPath).LastWriteTimeUtc);

            return AssetParseResult.Success(asset);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            InfrastructureLog.MalformedJson(logger, jsonPath, exception);
            return AssetParseResult.Malformed(exception.Message);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            InfrastructureLog.CannotReadJson(logger, jsonPath, exception);
            return AssetParseResult.Malformed(exception.Message);
        }
    }

    private static IReadOnlyList<AssetTag> BuildTags(
        JsonElement root,
        IReadOnlyList<string> categories)
    {
        var tags = new List<AssetTag>();
        tags.AddRange(categories.Select(static value => new AssetTag(AssetTagKind.Category, value)));
        Add(tags, root, AssetTagKind.Descriptive, "descriptiveTags", "semanticTags", "tags");
        Add(tags, root, AssetTagKind.State, "stateTags", "condition", "state");
        Add(tags, root, AssetTagKind.Color, "colorTags", "colors");
        Add(tags, root, AssetTagKind.Industry, "industryTags", "industries");
        Add(tags, root, AssetTagKind.Contains, "containsTags", "contains");
        Add(tags, root, AssetTagKind.Theme, "themeTags", "themes");
        return TagNormalizer.Normalize(tags);
    }

    private static void Add(
        List<AssetTag> tags,
        JsonElement root,
        AssetTagKind kind,
        params string[] propertyNames)
    {
        foreach (var value in JsonElementSearch.FindStrings(root, propertyNames))
        {
            tags.Add(new(kind, value));
        }
    }
}
