using ScanVault.Core.Models;

namespace ScanVault.Core.Policies;

public static class TagNormalizer
{
    public static IReadOnlyList<AssetTag> Normalize(IEnumerable<AssetTag> tags)
    {
        var seen = new HashSet<(AssetTagKind Kind, string Value)>(
            AssetTagIdentityComparer.Instance);
        var result = new List<AssetTag>();
        foreach (var tag in tags)
        {
            var value = MetadataNormalizer.NormalizeOptional(tag.Value);
            if (value is null || !seen.Add((tag.Kind, value)))
            {
                continue;
            }

            result.Add(tag with { Value = value });
        }

        return result;
    }

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
