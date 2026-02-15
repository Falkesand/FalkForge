using FalkInstaller.Extensions.Util.FileShare;
using Xunit;

namespace FalkInstaller.Extensions.Util.Tests.FileShare;

public sealed class FileShareBuilderTests
{
    [Fact]
    public void Build_WithRequiredFields_ReturnsSuccess()
    {
        var result = new FileShareBuilder()
            .Id("share1")
            .Name("MyShare")
            .Directory("INSTALLFOLDER")
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal("share1", result.Value.Id);
        Assert.Equal("MyShare", result.Value.Name);
        Assert.Equal("INSTALLFOLDER", result.Value.Directory);
    }

    [Fact]
    public void Build_WithoutId_ReturnsFailure()
    {
        var result = new FileShareBuilder()
            .Name("MyShare")
            .Directory("INSTALLFOLDER")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("FSH001", result.Error.Message);
    }

    [Fact]
    public void Build_WithoutName_ReturnsFailure()
    {
        var result = new FileShareBuilder()
            .Id("share1")
            .Directory("INSTALLFOLDER")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("FSH002", result.Error.Message);
    }

    [Fact]
    public void Build_WithPermissions_SetsPermissions()
    {
        var result = new FileShareBuilder()
            .Id("share1")
            .Name("MyShare")
            .Directory("INSTALLFOLDER")
            .GrantRead("Everyone")
            .GrantChange("Users")
            .GrantFull("Administrators")
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Permissions.Count);
        Assert.Equal("Everyone", result.Value.Permissions[0].User);
        Assert.Equal(FileSharePermissionLevel.Read, result.Value.Permissions[0].Permission);
        Assert.Equal("Users", result.Value.Permissions[1].User);
        Assert.Equal(FileSharePermissionLevel.Change, result.Value.Permissions[1].Permission);
        Assert.Equal("Administrators", result.Value.Permissions[2].User);
        Assert.Equal(FileSharePermissionLevel.Full, result.Value.Permissions[2].Permission);
    }

    [Fact]
    public void Build_WithDescription_SetsDescription()
    {
        var result = new FileShareBuilder()
            .Id("share1")
            .Name("MyShare")
            .Directory("INSTALLFOLDER")
            .Description("Shared folder for data")
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal("Shared folder for data", result.Value.Description);
    }
}
