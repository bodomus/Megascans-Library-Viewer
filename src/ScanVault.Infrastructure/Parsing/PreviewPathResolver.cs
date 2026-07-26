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
                else if (property.Name.Equals("resolution", StringComparison.OrdinalIgnoreCase) &&
                         property.Value.TryGetInt32(out var parsedResolution))
                {
                    resolution = parsedResolution;
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
