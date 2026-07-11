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

    [Fact]
    public void Validate_PasswordProperty_NewLocalUser_ReturnsSuccess()
    {
        var result = UserValidator.Validate("TestUser", null, "USERPASSWORD", domain: null, updateIfExists: false);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_BothCredentials_ReturnsUSR011()
    {
        var result = UserValidator.Validate("TestUser", "P@ssw0rd!", "USERPASSWORD", domain: null, updateIfExists: false);

        Assert.True(result.IsFailure);
        Assert.Contains("USR011", result.Error.Message);
    }

    [Fact]
    public void Validate_InvalidNameCharacters_ReturnsUSR003()
    {
        var result = UserValidator.Validate("bad:name", "P@ssw0rd!", null, null, updateIfExists: false);

        Assert.True(result.IsFailure);
        Assert.Contains("USR003", result.Error.Message);
    }

    [Fact]
    public void Validate_LiteralPasswordWithDoubleQuote_ReturnsUSR012()
    {
        var result = UserValidator.Validate("svc", "pa\"ss", null, null, updateIfExists: false);

        Assert.True(result.IsFailure);
        Assert.Contains("USR012", result.Error.Message);
    }

    [Fact]
    public void Validate_DomainUser_NoCredential_ReturnsSuccess()
    {
        // Domain accounts are references (never created), so no credential is required.
        var result = UserValidator.Validate("svc", null, null, "CONTOSO", updateIfExists: false);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_DotDomain_IsLocal_NoCredential_ReturnsUSR002()
    {
        // "." denotes the local machine, so it is a NEW local account and needs a credential — the same
        // predicate the command factory uses when deciding to emit a create step.
        var result = UserValidator.Validate("svc", null, null, ".", updateIfExists: false);

        Assert.True(result.IsFailure);
        Assert.Contains("USR002", result.Error.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("localhost")]
    public void IsLocalDomain_TreatsMachineReferencesAsLocal(string? domain)
        => Assert.True(UserValidator.IsLocalDomain(domain));

    [Fact]
    public void IsLocalDomain_TreatsNamedDomainAsRemote()
        => Assert.False(UserValidator.IsLocalDomain("CONTOSO"));

    [Theory]
    [InlineData("Good.Name")]
    [InlineData("O'Brien")]
    [InlineData("svc_account")]
    public void IsValidAccountName_AcceptsLegalNames(string name)
        => Assert.True(UserValidator.IsValidAccountName(name));

    [Theory]
    [InlineData("bad;name")]
    [InlineData("bad|name")]
    [InlineData("bad\\name")]
    [InlineData("   ")]
    [InlineData("...")]
    public void IsValidAccountName_RejectsIllegalNames(string name)
        => Assert.False(UserValidator.IsValidAccountName(name));
}
