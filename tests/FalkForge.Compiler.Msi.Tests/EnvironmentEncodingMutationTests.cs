using FalkForge.Compiler.Msi.Tables;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

public sealed class EnvironmentEncodingMutationTests
{
    [Fact]
    public void EncodeName_Set_PrefixIsExactlyEqualsHyphen()
    {
        var result = EnvironmentEncoding.EncodeName("VAR", EnvironmentVariableAction.Set);

        Assert.Equal("=-VAR", result);
        Assert.StartsWith("=-", result);
        Assert.EndsWith("VAR", result);
        Assert.Equal(5, result.Length);
    }

    [Fact]
    public void EncodeName_Append_PrefixIsExactlyEqualsHyphen()
    {
        var result = EnvironmentEncoding.EncodeName("VAR", EnvironmentVariableAction.Append);

        Assert.Equal("=-VAR", result);
    }

    [Fact]
    public void EncodeName_Prepend_PrefixIsExactlyEqualsHyphen()
    {
        var result = EnvironmentEncoding.EncodeName("VAR", EnvironmentVariableAction.Prepend);

        Assert.Equal("=-VAR", result);
    }

    [Fact]
    public void EncodeValue_Set_ReturnsExactValue()
    {
        var result = EnvironmentEncoding.EncodeValue("hello", EnvironmentVariableAction.Set, null);

        Assert.Equal("hello", result);
    }

    [Fact]
    public void EncodeValue_Append_DefaultSeparator_Format()
    {
        var result = EnvironmentEncoding.EncodeValue("val", EnvironmentVariableAction.Append, null);

        // [~];val - tilde first, then separator, then value
        Assert.Equal("[~];val", result);
        Assert.StartsWith("[~]", result);
        Assert.EndsWith("val", result);
        Assert.Equal(7, result.Length);
    }

    [Fact]
    public void EncodeValue_Prepend_DefaultSeparator_Format()
    {
        var result = EnvironmentEncoding.EncodeValue("val", EnvironmentVariableAction.Prepend, null);

        // val;[~] - value first, then separator, then tilde
        Assert.Equal("val;[~]", result);
        Assert.StartsWith("val", result);
        Assert.EndsWith("[~]", result);
        Assert.Equal(7, result.Length);
    }

    [Fact]
    public void EncodeValue_Append_CustomSeparator_UsedCorrectly()
    {
        var result = EnvironmentEncoding.EncodeValue("val", EnvironmentVariableAction.Append, "|");

        Assert.Equal("[~]|val", result);
        Assert.Contains("|", result);
        Assert.DoesNotContain(";", result);
    }

    [Fact]
    public void EncodeValue_Prepend_CustomSeparator_UsedCorrectly()
    {
        var result = EnvironmentEncoding.EncodeValue("val", EnvironmentVariableAction.Prepend, "|");

        Assert.Equal("val|[~]", result);
        Assert.Contains("|", result);
        Assert.DoesNotContain(";", result);
    }

    [Fact]
    public void EncodeValue_Append_SeparatorPosition_BetweenTildeAndValue()
    {
        var result = EnvironmentEncoding.EncodeValue("data", EnvironmentVariableAction.Append, ":");

        // Should be exactly [~]:data
        Assert.Equal("[~]:data", result);
        var tildeEnd = result.IndexOf(']') + 1;
        Assert.Equal(':', result[tildeEnd]);
    }

    [Fact]
    public void EncodeValue_Prepend_SeparatorPosition_BetweenValueAndTilde()
    {
        var result = EnvironmentEncoding.EncodeValue("data", EnvironmentVariableAction.Prepend, ":");

        // Should be exactly data:[~]
        Assert.Equal("data:[~]", result);
        Assert.Equal(':', result[4]);
    }

    [Fact]
    public void EncodeValue_NullSeparator_DefaultsToSemicolon()
    {
        var appendResult = EnvironmentEncoding.EncodeValue("v", EnvironmentVariableAction.Append, null);
        var prependResult = EnvironmentEncoding.EncodeValue("v", EnvironmentVariableAction.Prepend, null);

        Assert.Contains(";", appendResult);
        Assert.Contains(";", prependResult);
    }

    [Fact]
    public void EncodeValue_Set_IgnoresSeparator()
    {
        var withNull = EnvironmentEncoding.EncodeValue("val", EnvironmentVariableAction.Set, null);
        var withCustom = EnvironmentEncoding.EncodeValue("val", EnvironmentVariableAction.Set, "|");

        // Set action should return raw value regardless of separator
        Assert.Equal("val", withNull);
        Assert.Equal("val", withCustom);
    }

    [Fact]
    public void EncodeValue_EmptyValue_Set_ReturnsEmpty()
    {
        var result = EnvironmentEncoding.EncodeValue("", EnvironmentVariableAction.Set, null);

        Assert.Equal("", result);
    }

    [Fact]
    public void EncodeValue_EmptyValue_Append_StillProducesPattern()
    {
        var result = EnvironmentEncoding.EncodeValue("", EnvironmentVariableAction.Append, null);

        Assert.Equal("[~];", result);
    }

    [Fact]
    public void EncodeValue_EmptyValue_Prepend_StillProducesPattern()
    {
        var result = EnvironmentEncoding.EncodeValue("", EnvironmentVariableAction.Prepend, null);

        Assert.Equal(";[~]", result);
    }

    [Fact]
    public void EncodeName_EmptyName_StillPrefixed()
    {
        var result = EnvironmentEncoding.EncodeName("", EnvironmentVariableAction.Set);

        Assert.Equal("=-", result);
    }
}
