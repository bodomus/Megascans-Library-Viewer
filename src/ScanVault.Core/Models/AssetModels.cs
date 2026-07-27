namespace ScanVault.Core.Models;

/// <summary>Classifies normalized metadata attached to an asset.</summary>
public enum AssetTagKind
{
    Category,
    Descriptive,
    State,
    Color,
    Industry,
    Contains,
    Theme
}

/// <summary>A normalized, typed metadata value.</summary>
public sealed record AssetTag(AssetTagKind Kind, string Value);

/// <summary>Immutable data required to browse one physical Megascans asset.</summary>
public sealed record AssetSummary(
    string Id,
    string Name,
    string AssetType,
    string AssetFolderPath,
    string JsonPath,
    string? ThumbnailPath,
    string? PreviewPath,
    string? Biome,
    string? Region,
    string? PhysicalSize,
    ImageResolution? MaxResolution,
    double? TexelDensity,
    string? AverageColor,
    IReadOnlyList<string> Categories,
    IReadOnlyList<AssetTag> Tags,
    DateTimeOffset LastWriteTimeUtc)
{
    public string? RawAssetType { get; init; }
}

/// <summary>Persisted per-user application settings.</summary>
public sealed record LibrarySettings(string LibraryRoot, AssetSortMode SortMode = AssetSortMode.NameAscending)
{
    public static LibrarySettings Empty { get; } = new(string.Empty);
}
