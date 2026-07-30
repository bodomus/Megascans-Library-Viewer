namespace ScanVault.Core.Models;

public enum SmartCollectionKind { BuiltIn, User }
public enum SmartCollectionFolderScope { EntireLibrary, CurrentFolder, SpecificFolder }
public enum SmartCollectionCompatibility { Compatible, MissingFolder, UnsupportedDefinition, Corrupted }

public sealed record SmartCollectionDefinition(
    int DefinitionVersion,
    string SearchText,
    IReadOnlyList<string> AssetTypes,
    SmartCollectionFolderScope FolderScope,
    string? RelativeFolderPath,
    bool? HasFbx,
    bool? HasAbc,
    bool? HasLods,
    bool? HasVariants,
    bool? HasAtlas,
    bool? HasBillboard,
    bool? HasTextureSets,
    bool? HasIssues,
    IReadOnlyList<AssetCompletenessStatus> CompletenessStatuses,
    int? MinimumResolution,
    int? MaximumResolution,
    AssetSortMode? SortMode)
{
    public const int CurrentVersion = 1;
    public static SmartCollectionDefinition Empty { get; } = new(
        CurrentVersion,
        string.Empty,
        [],
        SmartCollectionFolderScope.EntireLibrary,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        [],
        null,
        null,
        null);
}

public sealed record SmartCollectionRecord(
    string Id,
    SmartCollectionKind Kind,
    string Name,
    string Description,
    SmartCollectionDefinition Definition,
    int Order,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record SmartCollectionValidationResult(
    SmartCollectionCompatibility Compatibility,
    string? Message);
