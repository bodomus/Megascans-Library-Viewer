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
            var id = JsonElementSearch.GetDirectString(root, "id", "assetId", "asset_id");
            var name = JsonElementSearch.GetDirectString(root, "name", "assetName", "title");
            var semanticTags = GetObject(root, "semanticTags", "semantic_tags");
            var rootCategories = JsonElementSearch.GetDirectStrings(root, "categories", "category");
            var assetCategoryKeys = GetAssetCategoryKeys(root);

            // Precedence is intentionally explicit: no unrestricted recursive "type" lookup
            // can allow a texture component such as normal/specular to classify the asset.
            var typeCandidates = new List<string?>
            {
                semanticTags is { } semantic
                    ? JsonElementSearch.GetDirectString(semantic, "asset_type", "assetType")
                    : null,
                JsonElementSearch.GetDirectString(root, "assetType", "asset_type", "type")
            };
            typeCandidates.AddRange(JsonElementSearch.GetDirectStrings(
                root,
                "classification",
                "rootCategory"));
            typeCandidates.AddRange(assetCategoryKeys);
            typeCandidates.AddRange(rootCategories);
            var type = MetadataNormalizer.ResolveAssetType(typeCandidates);

            // Requiring an ID plus a recognizable descriptive field prevents
            // unrelated application JSON from becoming a library asset.
            if (id is null || name is null && type.Canonical == "Unknown")
            {
                return AssetParseResult.Unrelated();
            }

            var folder = PathPolicy.Normalize(Path.GetDirectoryName(jsonPath)
                ?? throw new InvalidDataException("Metadata path has no parent directory."));
            var paths = PreviewPathResolver.Resolve(root, folder, id);
            var categories = MetadataNormalizer.NormalizeValues(
                rootCategories.Count > 0 ? rootCategories : assetCategoryKeys,
                name);
            var resolution = ReadMaximumResolution(root, jsonPath, semanticTags);
            var physicalSize = ReadPhysicalSize(root);
            var texelDensity = ReadTexelDensity(root);
            var tags = BuildTags(root, semanticTags, categories);

            var asset = new AssetSummary(
                id,
                name ?? id,
                type.Canonical,
                folder,
                PathPolicy.Normalize(jsonPath),
                paths.ThumbnailPath,
                paths.PreviewPath,
                ReadOptional(root, semanticTags, "biome"),
                ReadOptional(root, semanticTags, "region"),
                physicalSize,
                resolution,
                texelDensity,
                ReadOptional(root, semanticTags, "averageColor", "average_color"),
                categories,
                tags,
                new FileInfo(jsonPath).LastWriteTimeUtc)
            {
                RawAssetType = type.Raw,
                ReferencedContentPaths = ReadContentReferences(root)
            };

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

    private static string[] ReadContentReferences(JsonElement root)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Visit(root);
        return references.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray();

        void Visit(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject()) Visit(property.Value);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray()) Visit(item);
                    break;
                case JsonValueKind.String:
                    var value = element.GetString();
                    if (value is { Length: > 0 and <= 1024 } && IsContentPath(value)) references.Add(value);
                    break;
            }
        }
    }

    private static bool IsContentPath(string value)
    {
        var extension = Path.GetExtension(value);
        return extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".abc", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".exr", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tga", StringComparison.OrdinalIgnoreCase);
    }
    private ImageResolution? ReadMaximumResolution(
        JsonElement root,
        string jsonPath,
        JsonElement? semanticTags)
    {
        var parsed = new List<ImageResolution>();
        var rawValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddResolution(
            JsonElementSearch.GetDirectString(
                root,
                "maxResolution",
                "maximumResolution",
                "max_resolution"));
        if (semanticTags is { } semantic)
        {
            AddResolution(JsonElementSearch.GetDirectString(semantic, "resolution"));
        }

        foreach (var containerName in new[] { "components", "maps" })
        {
            if (!JsonElementSearch.TryGetProperty(root, containerName, out var container))
            {
                continue;
            }

            foreach (var element in JsonElementSearch.FindPropertyValues(container, "resolution"))
            {
                AddResolution(JsonElementSearch.ScalarToString(element));
            }
        }

        return MetadataNormalizer.SelectMaximum(parsed);

        void AddResolution(string? raw)
        {
            var normalized = MetadataNormalizer.NormalizeOptional(raw);
            if (normalized is null || !rawValues.Add(normalized))
            {
                return;
            }

            if (MetadataNormalizer.TryParseResolution(normalized, out var value))
            {
                parsed.Add(value);
            }
            else
            {
                InfrastructureLog.MalformedResolution(logger, jsonPath, normalized);
            }
        }
    }

    private static string? ReadPhysicalSize(JsonElement root)
    {
        var raw = JsonElementSearch.GetDirectString(root, "physicalSize", "physical_size") ??
                  JsonElementSearch.FindKeyedValue(root, "key", "scanArea", "value");
        if (raw is null)
        {
            foreach (var containerName in new[] { "components", "maps" })
            {
                if (!JsonElementSearch.TryGetProperty(root, containerName, out var container))
                {
                    continue;
                }

                raw = JsonElementSearch.FindPropertyValues(container, "physicalSize")
                    .Select(JsonElementSearch.ScalarToString)
                    .FirstOrDefault(static value => MetadataNormalizer.NormalizeOptional(value) is not null);
                if (raw is not null)
                {
                    break;
                }
            }
        }

        return MetadataNormalizer.NormalizePhysicalSize(raw);
    }

    private static double? ReadTexelDensity(JsonElement root)
    {
        var raw = JsonElementSearch.GetDirectString(root, "texelDensity", "texel_density") ??
                  JsonElementSearch.FindKeyedValue(root, "key", "texelDensity", "value");
        return MetadataNormalizer.ParseTexelDensity(raw);
    }

    private static IReadOnlyList<string> GetAssetCategoryKeys(JsonElement root)
    {
        if (!JsonElementSearch.TryGetProperty(root, "assetCategories", out var value))
        {
            return [];
        }

        return MetadataNormalizer.NormalizeValues(JsonElementSearch.EnumerateObjectKeys(value));
    }

    private static IReadOnlyList<AssetTag> BuildTags(
        JsonElement root,
        JsonElement? semanticTags,
        IReadOnlyList<string> categories)
    {
        var tags = new List<AssetTag>();
        tags.AddRange(categories.Select(static value => new AssetTag(AssetTagKind.Category, value)));
        Add(tags, root, AssetTagKind.Descriptive, "descriptiveTags", "tags");
        Add(tags, root, AssetTagKind.State, "stateTags", "condition", "state");
        Add(tags, root, AssetTagKind.Color, "colorTags", "colors");
        Add(tags, root, AssetTagKind.Industry, "industryTags", "industries");
        Add(tags, root, AssetTagKind.Contains, "containsTags", "contains");
        Add(tags, root, AssetTagKind.Theme, "themeTags", "themes");

        if (semanticTags is { } semantic)
        {
            Add(tags, semantic, AssetTagKind.Descriptive, "descriptive");
            Add(tags, semantic, AssetTagKind.State, "state");
            Add(tags, semantic, AssetTagKind.Color, "color", "colors");
            Add(tags, semantic, AssetTagKind.Industry, "industry", "industries");
            Add(tags, semantic, AssetTagKind.Contains, "contains");
            Add(tags, semantic, AssetTagKind.Theme, "theme");
        }

        return TagNormalizer.Normalize(tags);
    }

    private static void Add(
        List<AssetTag> tags,
        JsonElement source,
        AssetTagKind kind,
        params string[] propertyNames)
    {
        foreach (var value in JsonElementSearch.GetDirectStrings(source, propertyNames))
        {
            tags.Add(new(kind, value));
        }
    }

    private static string? ReadOptional(
        JsonElement root,
        JsonElement? semanticTags,
        params string[] names) =>
        JsonElementSearch.GetDirectString(root, names) ??
        (semanticTags is { } semantic
            ? JsonElementSearch.GetDirectString(semantic, names)
            : null);

    private static JsonElement? GetObject(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (JsonElementSearch.TryGetProperty(root, name, out var value) &&
                value.ValueKind == JsonValueKind.Object)
            {
                return value;
            }
        }

        return null;
    }
}
