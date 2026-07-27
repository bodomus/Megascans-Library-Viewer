using ScanVault.App.Services;

namespace ScanVault.App.Tests;

public sealed class ApplicationBuildInfoTests
{
    [Fact]
    public void CreateUsesProductVersionInTitleAndKeepsBuildMetadataOutOfUi()
    {
        var buildInfo = ApplicationBuildInfo.Create(
            "0.2.0",
            "0.2.0-ci.42+0123456789abcdef",
            "0123456789abcdef",
            "Release",
            ".NET Test",
            "Windows Test",
            "X64");

        Assert.Equal("0.2.0", buildInfo.ProductVersion);
        Assert.Equal("0.2.0-ci.42+0123456789abcdef", buildInfo.InformationalVersion);
        Assert.Equal("0123456", buildInfo.CommitSha);
        Assert.Equal("ScanVault \u2014 Megascans Library Viewer 0.2.0", buildInfo.WindowTitle);
        Assert.DoesNotContain("0123456", buildInfo.WindowTitle, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateDerivesProductAndCommitFromInformationalVersion()
    {
        var buildInfo = ApplicationBuildInfo.Create(
            null,
            "0.2.0-dev+abcdef123456",
            null,
            null,
            "Runtime",
            "OS",
            "Arm64");

        Assert.Equal("0.2.0", buildInfo.ProductVersion);
        Assert.Equal("abcdef1", buildInfo.CommitSha);
        Assert.Equal(ApplicationBuildInfo.UnknownValue, buildInfo.BuildConfiguration);
    }

    [Fact]
    public void CreateUsesExplicitFallbacksWhenMetadataIsAbsent()
    {
        var buildInfo = ApplicationBuildInfo.Create(
            null,
            null,
            null,
            null,
            "Runtime",
            "OS",
            "X86");

        Assert.Equal(ApplicationBuildInfo.UnknownValue, buildInfo.ProductVersion);
        Assert.Equal(ApplicationBuildInfo.UnknownValue, buildInfo.InformationalVersion);
        Assert.Equal(ApplicationBuildInfo.UnavailableCommit, buildInfo.CommitSha);
        Assert.Equal(ApplicationBuildInfo.ProductName, buildInfo.WindowTitle);
    }

    [Fact]
    public void FromAssemblyReadsGeneratedBuildMetadata()
    {
        var buildInfo = ApplicationBuildInfo.FromAssembly(typeof(App).Assembly);

        Assert.Equal("0.2.0", buildInfo.ProductVersion);
        Assert.StartsWith("0.2.0-", buildInfo.InformationalVersion, StringComparison.Ordinal);
        Assert.NotEqual(ApplicationBuildInfo.UnknownValue, buildInfo.BuildConfiguration);
    }
}
