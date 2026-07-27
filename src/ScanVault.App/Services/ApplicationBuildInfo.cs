using System.Reflection;
using System.Runtime.InteropServices;

namespace ScanVault.App.Services;

public sealed record ApplicationBuildInfo(
    string ProductVersion,
    string InformationalVersion,
    string CommitSha,
    string BuildConfiguration,
    string RuntimeVersion,
    string OperatingSystem,
    string ProcessArchitecture)
{
    public const string ProductName = "ScanVault \u2014 Megascans Library Viewer";
    public const string UnknownValue = "Unknown";
    public const string UnavailableCommit = "unavailable";

    public string WindowTitle => ProductVersion == UnknownValue
        ? ProductName
        : $"{ProductName} {ProductVersion}";

    public static ApplicationBuildInfo FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var configuration = assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()
            ?.Configuration;
        var productVersion = ReadMetadata(assembly, "ProductVersion") ??
                             FormatProductVersion(assembly.GetName().Version);

        return Create(
            productVersion,
            informationalVersion,
            ReadMetadata(assembly, "CommitSha"),
            configuration ?? ReadMetadata(assembly, "BuildConfiguration"));
    }

    public static ApplicationBuildInfo Create(
        string? productVersion,
        string? informationalVersion,
        string? commitSha,
        string? buildConfiguration,
        string? runtimeVersion = null,
        string? operatingSystem = null,
        string? processArchitecture = null)
    {
        var normalizedProduct = Normalize(productVersion) ??
                                ExtractProductVersion(informationalVersion) ??
                                UnknownValue;
        var normalizedInformational = Normalize(informationalVersion) ?? normalizedProduct;
        var normalizedCommit = Normalize(commitSha) ??
                               ExtractCommit(normalizedInformational) ??
                               UnavailableCommit;

        return new(
            normalizedProduct,
            normalizedInformational,
            ShortenCommit(normalizedCommit),
            Normalize(buildConfiguration) ?? UnknownValue,
            Normalize(runtimeVersion) ?? RuntimeInformation.FrameworkDescription,
            Normalize(operatingSystem) ?? RuntimeInformation.OSDescription,
            Normalize(processArchitecture) ?? RuntimeInformation.ProcessArchitecture.ToString());
    }

    private static string? ReadMetadata(Assembly assembly, string key) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute =>
                StringComparer.Ordinal.Equals(attribute.Key, key))
            ?.Value;

    private static string? FormatProductVersion(Version? version) =>
        version is null
            ? null
            : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";

    private static string? ExtractProductVersion(string? informationalVersion)
    {
        var normalized = Normalize(informationalVersion);
        if (normalized is null)
        {
            return null;
        }

        var separator = normalized.IndexOfAny(['-', '+']);
        var candidate = separator < 0 ? normalized : normalized[..separator];
        return Version.TryParse(candidate, out var parsed)
            ? $"{parsed.Major}.{parsed.Minor}.{Math.Max(parsed.Build, 0)}"
            : null;
    }

    private static string? ExtractCommit(string informationalVersion)
    {
        var separator = informationalVersion.LastIndexOf('+');
        return separator >= 0 && separator < informationalVersion.Length - 1
            ? informationalVersion[(separator + 1)..]
            : null;
    }

    private static string ShortenCommit(string value) =>
        value == UnavailableCommit || value.Length <= 7
            ? value
            : value[..7];

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
