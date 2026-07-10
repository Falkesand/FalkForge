namespace FalkForge.Engine.Tests;

using FalkForge.Engine.Pipeline;
using Xunit;

/// <summary>
/// Tests for <see cref="PropertyNameValidator"/> — MSI property name validation
/// enforced before SetProperty/SetSecureProperty values enter the VariableStore.
/// </summary>
public sealed class PropertyNameValidatorTests
{
    [Fact]
    public void Validate_EmptyString_ReturnsError()
    {
        var result = PropertyNameValidator.Validate("", null);
        Assert.NotNull(result);
        Assert.Contains("empty", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ExactlyMaxLength_Succeeds()
    {
        // MaxPropertyNameLength = 255. A 255-char valid name should succeed.
        var name = new string('A', PropertyNameValidator.MaxPropertyNameLength);
        var result = PropertyNameValidator.Validate(name, null);
        Assert.Null(result); // null = valid
    }

    [Fact]
    public void Validate_OneOverMaxLength_Fails()
    {
        var name = new string('A', PropertyNameValidator.MaxPropertyNameLength + 1);
        var result = PropertyNameValidator.Validate(name, null);
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
    public void Validate_BuiltInVariable_ReturnsBuiltInError(string name)
    {
        var result = PropertyNameValidator.Validate(name, null);
        Assert.NotNull(result);
        Assert.Contains("built-in", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("MYPROP")]
    [InlineData("MY_PROP_123")]
    [InlineData("A")]
    [InlineData("_UNDERSCORE_START")]   // covers ^[A-Z_] first-char alternative branch
    public void Validate_ValidFormat_ReturnsNull(string name)
    {
        var result = PropertyNameValidator.Validate(name, null);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("1INVALID")]    // starts with digit
    [InlineData("-BAD")]        // starts with dash
    [InlineData(" SPACE")]      // starts with space
    [InlineData("lowercase")]   // covers [A-Z] upper-only constraint
    public void Validate_InvalidFormat_ReturnsFormatError(string name)
    {
        var result = PropertyNameValidator.Validate(name, null);
        Assert.NotNull(result);
        Assert.Contains("invalid format", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_DotInMiddle_IsValid()
    {
        // ^[A-Z_][A-Z0-9_.]*$ — dot is allowed
        var result = PropertyNameValidator.Validate("MY.PROPERTY", null);
        Assert.Null(result);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Value-length validation — MaxPropertyValueLength (32767, the MSI property
    // value limit) must actually be enforced on SetProperty/SetSecureProperty
    // values, not just declared. Without it a value is bounded only by the 1 MB
    // wire frame.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidateValueLength_AtMax_ReturnsNull()
    {
        var result = PropertyNameValidator.ValidateValueLength(
            PropertyNameValidator.MaxPropertyValueLength, null);
        Assert.Null(result); // null = valid
    }

    [Fact]
    public void ValidateValueLength_OneOverMax_Fails()
    {
        var result = PropertyNameValidator.ValidateValueLength(
            PropertyNameValidator.MaxPropertyValueLength + 1, null);
        Assert.NotNull(result);
        Assert.Contains("too long", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateValueLength_Zero_ReturnsNull()
    {
        // An empty value is a legal way to clear a property.
        var result = PropertyNameValidator.ValidateValueLength(0, null);
        Assert.Null(result);
    }
}
