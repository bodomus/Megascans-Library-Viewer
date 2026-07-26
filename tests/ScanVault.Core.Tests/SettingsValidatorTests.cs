using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Core.Tests;

public sealed class SettingsValidatorTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "ScanVault.Core.Tests",
        Guid.NewGuid().ToString("N"));

    public SettingsValidatorTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void EmptyRootIsInvalid()
    {
        var result = SettingsValidator.Validate(LibrarySettings.Empty);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ExistingDirectoryIsValid()
    {
        var result = SettingsValidator.Validate(new(directory));

        Assert.True(result.IsValid);
        Assert.Null(result.Error);
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
