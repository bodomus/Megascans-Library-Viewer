namespace ScanVault.Core.Models;

public enum MeshFormat { Fbx, Abc }
public enum TextureMapType { Unknown, Albedo, Normal, Roughness, Gloss, Specular, Opacity, Translucency, AmbientOcclusion, Cavity, Displacement, Bump, Brush }
public enum TextureSetKind { Unknown, General, Atlas, Billboard }
public enum AssetCompletenessStatus { Complete, Usable, Partial, MissingCriticalFiles, Ambiguous, Unknown }
public enum AssetContentIssueCode { DuplicateMesh, DuplicateTexture, ConflictingName, MissingReference, MissingCriticalFile, InaccessibleDirectory, UnclassifiedFile }

public sealed record MeshLodEntry(string Path, string FileName, string Variant, int Lod, MeshFormat Format);
public sealed record MeshVariantInventory(string Name, IReadOnlyList<MeshLodEntry> Meshes);
public sealed record TextureComponentEntry(string Path, string FileName, string RawMapName, TextureMapType MapType, int? Resolution, string Format);
public sealed record TextureSetInventory(TextureSetKind Kind, int? Resolution, IReadOnlyList<TextureComponentEntry> Components);
public sealed record UnclassifiedAssetFile(string Path, string Reason);
public sealed record AssetContentIssue(AssetContentIssueCode Code, string Message, IReadOnlyList<string> Paths);

public sealed record AssetContentInventory(
    IReadOnlyList<MeshVariantInventory> Variants,
    IReadOnlyList<TextureSetInventory> TextureSets,
    IReadOnlyList<UnclassifiedAssetFile> UnclassifiedFiles,
    AssetCompletenessStatus Completeness,
    IReadOnlyList<AssetContentIssue> Issues)
{
    public static AssetContentInventory Empty { get; } = new([], [], [], AssetCompletenessStatus.Unknown, []);
    public int MeshCount => Variants.Sum(static variant => variant.Meshes.Count);
    public int TextureCount => TextureSets.Sum(static set => set.Components.Count);
    public int VariantCount => Variants.Count;
    public int LodCount => Variants.SelectMany(static variant => variant.Meshes).Select(static mesh => mesh.Lod).Distinct().Count();
    public int TextureSetCount => TextureSets.Count;
    public bool HasFbx => Variants.SelectMany(static variant => variant.Meshes).Any(static mesh => mesh.Format == MeshFormat.Fbx);
    public bool HasLods => Variants.SelectMany(static variant => variant.Meshes).Any(static mesh => mesh.Lod > 0);
    public bool HasAtlas => TextureSets.Any(static set => set.Kind == TextureSetKind.Atlas);
    public bool HasBillboard => TextureSets.Any(static set => set.Kind == TextureSetKind.Billboard);
}

[Flags]
public enum AssetInventoryFilter
{
    None = 0, HasFbx = 1 << 0, HasLods = 1 << 1, HasBillboard = 1 << 2,
    HasAtlas = 1 << 3, Complete = 1 << 4, Incomplete = 1 << 5, Ambiguous = 1 << 6,
    UnrealReady = 1 << 7,
    UnrealReadyWithWarnings = 1 << 8,
    UnrealNotReady = 1 << 9,
    UnrealUnknown = 1 << 10,
    UnrealNotApplicable = 1 << 11,
    UnrealMissingMesh = 1 << 12,
    UnrealMissingNormal = 1 << 13,
    UnrealMissingLods = 1 << 14,
    UnrealBlockingIssues = 1 << 15,
    UnrealWarnings = 1 << 16
}

public sealed record AssetContentFileCandidate(string FullPath, string RelativePath);
public sealed record AssetInventoryResult(AssetContentInventory Inventory, IReadOnlyList<string> InaccessibleDirectories);
