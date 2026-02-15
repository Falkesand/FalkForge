using FalkForge.Compiler.Msi.Tables;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

public sealed class EnvironmentEncodingTests
{
    [Fact]
    public void EncodeName_SetAction_PrefixesWithEqualsHyphen()
    {
        var result = EnvironmentEncoding.EncodeName("PATH", EnvironmentVariableAction.Set);

        Assert.Equal("=-PATH", result);
    }

    [Fact]
    public void EncodeName_AppendAction_PrefixesWithEqualsHyphen()
    {
        var result = EnvironmentEncoding.EncodeName("PATH", EnvironmentVariableAction.Append);

        Assert.Equal("=-PATH", result);
    }

    [Fact]
    public void EncodeName_PrependAction_PrefixesWithEqualsHyphen()
    {
        var result = EnvironmentEncoding.EncodeName("MYVAR", EnvironmentVariableAction.Prepend);

        Assert.Equal("=-MYVAR", result);
    }

    [Fact]
    public void EncodeValue_SetAction_ReturnsRawValue()
    {
        var result = EnvironmentEncoding.EncodeValue(@"C:\App\bin", EnvironmentVariableAction.Set, separator: null);

        Assert.Equal(@"C:\App\bin", result);
    }

    [Fact]
    public void EncodeValue_AppendAction_DefaultSeparator_PrependsTildeAndSemicolon()
    {
        var result = EnvironmentEncoding.EncodeValue(@"C:\App\bin", EnvironmentVariableAction.Append, separator: null);

        Assert.Equal(@"[~];C:\App\bin", result);
    }

    [Fact]
    public void EncodeValue_PrependAction_DefaultSeparator_AppendsSemicolonAndTilde()
    {
        var result = EnvironmentEncoding.EncodeValue(@"C:\App\bin", EnvironmentVariableAction.Prepend, separator: null);

        Assert.Equal(@"C:\App\bin;[~]", result);
    }

    [Fact]
    public void EncodeValue_AppendAction_CustomSeparator_UsesCustomSeparator()
    {
        var result = EnvironmentEncoding.EncodeValue("value", EnvironmentVariableAction.Append, separator: ":");

        Assert.Equal("[~]:value", result);
    }

    [Fact]
    public void EncodeValue_PrependAction_CustomSeparator_UsesCustomSeparator()
    {
        var result = EnvironmentEncoding.EncodeValue("value", EnvironmentVariableAction.Prepend, separator: ":");

        Assert.Equal("value:[~]", result);
    }

    [Fact]
    public void EncodeName_PreservesVariableNameCasing()
    {
        var result = EnvironmentEncoding.EncodeName("MyAppHome", EnvironmentVariableAction.Set);

        Assert.Equal("=-MyAppHome", result);
    }

    [Fact]
    public void EncodeValue_SetAction_EmptyValue_ReturnsEmpty()
    {
        var result = EnvironmentEncoding.EncodeValue("", EnvironmentVariableAction.Set, separator: null);

        Assert.Equal("", result);
    }
}
