namespace ScanVault.Core.Models;

public enum UnrealReadinessStatus
{
    Ready,
    ReadyWithWarnings,
    NotReady,
    NotApplicable,
    Unknown
}

public enum UnrealReadinessSeverity
{
    Blocking,
    Warning,
    Information
}

public enum UnrealReadinessRuleCode
{
    UeReady,
    UeNotApplicable,
    UeMissingMesh,
    UeMissingAlbedo,
    UeMissingNormal,
    UeMissingRoughness,
    UeMissingDisplacement,
    UeMissingOpacity,
    UeNoLods,
    UeIncompleteLodChain,
    UeOnlyBillboard,
    UeAmbiguousInventory,
    UeMixedResolutions,
    UeUnsupportedFileFormat,
    UeUnknownAssetType,
    UeDuplicateLogicalFiles,
    UeNoPrimaryTextureSet
}

public sealed record UnrealReadinessReason(
    UnrealReadinessRuleCode RuleCode,
    UnrealReadinessSeverity Severity,
    string Message,
    string? RelatedInventoryItem)
{
    public string Code => RuleCode switch
    {
        UnrealReadinessRuleCode.UeReady => "UE_READY",
        UnrealReadinessRuleCode.UeNotApplicable => "UE_NOT_APPLICABLE",
        UnrealReadinessRuleCode.UeMissingMesh => "UE_MISSING_MESH",
        UnrealReadinessRuleCode.UeMissingAlbedo => "UE_MISSING_ALBEDO",
        UnrealReadinessRuleCode.UeMissingNormal => "UE_MISSING_NORMAL",
        UnrealReadinessRuleCode.UeMissingRoughness => "UE_MISSING_ROUGHNESS",
        UnrealReadinessRuleCode.UeMissingDisplacement => "UE_MISSING_DISPLACEMENT",
        UnrealReadinessRuleCode.UeMissingOpacity => "UE_MISSING_OPACITY",
        UnrealReadinessRuleCode.UeNoLods => "UE_NO_LODS",
        UnrealReadinessRuleCode.UeIncompleteLodChain => "UE_INCOMPLETE_LOD_CHAIN",
        UnrealReadinessRuleCode.UeOnlyBillboard => "UE_ONLY_BILLBOARD",
        UnrealReadinessRuleCode.UeAmbiguousInventory => "UE_AMBIGUOUS_INVENTORY",
        UnrealReadinessRuleCode.UeMixedResolutions => "UE_MIXED_RESOLUTIONS",
        UnrealReadinessRuleCode.UeUnsupportedFileFormat => "UE_UNSUPPORTED_FILE_FORMAT",
        UnrealReadinessRuleCode.UeUnknownAssetType => "UE_UNKNOWN_ASSET_TYPE",
        UnrealReadinessRuleCode.UeDuplicateLogicalFiles => "UE_DUPLICATE_LOGICAL_FILES",
        UnrealReadinessRuleCode.UeNoPrimaryTextureSet => "UE_NO_PRIMARY_TEXTURE_SET",
        _ => RuleCode.ToString()
    };
}

public sealed record UnrealReadinessEvaluation(
    UnrealReadinessStatus Status,
    int ReadinessRuleVersion,
    IReadOnlyList<UnrealReadinessReason> Reasons,
    DateTimeOffset? EvaluatedAtUtc)
{
    public static UnrealReadinessEvaluation Unknown { get; } = new(
        UnrealReadinessStatus.Unknown,
        0,
        [new(
            UnrealReadinessRuleCode.UeUnknownAssetType,
            UnrealReadinessSeverity.Information,
            "Unreal readiness has not been evaluated.",
            null)],
        null);

    public int BlockingCount => Reasons.Count(static reason => reason.Severity == UnrealReadinessSeverity.Blocking);
    public int WarningCount => Reasons.Count(static reason => reason.Severity == UnrealReadinessSeverity.Warning);

    public string Summary => string.Join("; ", Reasons.Select(static reason => $"{reason.Code}: {reason.Message}"));
}

public sealed record UnrealReadinessSummary(
    int ReadyCount,
    int ReadyWithWarningsCount,
    int NotReadyCount,
    int NotApplicableCount,
    int UnknownCount,
    int RequiresRecalculationCount,
    int RuleVersion,
    DateTimeOffset? LastEvaluatedAtUtc)
{
    public static UnrealReadinessSummary Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, null);
}
