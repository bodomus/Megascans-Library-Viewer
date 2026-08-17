using System.Globalization;
using System.Text;

namespace ScanVault.Core.Policies;

public static class UnrealImportNamePolicy
{
    public const string FallbackName = "Asset";

    public static string SanitizeSegment(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return FallbackName;
        }

        var builder = new StringBuilder(trimmed.Length);
        var previousWasSeparator = false;
        foreach (var rune in trimmed.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                builder.Append(rune.ToString());
                previousWasSeparator = false;
                continue;
            }

            if (rune.Value == '_' && builder.Length > 0 && !previousWasSeparator)
            {
                builder.Append('_');
                previousWasSeparator = true;
                continue;
            }

            var category = Rune.GetUnicodeCategory(rune);
            var separator = category is UnicodeCategory.SpaceSeparator or UnicodeCategory.DashPunctuation or UnicodeCategory.ConnectorPunctuation ||
                            rune.Value is '/' or '\\' or '.' or ':' or ';' or ',' or '(' or ')' or '[' or ']' or '{' or '}' or '+';
            if (separator && builder.Length > 0 && !previousWasSeparator)
            {
                builder.Append('_');
                previousWasSeparator = true;
            }
        }

        var result = builder.ToString().Trim('_');
        return string.IsNullOrEmpty(result) ? FallbackName : result;
    }

    public static string MaterialInstanceName(string prefix, string assetBaseName)
    {
        var safePrefix = SanitizePrefix(prefix);
        var safeName = SanitizeSegment(assetBaseName);
        return safePrefix + safeName;
    }

    public static string SanitizePrefix(string? prefix)
    {
        var safe = SanitizeSegment(prefix);
        return safe == FallbackName ? "MI_" : safe.TrimEnd('_') + "_";
    }
}
