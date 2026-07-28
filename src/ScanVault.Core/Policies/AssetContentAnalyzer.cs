using System.Globalization;
using System.Text.RegularExpressions;
using ScanVault.Core.Models;

namespace ScanVault.Core.Policies;

/// <summary>Conservative deterministic filename analysis; asset payloads are never opened.</summary>
public static class AssetContentAnalyzer
{
    private static readonly HashSet<string> TextureExtensions = new(
        [".jpg", ".jpeg", ".png", ".tif", ".tiff", ".exr", ".tga"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly Regex VariantRegex = new(@"(?i)(?<![A-Za-z0-9])Var(?<value>\d+)(?![A-Za-z0-9])", RegexOptions.Compiled);
    private static readonly Regex LodRegex = new(@"(?i)(?<![A-Za-z0-9])LOD(?<value>\d+)(?![A-Za-z0-9])", RegexOptions.Compiled);
    private static readonly Regex ResolutionRegex = new(@"(?i)(?<![A-Za-z0-9])(?:(?<value>1|2|4|8|16)(?<k>K)|(?<value>512|1024|2048|4096|8192))(?![A-Za-z0-9])", RegexOptions.Compiled);
    private static readonly Regex MapTokenRegex = new(@"(?i)(?<![A-Za-z0-9])(BaseColor|Diffuse|Albedo|NormalBump|NormalObject|Normal|Roughness|Gloss|Specular|Alpha|Opacity|Translucency|AmbientOcclusion|AO|Cavity|Height|Displacement|Bump|Brush)(?![A-Za-z0-9])", RegexOptions.Compiled);
    private static readonly Regex BillboardTokenRegex = new(@"(?i)(?<![A-Za-z0-9])Billboard(?![A-Za-z0-9])", RegexOptions.Compiled);
    private static readonly Regex AtlasTokenRegex = new(@"(?i)(?<![A-Za-z0-9])Atlas(?![A-Za-z0-9])", RegexOptions.Compiled);

    public static AssetContentInventory Analyze(
        string assetType,
        IReadOnlyList<AssetContentFileCandidate> files,
        IReadOnlyList<string>? metadataReferences = null,
        IReadOnlyList<string>? inaccessibleDirectories = null)
    {
        var meshes = new List<MeshLodEntry>();
        var textures = new List<(TextureSetKind Kind, TextureComponentEntry Entry)>();
        var unclassified = new List<UnclassifiedAssetFile>();
        var issues = new List<AssetContentIssue>();
        var references = metadataReferences ?? [];
        var referenceSets = BuildReferenceSets(references);

        foreach (var file in files.OrderBy(static value => value.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var extension = Path.GetExtension(file.FullPath);
            if (extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".abc", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseMesh(file, out var mesh, out var reason)) meshes.Add(mesh);
                else unclassified.Add(new(file.FullPath, reason));
                continue;
            }

            if (!TextureExtensions.Contains(extension)) continue;
            if (TryParseTexture(file, referenceSets, out var kind, out var texture)) textures.Add((kind, texture));
            else if (!Path.GetFileNameWithoutExtension(file.FullPath).Contains("preview", StringComparison.OrdinalIgnoreCase))
                unclassified.Add(new(file.FullPath, "Texture map type could not be classified."));
        }

        AddDuplicateIssues(meshes, textures, issues);
        AddMissingReferences(files, references, issues);
        foreach (var directory in (inaccessibleDirectories ?? []).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
            issues.Add(new(AssetContentIssueCode.InaccessibleDirectory, "Content directory could not be read.", [directory]));
        foreach (var item in unclassified)
            issues.Add(new(AssetContentIssueCode.UnclassifiedFile, item.Reason, [item.Path]));

        var variants = meshes
            .GroupBy(static mesh => mesh.Variant, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => TrailingNumber(group.Key))
            .ThenBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static group => new MeshVariantInventory(group.Key, group
                .OrderBy(static mesh => mesh.Lod)
                .ThenBy(static mesh => mesh.Format)
                .ThenBy(static mesh => mesh.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray()))
            .ToArray();
        var sets = textures
            .GroupBy(static value => (value.Kind, value.Entry.Resolution))
            .OrderBy(static group => group.Key.Kind)
            .ThenByDescending(static group => group.Key.Resolution)
            .Select(static group => new TextureSetInventory(group.Key.Kind, group.Key.Resolution, group
                .Select(static value => value.Entry)
                .OrderBy(static entry => entry.MapType)
                .ThenBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray()))
            .ToArray();

        var completeness = EvaluateCompleteness(assetType, variants, sets, issues);
        return new(variants, sets,
            unclassified.OrderBy(static item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            completeness,
            issues.OrderBy(static issue => issue.Code).ThenBy(static issue => issue.Message, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static bool TryNormalizeMap(string fileName, out TextureMapType map, out string raw)
    {
        var matches = MapTokenRegex.Matches(Path.GetFileNameWithoutExtension(fileName));
        if (matches.Count == 0)
        {
            map = TextureMapType.Unknown;
            raw = string.Empty;
            return false;
        }

        raw = matches[^1].Value;
        map = raw.ToLowerInvariant() switch
        {
            "basecolor" or "diffuse" or "albedo" => TextureMapType.Albedo,
            "normal" or "normalbump" or "normalobject" => TextureMapType.Normal,
            "roughness" => TextureMapType.Roughness,
            "gloss" => TextureMapType.Gloss,
            "specular" => TextureMapType.Specular,
            "alpha" or "opacity" => TextureMapType.Opacity,
            "translucency" => TextureMapType.Translucency,
            "ao" or "ambientocclusion" => TextureMapType.AmbientOcclusion,
            "cavity" => TextureMapType.Cavity,
            "height" or "displacement" => TextureMapType.Displacement,
            "bump" => TextureMapType.Bump,
            "brush" => TextureMapType.Brush,
            _ => TextureMapType.Unknown
        };
        return map != TextureMapType.Unknown;
    }

    public static int? ParseResolution(string path)
    {
        var matches = ResolutionRegex.Matches(path);
        if (matches.Count == 0 || !int.TryParse(matches[^1].Groups["value"].Value, out var value)) return null;
        return matches[^1].Groups["k"].Success ? value * 1024 : value;
    }

    private static bool TryParseMesh(AssetContentFileCandidate file, out MeshLodEntry mesh, out string reason)
    {
        var variants = VariantRegex.Matches(file.RelativePath).Cast<Match>()
            .Select(static match => match.Groups["value"].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var lods = LodRegex.Matches(file.RelativePath).Cast<Match>()
            .Select(static match => match.Groups["value"].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (variants.Length > 1 || lods.Length != 1 || !int.TryParse(lods.ElementAtOrDefault(0), out var lod))
        {
            mesh = null!;
            reason = variants.Length > 1 || lods.Length > 1
                ? "Mesh name contains conflicting variant or LOD tokens."
                : "Mesh name does not contain one unambiguous LOD token.";
            return false;
        }

        var variant = variants.Length == 1 ? $"Var{int.Parse(variants[0], CultureInfo.InvariantCulture)}" : "Default";
        mesh = new(file.FullPath, Path.GetFileName(file.FullPath), variant, lod,
            Path.GetExtension(file.FullPath).Equals(".fbx", StringComparison.OrdinalIgnoreCase) ? MeshFormat.Fbx : MeshFormat.Abc);
        reason = string.Empty;
        return true;
    }

    private static bool TryParseTexture(
        AssetContentFileCandidate file,
        IReadOnlyDictionary<string, TextureSetKind> referenceSets,
        out TextureSetKind kind,
        out TextureComponentEntry texture)
    {
        if (!TryNormalizeMap(Path.GetFileName(file.FullPath), out var map, out var raw))
        {
            kind = TextureSetKind.Unknown;
            texture = null!;
            return false;
        }

        kind = ClassifySet(file.RelativePath, referenceSets);
        texture = new(file.FullPath, Path.GetFileName(file.FullPath), raw, map, ParseResolution(file.RelativePath),
            Path.GetExtension(file.FullPath).TrimStart('.').ToUpperInvariant());
        return true;
    }

    private static TextureSetKind ClassifySet(string relativePath, IReadOnlyDictionary<string, TextureSetKind> referenceSets)
    {
        var segments = relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments.Take(Math.Max(0, segments.Length - 1)))
        {
            var normalized = segment.Trim('[', ']');
            if (normalized.Equals("Billboard", StringComparison.OrdinalIgnoreCase)) return TextureSetKind.Billboard;
            if (normalized.Equals("Atlas", StringComparison.OrdinalIgnoreCase)) return TextureSetKind.Atlas;
        }

        var fileName = Path.GetFileName(relativePath);
        if (BillboardTokenRegex.IsMatch(fileName)) return TextureSetKind.Billboard;
        if (AtlasTokenRegex.IsMatch(fileName)) return TextureSetKind.Atlas;
        return referenceSets.TryGetValue(fileName, out var referenced) ? referenced : TextureSetKind.General;
    }

    private static Dictionary<string, TextureSetKind> BuildReferenceSets(IReadOnlyList<string> references)
    {
        var result = new Dictionary<string, TextureSetKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in references)
        {
            var kind = BillboardTokenRegex.IsMatch(reference) ? TextureSetKind.Billboard
                : AtlasTokenRegex.IsMatch(reference) ? TextureSetKind.Atlas : TextureSetKind.Unknown;
            if (kind != TextureSetKind.Unknown) result.TryAdd(Path.GetFileName(reference), kind);
        }
        return result;
    }

    private static void AddDuplicateIssues(
        IReadOnlyList<MeshLodEntry> meshes,
        IReadOnlyList<(TextureSetKind Kind, TextureComponentEntry Entry)> textures,
        List<AssetContentIssue> issues)
    {
        foreach (var group in meshes.GroupBy(static mesh => $"{mesh.Variant}\0{mesh.Lod}\0{mesh.Format}", StringComparer.OrdinalIgnoreCase).Where(static group => group.Count() > 1))
            issues.Add(new(AssetContentIssueCode.DuplicateMesh, "Multiple meshes have the same variant, LOD, and format.",
                group.Select(static mesh => mesh.Path).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray()));
        foreach (var group in textures.GroupBy(static value => $"{value.Kind}\0{value.Entry.MapType}\0{value.Entry.Resolution}\0{value.Entry.Format}", StringComparer.OrdinalIgnoreCase).Where(static group => group.Count() > 1))
            issues.Add(new(AssetContentIssueCode.DuplicateTexture, "Multiple textures have the same set, map, resolution, and format.",
                group.Select(static value => value.Entry.Path).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray()));
    }

    private static void AddMissingReferences(
        IReadOnlyList<AssetContentFileCandidate> files,
        IReadOnlyList<string> references,
        List<AssetContentIssue> issues)
    {
        var names = files.Select(static file => Path.GetFileName(file.FullPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in references.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(reference);
            var extension = Path.GetExtension(name);
            var known = extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".abc", StringComparison.OrdinalIgnoreCase) ||
                TextureExtensions.Contains(extension) && TryNormalizeMap(name, out _, out _);
            if (known && !names.Contains(name))
                issues.Add(new(AssetContentIssueCode.MissingReference, $"Referenced content file is missing: {name}", [reference]));
        }
    }

    private static AssetCompletenessStatus EvaluateCompleteness(
        string assetType,
        IReadOnlyList<MeshVariantInventory> variants,
        IReadOnlyList<TextureSetInventory> sets,
        List<AssetContentIssue> issues)
    {
        if (issues.Any(static issue => issue.Code is AssetContentIssueCode.DuplicateMesh or AssetContentIssueCode.DuplicateTexture or AssetContentIssueCode.ConflictingName))
            return AssetCompletenessStatus.Ambiguous;

        var meshes = variants.SelectMany(static variant => variant.Meshes).ToArray();
        var maps = sets.SelectMany(static set => set.Components).Select(static component => component.MapType).ToHashSet();
        var hasFbx = meshes.Any(static mesh => mesh.Format == MeshFormat.Fbx);
        var hasAnyMesh = meshes.Length > 0;
        var hasLod0 = meshes.Any(static mesh => mesh.Lod == 0);
        var hasAlbedo = maps.Contains(TextureMapType.Albedo);
        var hasNormal = maps.Contains(TextureMapType.Normal) || maps.Contains(TextureMapType.Bump);
        var hasSurface = maps.Contains(TextureMapType.Roughness) || maps.Contains(TextureMapType.Gloss);
        var hasOpacity = maps.Contains(TextureMapType.Opacity);
        bool complete;
        List<string> critical;

        switch (assetType)
        {
            case "3D Asset":
                critical = Missing((hasAnyMesh, "mesh"), (hasAlbedo, "Albedo"));
                complete = hasFbx && hasLod0 && hasAlbedo && hasNormal && hasSurface;
                break;
            case "3D Plant":
                critical = Missing((hasAnyMesh, "mesh"), (hasAlbedo, "Albedo"), (hasOpacity, "Opacity"));
                complete = hasFbx && hasLod0 && hasAlbedo && hasNormal && hasSurface && hasOpacity;
                break;
            case "Atlas":
                critical = Missing((hasAlbedo, "Albedo"), (hasOpacity, "Opacity"));
                complete = hasAlbedo && hasOpacity && hasNormal && hasSurface;
                break;
            case "Surface":
                critical = Missing((hasAlbedo, "Albedo"));
                complete = hasAlbedo && hasNormal && hasSurface;
                break;
            case "Brush":
                critical = Missing((maps.Contains(TextureMapType.Brush), "Brush"));
                complete = critical.Count == 0;
                break;
            default:
                return AssetCompletenessStatus.Unknown;
        }

        if (critical.Count > 0 || issues.Any(static issue => issue.Code == AssetContentIssueCode.MissingReference))
        {
            var missing = critical.Count > 0 ? string.Join(", ", critical) : "referenced content";
            issues.Add(new(AssetContentIssueCode.MissingCriticalFile, $"Missing critical content: {missing}.", []));
            return AssetCompletenessStatus.MissingCriticalFiles;
        }
        return complete ? AssetCompletenessStatus.Complete : AssetCompletenessStatus.Usable;
    }

    private static List<string> Missing(params (bool Present, string Name)[] values) =>
        values.Where(static value => !value.Present).Select(static value => value.Name).ToList();

    private static int TrailingNumber(string value)
    {
        var digits = new string(value.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return int.TryParse(digits, out var number) ? number : -1;
    }
}
