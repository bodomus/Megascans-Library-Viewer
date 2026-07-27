using System.Globalization;
using ScanVault.Core.Models;

namespace ScanVault.Core.Policies;

/// <summary>Canonical, schema-independent normalization for legacy metadata values.</summary>
public static class MetadataNormalizer
{
    private static readonly HashSet<string> PlaceholderTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "undefined",
        "null",
        "n/a",
        "na",
        "unknown"
    };

    private static readonly HashSet<string> ComponentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "normal",
        "specular",
        "roughness",
        "albedo",
        "opacity",
        "displacement",
        "bump",
        "gloss",
        "translucency"
    };

    public static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || PlaceholderTokens.Contains(trimmed)
            ? null
            : trimmed;
    }

    public static IReadOnlyList<string> NormalizeValues(
        IEnumerable<string?> values,
        string? redundantValue = null)
    {
        var normalizedRedundant = NormalizeOptional(redundantValue);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var value in values)
        {
            var normalized = NormalizeOptional(value);
            if (normalized is null ||
                StringComparer.OrdinalIgnoreCase.Equals(normalized, normalizedRedundant) ||
                !seen.Add(normalized))
            {
                continue;
            }

            result.Add(normalized);
        }

        return result;
    }

    public static AssetTypeResolution ResolveAssetType(IEnumerable<string?> candidates)
    {
        string? firstRaw = null;
        foreach (var candidate in candidates)
        {
            var normalized = NormalizeOptional(candidate);
            if (normalized is null)
            {
                continue;
            }

            firstRaw ??= normalized;
            if (TryCanonicalAssetType(normalized, out var canonical))
            {
                return new(canonical, normalized);
            }
        }

        return new("Unknown", firstRaw);
    }

    public static bool TryParseResolution(string? value, out ImageResolution resolution)
    {
        var normalized = NormalizeOptional(value)?.Replace('×', 'x');
        if (normalized is null)
        {
            resolution = default;
            return false;
        }

        if (normalized.EndsWith("K", StringComparison.OrdinalIgnoreCase) &&
            decimal.TryParse(
                normalized[..^1].Trim(),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var kilopixels))
        {
            var dimension = checked((int)decimal.Round(
                kilopixels * 1024,
                0,
                MidpointRounding.AwayFromZero));
            if (dimension > 0)
            {
                resolution = new(dimension, dimension);
                return true;
            }
        }

        var parts = normalized.Split('x', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && TryPositiveInteger(parts[0], out var width) &&
            TryPositiveInteger(parts[1], out var height))
        {
            resolution = new(width, height);
            return true;
        }

        if (parts.Length == 1 && TryPositiveInteger(parts[0], out var square))
        {
            resolution = new(square, square);
            return true;
        }

        resolution = default;
        return false;
    }

    public static ImageResolution? SelectMaximum(IEnumerable<ImageResolution> resolutions) =>
        resolutions
            .OrderByDescending(static value => value.MaxDimension)
            .ThenByDescending(static value => value.PixelCount)
            .ThenByDescending(static value => value.Width)
            .ThenByDescending(static value => value.Height)
            .Cast<ImageResolution?>()
            .FirstOrDefault();

    public static string? NormalizePhysicalSize(string? value)
    {
        var normalized = NormalizeOptional(value)?.Replace('×', 'x');
        if (normalized is null)
        {
            return null;
        }

        var parts = normalized.Split('x', StringSplitOptions.TrimEntries);
        if (parts.Length is < 2 or > 3)
        {
            return null;
        }

        var dimensions = new decimal[parts.Length];
        var unit = string.Empty;
        for (var index = 0; index < parts.Length; index++)
        {
            if (!TryReadDecimalAndUnit(parts[index], out dimensions[index], out var partUnit))
            {
                return null;
            }

            if (!string.IsNullOrEmpty(partUnit))
            {
                if (!string.IsNullOrEmpty(unit) &&
                    !StringComparer.OrdinalIgnoreCase.Equals(unit, partUnit))
                {
                    return null;
                }

                unit = partUnit;
            }
        }

        unit = string.IsNullOrWhiteSpace(unit) ? "m" : unit.Trim();
        return $"{string.Join(" × ", dimensions.Select(FormatDecimal))} {unit}";
    }

    public static double? ParseTexelDensity(string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return null;
        }

        var number = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return double.TryParse(
            number,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var result) && double.IsFinite(result) && result >= 0
            ? result
            : null;
    }

    private static bool TryCanonicalAssetType(string value, out string canonical)
    {
        var key = new string(value
            .Where(static character => !char.IsWhiteSpace(character) && character is not '-' and not '_')
            .Select(char.ToLowerInvariant)
            .ToArray());
        canonical = key switch
        {
            "3d" or "3dasset" => "3D Asset",
            "3dplant" => "3D Plant",
            "atlas" => "Atlas",
            "surface" => "Surface",
            "decal" => "Decal",
            "brush" => "Brush",
            "imperfection" => "Imperfection",
            _ => string.Empty
        };

        return canonical.Length > 0 && !ComponentTypes.Contains(value);
    }

    private static bool TryPositiveInteger(string value, out int result) =>
        int.TryParse(
            value.Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out result) && result > 0;

    private static bool TryReadDecimalAndUnit(
        string value,
        out decimal number,
        out string unit)
    {
        var splitAt = 0;
        while (splitAt < value.Length &&
               (char.IsDigit(value[splitAt]) || value[splitAt] is '.' or ',' or '+' or '-'))
        {
            splitAt++;
        }

        var numberText = value[..splitAt].Trim().Replace(',', '.');
        unit = value[splitAt..].Trim();
        return decimal.TryParse(
                   numberText,
                   NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                   CultureInfo.InvariantCulture,
                   out number) &&
               number >= 0;
    }

    private static string FormatDecimal(decimal value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}

public readonly record struct AssetTypeResolution(string Canonical, string? Raw);

