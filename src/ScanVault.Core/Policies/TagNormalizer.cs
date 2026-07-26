using ScanVault.Core.Models;

namespace ScanVault.Core.Policies;

public static class TagNormalizer
{
    public static IReadOnlyList<AssetTag> Normalize(IEnumerable<AssetTag> tags) =>
        tags
            .Where(static tag => !string.IsNullOrWhiteSpace(tag.Value))
            .Select(static tag => tag with { Value = tag.Value.Trim() })
            .DistinctBy(static tag => (tag.Kind, tag.Value), AssetTagIdentityComparer.Instance)
            .OrderBy(static tag => tag.Kind)
            .ThenBy(static tag => tag.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private sealed class AssetTagIdentityComparer : IEqualityComparer<(AssetTagKind Kind, string Value)>
    {
        public static AssetTagIdentityComparer Instance { get; } = new();

        public bool Equals(
            (AssetTagKind Kind, string Value) x,
            (AssetTagKind Kind, string Value) y) =>
            x.Kind == y.Kind && StringComparer.OrdinalIgnoreCase.Equals(x.Value, y.Value);

        public int GetHashCode((AssetTagKind Kind, string Value) value) =>
            HashCode.Combine(value.Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(value.Value));
    }
}
