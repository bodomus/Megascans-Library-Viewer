using System.Text.Json;
using ScanVault.Core.Policies;

namespace ScanVault.Infrastructure.Parsing;

internal static class JsonElementSearch
{
    public static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
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
        }

        value = default;
        return false;
    }

    public static string? GetDirectString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetProperty(element, name, out var value))
            {
                var normalized = MetadataNormalizer.NormalizeOptional(ScalarToString(value));
                if (normalized is not null)
                {
                    return normalized;
                }
            }
        }

        return null;
    }

    public static IReadOnlyList<string> GetDirectStrings(
        JsonElement element,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(element, name, out var value))
            {
                continue;
            }

            var result = MetadataNormalizer.NormalizeValues(FlattenStrings(value));
            if (result.Count > 0)
            {
                return result;
            }
        }

        return [];
    }

    public static string? FindKeyedValue(
        JsonElement element,
        string keyProperty,
        string keyValue,
        string valueProperty)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var key = GetDirectString(element, keyProperty);
            if (StringComparer.OrdinalIgnoreCase.Equals(key, keyValue))
            {
                return GetDirectString(element, valueProperty);
            }

            foreach (var property in element.EnumerateObject())
            {
                var result = FindKeyedValue(
                    property.Value,
                    keyProperty,
                    keyValue,
                    valueProperty);
                if (result is not null)
                {
                    return result;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var result = FindKeyedValue(item, keyProperty, keyValue, valueProperty);
                if (result is not null)
                {
                    return result;
                }
            }
        }

        return null;
    }

    public static IEnumerable<string> EnumerateObjectKeys(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var property in element.EnumerateObject())
        {
            yield return property.Name;
            foreach (var nested in EnumerateObjectKeys(property.Value))
            {
                yield return nested;
            }
        }
    }

    public static IEnumerable<JsonElement> FindPropertyValues(
        JsonElement element,
        string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    yield return property.Value;
                }

                foreach (var nested in FindPropertyValues(property.Value, propertyName))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in FindPropertyValues(item, propertyName))
                {
                    yield return nested;
                }
            }
        }
    }

    public static IEnumerable<string?> FlattenStrings(JsonElement value)
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
                var preferred = GetDirectString(value, "name", "value", "label", "tag");
                if (preferred is not null)
                {
                    yield return preferred;
                }

                break;
            default:
                yield return ScalarToString(value);
                break;
        }
    }

    public static string? ScalarToString(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
}
