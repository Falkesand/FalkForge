using FalkForge.Extensions.Util.RemoveFolderEx;
using Xunit;

namespace FalkForge.Extensions.Util.Tests.RemoveFolderEx;

public sealed class RemoveFolderExBuilderTests
{
    [Fact]
    public void Build_WithDirectory_ReturnsSuccess()
    {
        var result = new RemoveFolderExBuilder()
            .Id("rmf1")
            .Directory("INSTALLFOLDER")
            .OnUninstall()
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal("rmf1", result.Value.Id);
        Assert.Equal("INSTALLFOLDER", result.Value.Directory);
        Assert.Equal(RemoveFolderExInstallMode.Uninstall, result.Value.InstallMode);
    }

    [Fact]
    public void Build_WithProperty_OnInstall_ReturnsSuccess()
    {
        // Property (a live MSI token, e.g. an install folder only known at run time) is resolvable via
        // the execution seam's CustomActionData channel for the INSTALL action only (RFX004), so
        // OnInstall is the supported combination.
        var result = new RemoveFolderExBuilder()
            .Id("rmf1")
            .Property("LOGFOLDER")
            .OnInstall()
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal("LOGFOLDER", result.Value.Property);
        Assert.Equal(RemoveFolderExInstallMode.Install, result.Value.InstallMode);
    }

    [Fact]
    public void Build_WithoutId_ReturnsFailure()
    {
        var result = new RemoveFolderExBuilder()
            .Directory("INSTALLFOLDER")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("RFX001", result.Error.Message);
    }

    [Fact]
    public void Build_WithoutDirectoryOrProperty_ReturnsFailure()
    {
        var result = new RemoveFolderExBuilder()
            .Id("rmf1")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("RFX002", result.Error.Message);
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:")]
    [InlineData(@"D:\")]
    [InlineData(@"\")]
    [InlineData(@"\\server\share")]
    [InlineData(@"\\server\share\")]
    public void Build_WithRootLikeDirectory_ReturnsFailure(string rootLikePath)
    {
        var result = new RemoveFolderExBuilder()
            .Id("rmf1")
            .Directory(rootLikePath)
            .OnUninstall()
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("RFX003", result.Error.Message);
    }

    [Fact]
    public void Build_WithSubfolderDirectory_ReturnsSuccess()
    {
        // A real subfolder under a drive root must NOT be rejected by the root-path guard.
        var result = new RemoveFolderExBuilder()
            .Id("rmf1")
            .Directory(@"C:\ProgramData\MyApp\Cache")
            .OnUninstall()
            .Build();

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(nameof(RemoveFolderExBuilder.OnUninstall))]
    [InlineData(nameof(RemoveFolderExBuilder.OnBoth))]
    public void Build_WithPropertyAndUninstallMode_ReturnsFailure(string mode)
    {
        var builder = new RemoveFolderExBuilder().Id("rmf1").Property("LOGFOLDER");
        var result = (mode == nameof(RemoveFolderExBuilder.OnUninstall) ? builder.OnUninstall() : builder.OnBoth())
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("RFX004", result.Error.Message);
    }
}
