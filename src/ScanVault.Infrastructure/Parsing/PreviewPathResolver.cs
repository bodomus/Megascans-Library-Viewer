using System.Globalization;
using System.Text.Json;
using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Infrastructure.Parsing;

public static class PreviewPathResolver
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff"
    };

    public static (string? ThumbnailPath, string? PreviewPath) Resolve(
        JsonElement root,
        string assetFolder,
        string assetId)
    {
        var candidates = ReadMetadataCandidates(root, assetFolder).ToList();
        candidates.AddRange(ReadKnownCandidates(assetFolder, assetId));

        return (
            PreviewCandidateSelector.SelectThumbnail(candidates),
            PreviewCandidateSelector.SelectPreview(candidates));
    }

    private static IEnumerable<ImageCandidate> ReadMetadataCandidates(
        JsonElement element,
        string assetFolder)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            string? tag = null;
            string? path = null;
            int? resolution = null;

            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals("type", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("tag", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("name", StringComparison.OrdinalIgnoreCase))
                {
                    tag ??= property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : null;
                }
                else if (property.Name.Equals("path", StringComparison.OrdinalIgnoreCase) ||
                         property.Name.Equals("uri", StringComparison.OrdinalIgnoreCase) ||
                         property.Name.Equals("url", StringComparison.OrdinalIgnoreCase))
                {
                    path ??= property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : null;
                }
                else if (property.Name.Equals("resolution", StringComparison.OrdinalIgnoreCase))
                {
                    resolution = ParseResolution(property.Value);
                }
            }

            var candidatePath = ResolveLocalPath(assetFolder, path);
            if (candidatePath is not null && ImageExtensions.Contains(Path.GetExtension(candidatePath)))
            {
                yield return new(candidatePath, Classify(tag, candidatePath), resolution);
            }

            foreach (var property in element.EnumerateObject())
            {
                foreach (var candidate in ReadMetadataCandidates(property.Value, assetFolder))
                {
                    yield return candidate;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var candidate in ReadMetadataCandidates(item, assetFolder))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static int? ParseResolution(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetInt32(out var numericResolution) && numericResolution > 0
                ? numericResolution
                : null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = element.GetString()?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var scalarResolution))
        {
            return scalarResolution > 0 ? scalarResolution : null;
        }

        var separatorIndex = text.IndexOfAny(['x', 'X', '\u00D7']);
        if (separatorIndex <= 0 || separatorIndex >= text.Length - 1 ||
            text.IndexOfAny(['x', 'X', '\u00D7'], separatorIndex + 1) >= 0 ||
            !TryParsePositiveDimension(text.AsSpan(0, separatorIndex), out var width) ||
            !TryParsePositiveDimension(text.AsSpan(separatorIndex + 1), out var height))
        {
            return null;
        }

        return Math.Max(width, height);
    }

    private static bool TryParsePositiveDimension(ReadOnlySpan<char> value, out int dimension) =>
        int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out dimension) &&
        dimension > 0;

    private static IEnumerable<ImageCandidate> ReadKnownCandidates(string folder, string assetId)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, $"{assetId}*.*", SearchOption.TopDirectoryOnly)
                .Where(path => ImageExtensions.Contains(Path.GetExtension(path)))
                .OrderBy(static path => path, PathPolicy.Comparer)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files)
        {
            yield return new(PathPolicy.Normalize(file), Classify(Path.GetFileName(file), file));
        }
    }

    private static string? ResolveLocalPath(string folder, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            return null;
        }

        var local = Path.IsPathRooted(path) ? path : Path.Combine(folder, path);
        return File.Exists(local) ? PathPolicy.Normalize(local) : null;
    }

    private static ImageCandidateRole Classify(string? tag, string path)
    {
        var value = $"{tag} {Path.GetFileNameWithoutExtension(path)}";
        if (value.Contains("thumb", StringComparison.OrdinalIgnoreCase))
        {
            return ImageCandidateRole.Thumb;
        }

        if (value.Contains("retina", StringComparison.OrdinalIgnoreCase))
        {
            return ImageCandidateRole.Retina;
        }

        if (value.Contains("bake", StringComparison.OrdinalIgnoreCase))
        {
            return ImageCandidateRole.Bake;
        }

        if (value.Contains("preview", StringComparison.OrdinalIgnoreCase))
        {
            return ImageCandidateRole.Preview;
        }

        return ImageCandidateRole.KnownPattern;
    }
}
