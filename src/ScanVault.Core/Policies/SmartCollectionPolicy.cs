using ScanVault.Core.Models;

namespace ScanVault.Core.Policies;

public static class SmartCollectionPolicy
{
    public static IReadOnlyList<SmartCollectionRecord> BuiltIns { get; } =
    [
        BuiltIn("builtin-all-assets", "All Assets", "Every indexed asset.", SmartCollectionDefinition.Empty),
        BuiltIn("builtin-complete-assets", "Complete Assets", "Assets with complete content inventory.",
            SmartCollectionDefinition.Empty with { CompletenessStatuses = [AssetCompletenessStatus.Complete] }),
        BuiltIn("builtin-assets-with-issues", "Assets With Issues", "Assets with inventory issues.",
            SmartCollectionDefinition.Empty with { HasIssues = true }),
        BuiltIn("builtin-missing-mesh", "Missing Mesh", "Assets without FBX or ABC meshes.",
            SmartCollectionDefinition.Empty with { HasFbx = false, HasAbc = false }),
        BuiltIn("builtin-missing-lods", "Missing LODs", "Assets without LOD meshes.",
            SmartCollectionDefinition.Empty with { HasLods = false }),
        BuiltIn("builtin-atlas-assets", "Atlas Assets", "Assets containing atlas texture sets.",
            SmartCollectionDefinition.Empty with { HasAtlas = true }),
        BuiltIn("builtin-billboard-assets", "Billboard Assets", "Assets containing billboard texture sets.",
            SmartCollectionDefinition.Empty with { HasBillboard = true })
    ];

    public static SmartCollectionDefinition FromUiState(
        string searchText,
        AssetInventoryFilter inventoryFilter,
        SmartCollectionFolderScope folderScope,
        string? libraryRoot,
        string? selectedFolderPath,
        AssetSortMode sortMode,
        bool saveSort)
    {
        string? relativeFolder = null;
        if (folderScope == SmartCollectionFolderScope.SpecificFolder &&
            !string.IsNullOrWhiteSpace(libraryRoot) &&
            !string.IsNullOrWhiteSpace(selectedFolderPath))
        {
            relativeFolder = NormalizeRelativeFolder(libraryRoot, selectedFolderPath);
        }

        return SmartCollectionDefinition.Empty with
        {
            SearchText = searchText.Trim(),
            FolderScope = folderScope,
            RelativeFolderPath = relativeFolder,
            HasFbx = ToCriterion(inventoryFilter, AssetInventoryFilter.HasFbx),
            HasLods = ToCriterion(inventoryFilter, AssetInventoryFilter.HasLods),
            HasBillboard = ToCriterion(inventoryFilter, AssetInventoryFilter.HasBillboard),
            HasAtlas = ToCriterion(inventoryFilter, AssetInventoryFilter.HasAtlas),
            CompletenessStatuses = CompletenessCriteria(inventoryFilter),
            SortMode = saveSort ? sortMode : null
        };
    }

    public static bool Matches(
        AssetSummary asset,
        SmartCollectionDefinition definition,
        string? libraryRoot,
        string? currentFolderPath)
    {
        if (definition.DefinitionVersion != SmartCollectionDefinition.CurrentVersion)
        {
            return false;
        }

        var folderPath = ResolveFolderPath(definition, libraryRoot, currentFolderPath);
        if (!string.IsNullOrWhiteSpace(folderPath) && !AssetFiltering.IsInFolder(asset, folderPath))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(definition.SearchText) &&
            !AssetFiltering.MatchesSearch(asset, definition.SearchText))
        {
            return false;
        }

        if (definition.AssetTypes.Count > 0 &&
            !definition.AssetTypes.Any(type => string.Equals(type, asset.AssetType, StringComparison.OrdinalIgnoreCase) ||
                                               string.Equals(type, asset.RawAssetType, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return MatchesContent(asset, definition);
    }

    public static SmartCollectionValidationResult Validate(
        SmartCollectionDefinition definition,
        string? libraryRoot)
    {
        if (definition.DefinitionVersion != SmartCollectionDefinition.CurrentVersion)
        {
            return new(SmartCollectionCompatibility.UnsupportedDefinition, "Unsupported definition version.");
        }

        if (definition.FolderScope != SmartCollectionFolderScope.SpecificFolder ||
            string.IsNullOrWhiteSpace(definition.RelativeFolderPath))
        {
            return new(SmartCollectionCompatibility.Compatible, null);
        }

        if (string.IsNullOrWhiteSpace(libraryRoot))
        {
            return new(SmartCollectionCompatibility.MissingFolder, "Library root is not configured.");
        }

        var fullPath = ResolveSpecificFolder(libraryRoot, definition.RelativeFolderPath);
        return Directory.Exists(fullPath)
            ? new(SmartCollectionCompatibility.Compatible, null)
            : new(SmartCollectionCompatibility.MissingFolder, $"Folder is missing: {definition.RelativeFolderPath}");
    }

    public static IReadOnlyDictionary<string, int> CountMatches(
        IEnumerable<SmartCollectionRecord> collections,
        IReadOnlyList<AssetSummary> assets,
        string? libraryRoot,
        string? currentFolderPath)
    {
        var collectionList = collections.ToArray();
        var counts = collectionList.ToDictionary(static collection => collection.Id, static _ => 0);
        foreach (var asset in assets)
        {
            foreach (var collection in collectionList)
            {
                if (Matches(asset, collection.Definition, libraryRoot, currentFolderPath))
                {
                    counts[collection.Id]++;
                }
            }
        }

        return counts;
    }

    public static string? ResolveFolderPath(
        SmartCollectionDefinition definition,
        string? libraryRoot,
        string? currentFolderPath) =>
        definition.FolderScope switch
        {
            SmartCollectionFolderScope.CurrentFolder => currentFolderPath,
            SmartCollectionFolderScope.SpecificFolder when !string.IsNullOrWhiteSpace(libraryRoot) &&
                                                           !string.IsNullOrWhiteSpace(definition.RelativeFolderPath) =>
                ResolveSpecificFolder(libraryRoot, definition.RelativeFolderPath),
            _ => null
        };

    private static SmartCollectionRecord BuiltIn(
        string id,
        string name,
        string description,
        SmartCollectionDefinition definition) =>
        new(id, SmartCollectionKind.BuiltIn, name, description, definition, 0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static bool? ToCriterion(AssetInventoryFilter filter, AssetInventoryFilter flag) =>
        filter.HasFlag(flag) ? true : null;

    private static IReadOnlyList<AssetCompletenessStatus> CompletenessCriteria(AssetInventoryFilter filter)
    {
        if (filter.HasFlag(AssetInventoryFilter.Complete))
        {
            return [AssetCompletenessStatus.Complete];
        }

        if (filter.HasFlag(AssetInventoryFilter.Incomplete))
        {
            return [AssetCompletenessStatus.Usable, AssetCompletenessStatus.Partial, AssetCompletenessStatus.MissingCriticalFiles];
        }

        return filter.HasFlag(AssetInventoryFilter.Ambiguous)
            ? [AssetCompletenessStatus.Ambiguous]
            : [];
    }

    private static bool MatchesContent(AssetSummary asset, SmartCollectionDefinition definition)
    {
        var content = asset.Content;
        return MatchesBoolean(definition.HasFbx, content.HasFbx) &&
               MatchesBoolean(definition.HasAbc, content.Variants.SelectMany(static variant => variant.Meshes).Any(static mesh => mesh.Format == MeshFormat.Abc)) &&
               MatchesBoolean(definition.HasLods, content.HasLods) &&
               MatchesBoolean(definition.HasVariants, content.VariantCount > 0) &&
               MatchesBoolean(definition.HasAtlas, content.HasAtlas) &&
               MatchesBoolean(definition.HasBillboard, content.HasBillboard) &&
               MatchesBoolean(definition.HasTextureSets, content.TextureSetCount > 0) &&
               MatchesBoolean(definition.HasIssues, content.Issues.Count > 0) &&
               (definition.CompletenessStatuses.Count == 0 || definition.CompletenessStatuses.Contains(content.Completeness)) &&
               (definition.MinimumResolution is null || asset.MaxResolution?.MaxDimension >= definition.MinimumResolution) &&
               (definition.MaximumResolution is null || asset.MaxResolution?.MaxDimension <= definition.MaximumResolution);
    }

    private static bool MatchesBoolean(bool? criterion, bool value) =>
        criterion is null || criterion == value;

    private static string ResolveSpecificFolder(string libraryRoot, string relativeFolderPath) =>
        PathPolicy.Normalize(Path.Combine(PathPolicy.Normalize(libraryRoot), relativeFolderPath));

    private static string NormalizeRelativeFolder(string libraryRoot, string selectedFolderPath)
    {
        var relative = Path.GetRelativePath(PathPolicy.Normalize(libraryRoot), PathPolicy.Normalize(selectedFolderPath));
        return relative == "." ? string.Empty : relative.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }
}
