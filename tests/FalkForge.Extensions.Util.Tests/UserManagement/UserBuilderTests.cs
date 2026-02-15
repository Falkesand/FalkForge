using FalkForge.Extensions.Util.UserManagement;
using Xunit;

namespace FalkForge.Extensions.Util.Tests.UserManagement;

public sealed class UserBuilderTests
{
    [Fact]
    public void Build_WithNameAndPassword_ReturnsSuccess()
    {
        var result = new UserBuilder()
            .Name("TestUser")
            .Password("P@ssw0rd!")
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal("TestUser", result.Value.Name);
        Assert.Equal("P@ssw0rd!", result.Value.Password);
    }

    [Fact]
    public void Build_WithoutName_ReturnsFailure()
    {
        var result = new UserBuilder()
            .Password("P@ssw0rd!")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("USR001", result.Error.Message);
    }

    [Fact]
    public void Build_WithoutPassword_WhenCreatingNew_ReturnsFailure()
    {
        var result = new UserBuilder()
            .Name("TestUser")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("USR002", result.Error.Message);
    }

    [Fact]
    public void Build_WithoutPassword_WhenUpdateIfExists_ReturnsSuccess()
    {
        var result = new UserBuilder()
            .Name("TestUser")
            .UpdateIfExists()
            .Build();

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.UpdateIfExists);
        Assert.Null(result.Value.Password);
    }

    [Fact]
    public void Build_WithDomain_SetsDomain()
    {
        var result = new UserBuilder()
            .Name("TestUser")
            .Password("P@ssw0rd!")
            .Domain("CONTOSO")
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal("CONTOSO", result.Value.Domain);
    }

    [Fact]
    public void Build_WithAllFlags_SetsAllFlags()
    {
        var result = new UserBuilder()
            .Name("TestUser")
            .Password("P@ssw0rd!")
            .CanNotChangePassword()
            .Disabled()
            .PasswordExpired()
            .PasswordNeverExpires()
            .RemoveOnUninstall()
            .Build();

        Assert.True(result.IsSuccess);
        var user = result.Value;
        Assert.True(user.CanNotChangePassword);
        Assert.True(user.Disabled);
        Assert.True(user.PasswordExpired);
        Assert.True(user.PasswordNeverExpires);
        Assert.True(user.RemoveOnUninstall);
    }

    [Fact]
    public void Build_WithComponentRef_SetsComponentRef()
    {
        var result = new UserBuilder()
            .Name("TestUser")
            .Password("P@ssw0rd!")
            .ComponentRef("comp1")
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal("comp1", result.Value.ComponentRef);
    }

    [Fact]
    public void Build_FluentChaining_ReturnsSameBuilder()
    {
        var builder = new UserBuilder();

        var returned = builder
            .Name("TestUser")
            .Password("P@ssw0rd!")
            .Domain("CONTOSO")
            .CanNotChangePassword()
            .RemoveOnUninstall();

        Assert.Same(builder, returned);
    }
}
