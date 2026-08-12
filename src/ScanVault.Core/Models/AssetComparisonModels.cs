namespace ScanVault.Core.Models;

public enum ComparisonResult
{
    Equal,
    Different,
    OnlyLeft,
    OnlyRight,
    Unknown,
    NotApplicable,
    Ambiguous
}

public enum ComparisonValueKind
{
    Present,
    Missing,
    Unknown,
    NotApplicable,
    Ambiguous
}

public sealed record ComparisonValue(
    string DisplayValue,
    string? NormalizedValue,
    ComparisonValueKind Kind)
{
    public static ComparisonValue Present(string displayValue, string normalizedValue) =>
        new(displayValue, normalizedValue, ComparisonValueKind.Present);

    public static ComparisonValue Missing() => new("Missing", null, ComparisonValueKind.Missing);

    public static ComparisonValue Unknown() => new("Unknown", null, ComparisonValueKind.Unknown);

    public static ComparisonValue NotApplicable() =>
        new("Not applicable", null, ComparisonValueKind.NotApplicable);

    public static ComparisonValue Ambiguous(string displayValue, string normalizedValue) =>
        new(displayValue, normalizedValue, ComparisonValueKind.Ambiguous);
}

public sealed record AssetComparisonHeader(
    string AssetId,
    string Name,
    string AssetType,
    string RelativePath,
    AssetCompletenessStatus Completeness,
    string? PreviewPath);

public sealed record ComparisonRow(
    string Key,
    string Label,
    ComparisonValue Left,
    ComparisonValue Right,
    ComparisonResult Result)
{
    public string ResultLabel => Result switch
    {
        ComparisonResult.OnlyLeft => "Only left",
        ComparisonResult.OnlyRight => "Only right",
        ComparisonResult.NotApplicable => "Not applicable",
        _ => Result.ToString()
    };
}

public sealed record ComparisonSummary(
    int Equal,
    int Different,
    int OnlyLeft,
    int OnlyRight,
    int Unknown,
    int NotApplicable,
    int Ambiguous)
{
    public int DifferenceCount => Different + OnlyLeft + OnlyRight + Unknown + Ambiguous;
}

public sealed record AssetComparisonSnapshot(
    AssetComparisonHeader Left,
    AssetComparisonHeader Right,
    IReadOnlyList<ComparisonRow> Overview,
    IReadOnlyList<ComparisonRow> VariantsAndLods,
    IReadOnlyList<ComparisonRow> TextureSets,
    IReadOnlyList<ComparisonRow> Files,
    IReadOnlyList<ComparisonRow> Issues,
    ComparisonSummary Summary)
{
    public IEnumerable<ComparisonRow> AllRows =>
        Overview.Concat(VariantsAndLods).Concat(TextureSets).Concat(Files).Concat(Issues);
}

