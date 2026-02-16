using FalkForge.Extensions.Util.UserManagement;
using Xunit;

namespace FalkForge.Extensions.Util.Tests.UserManagement;

public sealed class UserValidatorTests
{
    [Fact]
    public void Validate_WithNameAndPassword_ReturnsSuccess()
    {
        var result = UserValidator.Validate("TestUser", "P@ssw0rd!", updateIfExists: false);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_EmptyName_ReturnsUSR001()
    {
        var result = UserValidator.Validate("", "P@ssw0rd!", updateIfExists: false);

        Assert.True(result.IsFailure);
        Assert.Contains("USR001", result.Error.Message);
    }

    [Fact]
    public void Validate_WhitespaceName_ReturnsUSR001()
    {
        var result = UserValidator.Validate("   ", "P@ssw0rd!", updateIfExists: false);

        Assert.True(result.IsFailure);
        Assert.Contains("USR001", result.Error.Message);
    }

    [Fact]
    public void Validate_NullPassword_WhenNotUpdateIfExists_ReturnsUSR002()
    {
        var result = UserValidator.Validate("TestUser", null, updateIfExists: false);

        Assert.True(result.IsFailure);
        Assert.Contains("USR002", result.Error.Message);
    }

    [Fact]
    public void Validate_NullPassword_WhenUpdateIfExists_ReturnsSuccess()
    {
        var result = UserValidator.Validate("TestUser", null, updateIfExists: true);

        Assert.True(result.IsSuccess);
    }
}
