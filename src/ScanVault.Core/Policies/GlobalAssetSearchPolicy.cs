using System.Text.RegularExpressions;
using ScanVault.Core.Models;

namespace ScanVault.Core.Policies;

public enum GlobalSearchAssetType
{
    All,
    Mesh,
    Material,
    Texture,
    AtlasOrBillboard,
    Other
}

public sealed record GlobalAssetSearchMatch(AssetSummary Asset, string Field, string Value);

public static class GlobalAssetSearchPolicy
{
    public const int MaximumQueryLength = 512;
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    public static IReadOnlyList<GlobalAssetSearchMatch> Search(
        IEnumerable<AssetSummary> assets,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        if (query.Length > MaximumQueryLength)
        {
            throw new ArgumentException($"Search query cannot exceed {MaximumQueryLength} characters.", nameof(query));
        }

        var matcher = CreateMatcher(query);
        var results = new List<GlobalAssetSearchMatch>();
        foreach (var asset in assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var field in EnumerateFields(asset))
            {
                if (matcher(field.Value))
                {
                    results.Add(new(asset, field.Name, field.Value));
                    break;
                }
            }
        }

        return results;
    }

    public static bool MatchesType(AssetSummary asset, GlobalSearchAssetType filter) =>
        filter == GlobalSearchAssetType.All || ClassifyType(asset) == filter;

    public static GlobalSearchAssetType ClassifyType(AssetSummary asset)
    {
        var type = $"{asset.AssetType} {asset.RawAssetType}";
        if (Contains(type, "atlas") || Contains(type, "billboard") || asset.Content.HasAtlas || asset.Content.HasBillboard)
        {
            return GlobalSearchAssetType.AtlasOrBillboard;
        }

        if (Contains(type, "material") || Contains(type, "surface"))
        {
            return GlobalSearchAssetType.Material;
        }

        if (Contains(type, "texture") || Contains(type, "brush"))
        {
            return GlobalSearchAssetType.Texture;
        }

        if (Contains(type, "mesh") || Contains(type, "3d") || asset.Content.MeshCount > 0)
        {
            return GlobalSearchAssetType.Mesh;
        }

        return GlobalSearchAssetType.Other;
    }

    private static Func<string, bool> CreateMatcher(string query)
    {
        if (!query.Contains('*') && !query.Contains('_'))
        {
            return CreateTokenMatcher(query);
        }

        var pattern = string.Concat(query.Select(character => character switch
        {
            '*' => ".*",
            '_' => ".",
            _ => Regex.Escape(character.ToString())
        }));
        var regex = new Regex($"^(?:{pattern})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, MatchTimeout);
        return regex.IsMatch;
    }

    private static Func<string, bool> CreateTokenMatcher(string query)
    {
        var tokens = Regex.Matches(
                query,
                @"[\p{L}\p{N}]+",
                RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
                MatchTimeout)
            .Select(static match => Regex.Escape(match.Value))
            .ToArray();
        if (tokens.Length == 0)
        {
            return static _ => false;
        }

        var pattern = $@"(?:^|[^\p{{L}}\p{{N}}]){string.Join(@"[^\p{L}\p{N}]+", tokens)}(?:$|[^\p{{L}}\p{{N}}])";
        var regex = new Regex(
            pattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
            MatchTimeout);
        return regex.IsMatch;
    }

    private static IEnumerable<(string Name, string Value)> EnumerateFields(AssetSummary asset)
    {
        yield return ("Name", asset.Name);
        yield return ("Asset ID", asset.Id);
        yield return ("Type", asset.AssetType);
        if (!string.IsNullOrWhiteSpace(asset.RawAssetType)) yield return ("Source type", asset.RawAssetType);
        yield return ("Folder", asset.AssetFolderPath);
        yield return ("Metadata file", asset.JsonPath);
        if (!string.IsNullOrWhiteSpace(asset.Biome)) yield return ("Biome", asset.Biome);
        if (!string.IsNullOrWhiteSpace(asset.Region)) yield return ("Region", asset.Region);

        foreach (var category in asset.Categories) yield return ("Category", category);
        foreach (var tag in asset.Tags) yield return ($"{tag.Kind} tag", tag.Value);
        foreach (var path in asset.ReferencedContentPaths) yield return ("Referenced file", path);
        foreach (var variant in asset.Content.Variants)
        {
            yield return ("Variant", variant.Name);
            foreach (var mesh in variant.Meshes)
            {
                yield return ("Mesh file", mesh.FileName);
                yield return ("Mesh path", mesh.Path);
            }
        }

        foreach (var set in asset.Content.TextureSets)
        {
            yield return ("Texture set", set.Kind.ToString());
            foreach (var component in set.Components)
            {
                yield return ("Texture file", component.FileName);
                yield return ("Texture path", component.Path);
                yield return ("Texture map", component.MapType.ToString());
            }
        }

        foreach (var file in asset.Content.UnclassifiedFiles) yield return ("File", file.Path);
    }

    private static bool Contains(string value, string term) =>
        value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
