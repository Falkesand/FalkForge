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

    [Fact]
    public void Build_WithPasswordProperty_NoLiteral_Succeeds()
    {
        var result = new UserBuilder()
            .Name("TestUser")
            .PasswordProperty("USERPASSWORD")
            .Build();

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.Equal("USERPASSWORD", result.Value.PasswordProperty);
        Assert.Null(result.Value.Password);
    }

    [Fact]
    public void Build_WithBothPasswordAndPasswordProperty_ReturnsUSR011()
    {
        var result = new UserBuilder()
            .Name("TestUser")
            .Password("P@ssw0rd!")
            .PasswordProperty("USERPASSWORD")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("USR011", result.Error.Message);
    }

    [Fact]
    public void Build_WithInjectionInName_ReturnsUSR003()
    {
        var result = new UserBuilder()
            .Name("bad;name")
            .Password("P@ssw0rd!")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("USR003", result.Error.Message);
    }

    [Fact]
    public void Build_MemberOf_StoresGroups()
    {
        var result = new UserBuilder()
            .Name("TestUser")
            .Password("P@ssw0rd!")
            .MemberOf("Administrators")
            .MemberOf("Ops")
            .Build();

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.Equal(["Administrators", "Ops"], result.Value.Groups);
    }

    [Fact]
    public void Build_MemberOf_InvalidGroupName_ReturnsUSR003()
    {
        var result = new UserBuilder()
            .Name("TestUser")
            .Password("P@ssw0rd!")
            .MemberOf("bad|group")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("USR003", result.Error.Message);
    }

    [Fact]
    public void Build_DomainUser_NoPassword_Succeeds()
    {
        // A domain-qualified user is a reference (never created locally), so USR002 must not fire.
        var result = new UserBuilder()
            .Name("svc")
            .Domain("CONTOSO")
            .MemberOf("Ops")
            .Build();

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
    }
}
