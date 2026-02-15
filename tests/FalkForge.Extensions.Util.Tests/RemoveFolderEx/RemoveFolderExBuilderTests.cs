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
    public void Build_WithProperty_ReturnsSuccess()
    {
        var result = new RemoveFolderExBuilder()
            .Id("rmf1")
            .Property("LOGFOLDER")
            .OnBoth()
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal("LOGFOLDER", result.Value.Property);
        Assert.Equal(RemoveFolderExInstallMode.Both, result.Value.InstallMode);
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
}
