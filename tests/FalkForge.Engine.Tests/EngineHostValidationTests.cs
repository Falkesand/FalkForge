namespace FalkForge.Engine.Tests;

using FalkForge.Engine.Logging;
using Xunit;

public sealed class EngineHostValidationTests
{
    private readonly NullLogger _logger = new();

    [Fact]
    public void ValidatePropertyName_EmptyString_ReturnsError()
    {
        var result = EngineHost.ValidatePropertyName("", _logger);
        Assert.NotNull(result);
        Assert.Contains("empty", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatePropertyName_ExactlyMaxLength_Succeeds()
    {
        // MaxPropertyNameLength = 255. A 255-char valid name should succeed.
        var name = new string('A', 255);
        var result = EngineHost.ValidatePropertyName(name, _logger);
        Assert.Null(result); // null = valid
    }

    [Fact]
    public void ValidatePropertyName_OneOverMaxLength_Fails()
    {
        var name = new string('A', 256);
        var result = EngineHost.ValidatePropertyName(name, _logger);
        Assert.NotNull(result);
        Assert.Contains("too long", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("VersionNT")]
    [InlineData("VersionNTMajor")]
    [InlineData("ProcessorArchitecture")]
    [InlineData("ProgramFilesFolder")]
    [InlineData("ComputerName")]
    [InlineData("RebootPending")]
    [InlineData("SystemLanguageID")]
    public void ValidatePropertyName_BuiltInVariable_ReturnsBuiltInError(string name)
    {
        var result = EngineHost.ValidatePropertyName(name, _logger);
        Assert.NotNull(result);
        Assert.Contains("built-in", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("MYPROP")]
    [InlineData("MY_PROP_123")]
    [InlineData("A")]
    public void ValidatePropertyName_ValidFormat_ReturnsNull(string name)
    {
        var result = EngineHost.ValidatePropertyName(name, _logger);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("1INVALID")]    // starts with digit
    [InlineData("-BAD")]        // starts with dash
    [InlineData(" SPACE")]      // starts with space
    public void ValidatePropertyName_InvalidFormat_ReturnsFormatError(string name)
    {
        var result = EngineHost.ValidatePropertyName(name, _logger);
        Assert.NotNull(result);
        Assert.Contains("invalid format", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatePropertyName_DotInMiddle_IsValid()
    {
        // ^[A-Z_][A-Z0-9_.]*$ — dot is allowed
        var result = EngineHost.ValidatePropertyName("MY.PROPERTY", _logger);
        Assert.Null(result);
    }
}
