using System.Globalization;
using System.Text.Json;

namespace ScanVault.Infrastructure.Parsing;

internal static class JsonElementSearch
{
    public static bool TryFind(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                if (TryFind(property.Value, name, out value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFind(item, name, out value))
                {
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    public static string? FindString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryFind(root, name, out var value))
            {
                continue;
            }

            var result = ScalarToString(value);
            if (!string.IsNullOrWhiteSpace(result))
            {
                return result.Trim();
            }
        }

        return null;
    }

    public static IReadOnlyList<string> FindStrings(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryFind(root, name, out var value))
            {
                continue;
            }

            var values = FlattenStrings(value)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (values.Length > 0)
            {
                return values;
            }
        }

        return [];
    }

    public static int? FindResolution(JsonElement root, params string[] names)
    {
        var raw = FindString(root, names);
        if (raw is null)
        {
            return null;
        }

        var normalized = raw.Trim().ToUpperInvariant();
        if (normalized.EndsWith('K') &&
            double.TryParse(normalized[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var k))
        {
            return checked((int)(k * 1024));
        }

        var digits = new string(normalized.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    public static double? FindDouble(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryFind(root, name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            {
                return number;
            }

            if (double.TryParse(
                    ScalarToString(value),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out number))
            {
                return number;
            }
        }

        return null;
    }

    private static IEnumerable<string> FlattenStrings(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    foreach (var text in FlattenStrings(item))
                    {
                        yield return text;
                    }
                }

                break;
            case JsonValueKind.Object:
                var preferred = FindString(value, "name", "value", "label", "tag");
                if (preferred is not null)
                {
                    yield return preferred;
                }

                break;
            default:
                var scalar = ScalarToString(value);
                if (scalar is not null)
                {
                    yield return scalar;
                }

                break;
        }
    }

    private static string? ScalarToString(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
}
