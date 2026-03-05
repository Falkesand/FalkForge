namespace FalkForge.Engine.Tests.Cache;

using FalkForge;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class AuthenticodeValidationTests
{
    [Fact]
    public void ValidSignature_ReturnsSuccess()
    {
        var validator = new MockAuthenticodeValidator().ReturnsSuccess();

        var result = validator.ValidateSignature("test.msi", "ABC123");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void InvalidSignature_ReturnsSecurityError()
    {
        var validator = new MockAuthenticodeValidator().ReturnsFailure("Invalid signature");

        var result = validator.ValidateSignature("test.msi", "ABC123");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("Invalid signature", result.Error.Message);
    }

    [Fact]
    public void WrongThumbprint_ReturnsFailure()
    {
        var validator = new MockAuthenticodeValidator()
            .ReturnsFailure("Certificate thumbprint mismatch");

        var result = validator.ValidateSignature("test.msi", "WRONG_THUMBPRINT");

        Assert.True(result.IsFailure);
        Assert.Contains("thumbprint mismatch", result.Error.Message);
    }

    [Fact]
    public void NullThumbprint_SkipsThumbprintCheck()
    {
        var validator = new MockAuthenticodeValidator().ReturnsSuccess();

        var result = validator.ValidateSignature("test.msi", null);

        Assert.True(result.IsSuccess);
        Assert.Null(validator.LastThumbprint);
    }

    [Fact]
    public void MissingFile_ReturnsFailure()
    {
        var validator = new MockAuthenticodeValidator()
            .ReturnsFailure("File not found");

        var result = validator.ValidateSignature("nonexistent.msi", null);

        Assert.True(result.IsFailure);
        Assert.Contains("File not found", result.Error.Message);
    }
}
