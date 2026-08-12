using System.Globalization;
using ScanVault.Core.Models;

namespace ScanVault.Core.Policies;

public static class AssetComparisonPolicy
{
    private static readonly StringComparer IdentityComparer = StringComparer.OrdinalIgnoreCase;

    public static AssetComparisonSnapshot Compare(AssetSummary left, AssetSummary right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (IdentityComparer.Equals(NormalizeIdentity(left.Id), NormalizeIdentity(right.Id)))
        {
            throw new ArgumentException("An asset cannot be compared with itself.", nameof(right));
        }

        var overview = BuildOverview(left, right);
        var variantsAndLods = BuildVariantsAndLods(left, right);
        var textureSets = BuildTextureSets(left, right);
        var files = BuildFiles(left, right);
        var issues = BuildIssues(left, right);
        var allRows = overview.Concat(variantsAndLods).Concat(textureSets).Concat(files).Concat(issues).ToArray();

        return new(
            Header(left),
            Header(right),
            overview,
            variantsAndLods,
            textureSets,
            files,
            issues,
            Summarize(allRows));
    }

    public static IReadOnlyList<ComparisonRow> FilterDifferences(
        IReadOnlyList<ComparisonRow> rows,
        bool differencesOnly) =>
        differencesOnly
            ? rows.Where(static row => row.Result != ComparisonResult.Equal).ToArray()
            : rows;

    private static ComparisonRow[] BuildOverview(AssetSummary left, AssetSummary right)
    {
        var leftContent = left.Content;
        var rightContent = right.Content;
        return
        [
            Row("asset-id", "Asset ID", Text(left.Id), Text(right.Id)),
            Row("name", "Name", Text(left.Name, caseSensitive: true), Text(right.Name, caseSensitive: true)),
            Row("asset-type", "Asset Type", Text(left.AssetType), Text(right.AssetType)),
            Row("biome", "Biome", OptionalText(left.Biome), OptionalText(right.Biome)),
            Row("region", "Region", OptionalText(left.Region), OptionalText(right.Region)),
            Row("relative-path", "Relative Path", RelativeFolder(left), RelativeFolder(right)),
            Row("resolution", "Resolution", Resolution(left.MaxResolution), Resolution(right.MaxResolution)),
            Row("texel-density", "Texel Density", Number(left.TexelDensity, "0.##", " px/m"), Number(right.TexelDensity, "0.##", " px/m")),
            Row("completeness", "Completeness", EnumValue(leftContent.Completeness), EnumValue(rightContent.Completeness)),
            Row("issue-count", "Issue Count", Integer(leftContent.Issues.Count), Integer(rightContent.Issues.Count)),
            Row("file-count", "File Count", Integer(FileCount(leftContent)), Integer(FileCount(rightContent))),
            Row("texture-set-count", "Texture Set Count", Integer(leftContent.TextureSetCount), Integer(rightContent.TextureSetCount)),
            Row("variant-count", "Variant Count", Integer(leftContent.VariantCount), Integer(rightContent.VariantCount)),
            Row("lod-count", "LOD Count", LodCount(left), LodCount(right)),
            Row("has-fbx", "Has FBX", Presence(leftContent.HasFbx), Presence(rightContent.HasFbx)),
            Row("has-abc", "Has ABC", Presence(HasMeshFormat(leftContent, MeshFormat.Abc)), Presence(HasMeshFormat(rightContent, MeshFormat.Abc))),
            Row("has-atlas", "Has Atlas", Presence(leftContent.HasAtlas), Presence(rightContent.HasAtlas)),
            Row("has-billboard", "Has Billboard", Presence(leftContent.HasBillboard), Presence(rightContent.HasBillboard))
        ];
    }

    private static ComparisonRow[] BuildVariantsAndLods(AssetSummary left, AssetSummary right)
    {
        var rows = new List<ComparisonRow>();
        AppendGroupedRows(
            rows,
            left.Content.Variants,
            right.Content.Variants,
            static item => NormalizeToken(item.Name),
            static item => $"{item.Name}: {string.Join(", ", item.Meshes.OrderBy(static mesh => mesh.Lod).ThenBy(static mesh => mesh.Format).Select(static mesh => $"LOD{mesh.Lod} {mesh.Format.ToString().ToUpperInvariant()}"))}",
            static item => string.Join("|", item.Meshes.Select(static mesh => $"{mesh.Lod}:{mesh.Format}").Order(StringComparer.OrdinalIgnoreCase)),
            "variant",
            static key => string.IsNullOrEmpty(key) ? "Variant (unnamed)" : $"Variant {key}");

        var leftLods = LodGroups(left.Content);
        var rightLods = LodGroups(right.Content);
        AppendGroupedRows(
            rows,
            leftLods,
            rightLods,
            static item => item.Key.ToString(CultureInfo.InvariantCulture),
            static item => DescribeLod(item),
            static item => NormalizeLod(item),
            "lod",
            static key => $"LOD{key}");

        if (rows.Count == 0)
        {
            rows.Add(Row("variants-lods-na", "Variants and LODs", ComparisonValue.NotApplicable(), ComparisonValue.NotApplicable()));
        }

        return rows.OrderBy(static row => row.Key, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static ComparisonRow[] BuildTextureSets(AssetSummary left, AssetSummary right)
    {
        var leftEntries = FlattenTextures(left.Content);
        var rightEntries = FlattenTextures(right.Content);
        var rows = new List<ComparisonRow>();
        AppendGroupedRows(
            rows,
            leftEntries,
            rightEntries,
            static item => $"{item.Kind}:{item.Component.MapType}",
            static item => DescribeTexture(item),
            static item => NormalizeTexture(item),
            "texture",
            static key => key.Replace(':', ' '));
        return rows.OrderBy(static row => row.Key, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static ComparisonRow[] BuildFiles(AssetSummary left, AssetSummary right)
    {
        var leftFiles = FlattenFiles(left);
        var rightFiles = FlattenFiles(right);
        var rows = new List<ComparisonRow>();
        AppendGroupedRows(
            rows,
            leftFiles,
            rightFiles,
            static item => item.LogicalKey,
            static item => item.Display,
            static item => item.SemanticValue,
            "file",
            static key => key);
        return rows.OrderBy(static row => row.Key, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static ComparisonRow[] BuildIssues(AssetSummary left, AssetSummary right)
    {
        var leftIssues = left.Content.Issues.Select(issue => Issue(left, issue)).ToArray();
        var rightIssues = right.Content.Issues.Select(issue => Issue(right, issue)).ToArray();
        var rows = new List<ComparisonRow>();
        AppendGroupedRows(
            rows,
            leftIssues,
            rightIssues,
            static item => item.Code.ToString(),
            static item => item.Display,
            static item => item.SemanticValue,
            "issue",
            static key => key);
        return rows.OrderBy(static row => row.Key, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AppendGroupedRows<T>(
        List<ComparisonRow> rows,
        IEnumerable<T> leftItems,
        IEnumerable<T> rightItems,
        Func<T, string> keySelector,
        Func<T, string> displaySelector,
        Func<T, string> semanticSelector,
        string keyPrefix,
        Func<string, string> labelSelector)
    {
        var leftGroups = leftItems.GroupBy(keySelector, IdentityComparer).ToDictionary(static group => group.Key, static group => group.ToArray(), IdentityComparer);
        var rightGroups = rightItems.GroupBy(keySelector, IdentityComparer).ToDictionary(static group => group.Key, static group => group.ToArray(), IdentityComparer);
        var keys = leftGroups.Keys.Concat(rightGroups.Keys).Distinct(IdentityComparer).Order(IdentityComparer);
        foreach (var key in keys)
        {
            leftGroups.TryGetValue(key, out var leftGroup);
            rightGroups.TryGetValue(key, out var rightGroup);
            leftGroup ??= [];
            rightGroup ??= [];
            var ambiguous = leftGroup.Length > 1 || rightGroup.Length > 1;
            var count = Math.Max(leftGroup.Length, rightGroup.Length);
            for (var index = 0; index < count; index++)
            {
                var left = index < leftGroup.Length
                    ? ItemValue(leftGroup[index], displaySelector, semanticSelector, ambiguous)
                    : ComparisonValue.Missing();
                var right = index < rightGroup.Length
                    ? ItemValue(rightGroup[index], displaySelector, semanticSelector, ambiguous)
                    : ComparisonValue.Missing();
                var suffix = count > 1 ? $" #{index + 1}" : string.Empty;
                rows.Add(Row($"{keyPrefix}:{key}:{index}", $"{labelSelector(key)}{suffix}", left, right, ambiguous));
            }
        }
    }

    private static ComparisonValue ItemValue<T>(
        T item,
        Func<T, string> displaySelector,
        Func<T, string> semanticSelector,
        bool ambiguous) =>
        ambiguous
            ? ComparisonValue.Ambiguous(displaySelector(item), semanticSelector(item))
            : ComparisonValue.Present(displaySelector(item), semanticSelector(item));

    private static ComparisonRow Row(
        string key,
        string label,
        ComparisonValue left,
        ComparisonValue right,
        bool ambiguous = false) =>
        new(key, label, left, right, ambiguous ? ComparisonResult.Ambiguous : CompareValues(left, right));

    private static ComparisonResult CompareValues(ComparisonValue left, ComparisonValue right)
    {
        if (left.Kind == ComparisonValueKind.Ambiguous || right.Kind == ComparisonValueKind.Ambiguous)
        {
            return ComparisonResult.Ambiguous;
        }

        if (left.Kind == ComparisonValueKind.Unknown || right.Kind == ComparisonValueKind.Unknown)
        {
            return ComparisonResult.Unknown;
        }

        if (left.Kind == ComparisonValueKind.NotApplicable || right.Kind == ComparisonValueKind.NotApplicable)
        {
            return left.Kind == right.Kind ? ComparisonResult.NotApplicable : ComparisonResult.Different;
        }

        if (left.Kind == ComparisonValueKind.Missing || right.Kind == ComparisonValueKind.Missing)
        {
            if (left.Kind == right.Kind)
            {
                return ComparisonResult.Equal;
            }

            return left.Kind == ComparisonValueKind.Missing
                ? ComparisonResult.OnlyRight
                : ComparisonResult.OnlyLeft;
        }

        return IdentityComparer.Equals(left.NormalizedValue, right.NormalizedValue)
            ? ComparisonResult.Equal
            : ComparisonResult.Different;
    }

    private static ComparisonSummary Summarize(IEnumerable<ComparisonRow> rows)
    {
        var counts = rows.GroupBy(static row => row.Result).ToDictionary(static group => group.Key, static group => group.Count());
        return new(
            Get(ComparisonResult.Equal),
            Get(ComparisonResult.Different),
            Get(ComparisonResult.OnlyLeft),
            Get(ComparisonResult.OnlyRight),
            Get(ComparisonResult.Unknown),
            Get(ComparisonResult.NotApplicable),
            Get(ComparisonResult.Ambiguous));

        int Get(ComparisonResult result) => counts.GetValueOrDefault(result);
    }

    private static AssetComparisonHeader Header(AssetSummary asset) =>
        new(asset.Id, asset.Name, asset.AssetType, RelativeFolderDisplay(asset), asset.Content.Completeness, asset.PreviewPath ?? asset.ThumbnailPath);

    private static ComparisonValue Text(string value, bool caseSensitive = false)
    {
        var trimmed = value.Trim();
        return ComparisonValue.Present(trimmed, caseSensitive ? trimmed : NormalizeToken(trimmed));
    }

    private static ComparisonValue OptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? ComparisonValue.Unknown() : Text(value);

    private static ComparisonValue EnumValue<T>(T value) where T : struct, Enum =>
        ComparisonValue.Present(value.ToString(), Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));

    private static ComparisonValue Integer(int value) =>
        ComparisonValue.Present(value.ToString(CultureInfo.CurrentCulture), value.ToString(CultureInfo.InvariantCulture));

    private static ComparisonValue Number(double? value, string format, string suffix) =>
        value is null
            ? ComparisonValue.Unknown()
            : ComparisonValue.Present(value.Value.ToString(format, CultureInfo.CurrentCulture) + suffix, value.Value.ToString("R", CultureInfo.InvariantCulture));

    private static ComparisonValue Resolution(ImageResolution? resolution) =>
        resolution is null
            ? ComparisonValue.Unknown()
            : ComparisonValue.Present(resolution.Value.ToDisplayString(), $"{resolution.Value.Width}x{resolution.Value.Height}");

    private static ComparisonValue Presence(bool present) =>
        present ? ComparisonValue.Present("Present", "1") : ComparisonValue.Missing();

    private static ComparisonValue LodCount(AssetSummary asset) =>
        IsLodApplicable(asset)
            ? Integer(asset.Content.LodCount)
            : ComparisonValue.NotApplicable();

    private static bool IsLodApplicable(AssetSummary asset) =>
        !asset.AssetType.Contains("surface", StringComparison.OrdinalIgnoreCase);

    private static ComparisonValue RelativeFolder(AssetSummary asset) =>
        ComparisonValue.Present(RelativeFolderDisplay(asset), NormalizePath(RelativeFolderDisplay(asset)));

    private static string RelativeFolderDisplay(AssetSummary asset)
    {
        var trimmed = asset.AssetFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? "(library root)" : name;
    }

    private static int FileCount(AssetContentInventory content) =>
        content.MeshCount + content.TextureCount + content.UnclassifiedFiles.Count;

    private static bool HasMeshFormat(AssetContentInventory content, MeshFormat format) =>
        content.Variants.SelectMany(static variant => variant.Meshes).Any(mesh => mesh.Format == format);

    private static IGrouping<int, MeshLodEntry>[] LodGroups(AssetContentInventory content) =>
        content.Variants.SelectMany(static variant => variant.Meshes).GroupBy(static mesh => mesh.Lod).ToArray();

    private static string DescribeLod(IGrouping<int, MeshLodEntry> group) =>
        $"LOD{group.Key}: {string.Join(", ", group.Select(static mesh => $"{mesh.Variant} {mesh.Format.ToString().ToUpperInvariant()}").Order(IdentityComparer))}";

    private static string NormalizeLod(IGrouping<int, MeshLodEntry> group) =>
        string.Join("|", group.Select(static mesh => $"{NormalizeToken(mesh.Variant)}:{mesh.Format}").Order(IdentityComparer));

    private static TextureEntry[] FlattenTextures(AssetContentInventory content) =>
        content.TextureSets.SelectMany(set => set.Components.Select(component => new TextureEntry(set.Kind, set.Resolution, component))).ToArray();

    private static string DescribeTexture(TextureEntry entry)
    {
        var resolution = entry.Component.Resolution ?? entry.SetResolution;
        var resolutionDisplay = resolution is null ? "Unknown resolution" : $"{resolution.Value}×{resolution.Value}";
        return $"{resolutionDisplay} {entry.Component.Format.ToUpperInvariant()} — {entry.Component.FileName}";
    }

    private static string NormalizeTexture(TextureEntry entry)
    {
        var resolution = entry.Component.Resolution ?? entry.SetResolution;
        return $"{entry.Kind}|{entry.Component.MapType}|{resolution?.ToString(CultureInfo.InvariantCulture) ?? "?"}|{NormalizeToken(entry.Component.Format)}";
    }

    private static LogicalFile[] FlattenFiles(AssetSummary asset)
    {
        var files = new List<LogicalFile>();
        foreach (var variant in asset.Content.Variants)
        {
            files.AddRange(variant.Meshes.Select(mesh => CreateLogicalFile(
                asset,
                $"Meshes | {NormalizeToken(variant.Name)} | LOD{mesh.Lod} | {mesh.Format}",
                mesh.Path,
                $"Variant {variant.Name}; LOD{mesh.Lod}; {mesh.Format.ToString().ToUpperInvariant()}",
                $"mesh|{NormalizeToken(variant.Name)}|{mesh.Lod}|{mesh.Format}")));
        }

        foreach (var set in asset.Content.TextureSets)
        {
            files.AddRange(set.Components.Select(component => CreateLogicalFile(
                asset,
                $"Textures | {set.Kind} | {component.MapType} | {NormalizeToken(component.Format)}",
                component.Path,
                $"{set.Kind}; {component.MapType}; resolution {(component.Resolution ?? set.Resolution)?.ToString(CultureInfo.InvariantCulture) ?? "Unknown"}; {component.Format.ToUpperInvariant()}",
                $"texture|{set.Kind}|{component.MapType}|{component.Resolution ?? set.Resolution}|{NormalizeToken(component.Format)}")));
        }

        files.AddRange(asset.Content.UnclassifiedFiles.Select(file =>
        {
            var relative = RelativePath(asset, file.Path);
            return CreateLogicalFile(
                asset,
                $"Other | {NormalizePath(Path.GetFileName(relative))}",
                file.Path,
                $"Unclassified: {file.Reason}",
                $"other|{NormalizePath(relative)}|{NormalizeToken(file.Reason)}");
        }));
        return files.ToArray();
    }
    private static LogicalFile CreateLogicalFile(
        AssetSummary asset,
        string logicalKey,
        string path,
        string details,
        string semanticValue)
    {
        var relative = RelativePath(asset, path);
        var issueFlags = IssueFlags(asset, relative);
        var display = $"{relative} | {details} | Size: Unknown | LastWriteTimeUtc: Unknown | Issues: {issueFlags}";
        return new(logicalKey, display, $"{semanticValue}|issues:{NormalizeToken(issueFlags)}");
    }

    private static string IssueFlags(AssetSummary asset, string relativePath)
    {
        var normalizedPath = NormalizePath(relativePath);
        var codes = asset.Content.Issues
            .Where(issue => issue.Paths.Any(path => NormalizePath(RelativePath(asset, path)) == normalizedPath))
            .Select(static issue => issue.Code.ToString())
            .Distinct(IdentityComparer)
            .Order(IdentityComparer)
            .ToArray();
        return codes.Length == 0 ? "None" : string.Join(", ", codes);
    }

    private static IssueEntry Issue(AssetSummary asset, AssetContentIssue issue)
    {
        var relativePaths = issue.Paths.Select(path => RelativePath(asset, path)).Order(IdentityComparer).ToArray();
        var display = relativePaths.Length == 0
            ? issue.Message
            : $"{issue.Message} — {string.Join(", ", relativePaths)}";
        var semantic = $"{NormalizeToken(issue.Message)}|{string.Join("|", relativePaths.Select(NormalizePath))}";
        return new(issue.Code, display, semantic);
    }

    private static string RelativePath(AssetSummary asset, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Unknown";
        }

        var relative = Path.IsPathRooted(path)
            ? Path.GetRelativePath(asset.AssetFolderPath, path)
            : path;
        return relative.StartsWith("..", StringComparison.Ordinal)
            ? Path.GetFileName(path)
            : relative.Replace('\\', '/');
    }

    private static string NormalizeIdentity(string value) => value.Trim();

    private static string NormalizeToken(string value) => value.Trim().ToUpperInvariant();

    private static string NormalizePath(string value) => value.Trim().Replace('\\', '/').ToUpperInvariant();

    private sealed record TextureEntry(TextureSetKind Kind, int? SetResolution, TextureComponentEntry Component);
    private sealed record LogicalFile(string LogicalKey, string Display, string SemanticValue);
    private sealed record IssueEntry(AssetContentIssueCode Code, string Display, string SemanticValue);
}
