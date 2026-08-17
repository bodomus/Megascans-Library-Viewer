namespace ScanVault.Core.Models;

public static class UnrealImportPackageSchema
{
    public const int CurrentVersion = 1;
}

public enum UnrealImportSemanticRole
{
    BaseColor,
    Normal,
    Roughness,
    AO,
    Displacement,
    Opacity,
    Specular,
    Metalness,
    Emissive,
    Translucency,
    Other
}

public enum UnrealImportValidationSeverity
{
    Error,
    Warning,
    Information
}

public enum UnrealImportPackageIssueCode
{
    UnsupportedSchema,
    MissingSourceAssetIdentity,
    ReadinessBlocked,
    ReadinessStale,
    MissingRequiredMesh,
    MissingRequiredTexture,
    MissingProfile,
    IncompatibleMaterialProfile,
    MissingMasterMaterialPath,
    InvalidDestinationPath,
    AmbiguousTextureRole,
    MissingOptionalTexture,
    MissingSourcePath,
    ReadinessWarning,
    NaniteSuitabilityUncertain
}

public sealed record UnrealImportPackage(
    int SchemaVersion,
    string PackageId,
    UnrealImportPackageGenerator Generator,
    UnrealImportPackageSource Source,
    UnrealImportReadinessSnapshot Readiness,
    UnrealImportDestination Destination,
    UnrealImportMesh? Mesh,
    IReadOnlyList<UnrealImportTexture> Textures,
    UnrealImportMaterialProfileSnapshot Material,
    UnrealImportOptions Options,
    UnrealImportPackageValidation Validation);

public sealed record UnrealImportPackageGenerator(
    string Application,
    string ApplicationVersion,
    string CommitSha,
    DateTimeOffset GeneratedAtUtc);

public sealed record UnrealImportPackageSource(
    string AssetId,
    string Name,
    string AssetType,
    string JsonPath,
    string AssetFolderPath,
    DateTimeOffset LastWriteTimeUtc);

public sealed record UnrealImportReadinessSnapshot(
    UnrealReadinessStatus Status,
    int RuleVersion,
    int BlockingCount,
    int WarningCount,
    IReadOnlyList<UnrealImportReadinessReasonSnapshot> Reasons);

public sealed record UnrealImportReadinessReasonSnapshot(
    string Code,
    UnrealReadinessSeverity Severity,
    string Message,
    string? RelatedInventoryItem);

public sealed record UnrealImportDestination(
    string BaseContentPath,
    string ContentPath,
    string AssetBaseName,
    string OriginalAssetName);

public sealed record UnrealImportMesh(
    string PrimaryVariant,
    IReadOnlyList<UnrealImportMeshLod> Lods);

public sealed record UnrealImportMeshLod(
    string Variant,
    int Lod,
    string SourcePath,
    MeshFormat Format);

public sealed record UnrealImportTexture(
    UnrealImportSemanticRole Role,
    string SourcePath,
    TextureMapType MapType,
    TextureSetKind SetKind,
    int? Resolution,
    string Format);

public sealed record UnrealImportMaterialProfileSnapshot(
    string Id,
    string Name,
    string? Description,
    IReadOnlyList<string> AssetTypes,
    string? MasterMaterialPath,
    string MaterialInstanceName,
    string MaterialInstancePrefix,
    IReadOnlyList<UnrealImportTextureParameterMapping> TextureParameterMappings,
    bool IsBuiltIn);

public sealed record UnrealImportTextureParameterMapping(
    UnrealImportSemanticRole Role,
    string ParameterName);

public sealed record UnrealImportOptions(
    bool ImportLods,
    bool EnableNanite,
    bool CreateMaterialInstance,
    bool ReadinessOverride);

public sealed record UnrealImportPackageValidation(
    IReadOnlyList<UnrealImportPackageIssue> Issues)
{
    public bool HasErrors => Issues.Any(static issue => issue.Severity == UnrealImportValidationSeverity.Error);
    public int ErrorCount => Issues.Count(static issue => issue.Severity == UnrealImportValidationSeverity.Error);
    public int WarningCount => Issues.Count(static issue => issue.Severity == UnrealImportValidationSeverity.Warning);
}

public sealed record UnrealImportPackageIssue(
    UnrealImportPackageIssueCode Code,
    UnrealImportValidationSeverity Severity,
    string Message,
    string? RelatedPath);

public sealed record UnrealMaterialProfile(
    string Id,
    string Name,
    string? Description,
    IReadOnlyList<string> AssetTypes,
    string? MasterMaterialPath,
    bool CreateMaterialInstance,
    string MaterialInstancePrefix,
    IReadOnlyList<UnrealImportTextureParameterMapping> TextureParameterMappings,
    UnrealImportOptions DefaultOptions,
    bool IsBuiltIn);

public sealed record UnrealImportPackageSettings(
    string DefaultDestinationBasePath,
    string LastManifestExportFolder,
    IReadOnlyList<UnrealImportDefaultProfile> DefaultProfilesByAssetType)
{
    public static UnrealImportPackageSettings Default { get; } = new("/Game/Megascans", string.Empty, []);
}

public sealed record UnrealImportDefaultProfile(string AssetType, string ProfileId);

public sealed record UnrealImportPackageRequest(
    AssetSummary Asset,
    UnrealMaterialProfile Profile,
    string DestinationBasePath,
    UnrealImportOptions Options,
    string ApplicationVersion,
    string CommitSha,
    DateTimeOffset GeneratedAtUtc,
    bool ValidateSourceExists = false);

public sealed record UnrealImportPackageExportResult(
    string DestinationPath,
    long OutputSizeBytes);
