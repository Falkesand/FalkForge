using FalkForge.Compiler.Msi.Tables;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

public sealed class EnvironmentEncodingTests
{
    // Canonical MSI Environment Name-prefix encoding per the MSI SDK "Environment Table" topic:
    //   =  set (overwrite existing). Causes MSI to ignore [~] in Value.
    //   +  set only if not already present.
    //   -  remove on uninstall (modifier).
    //   !  remove matching value on install.
    //   *  system scope (not user).
    //   (no prefix / only "-") append or prepend using [~] token in Value.

    [Fact]
    public void EncodeName_SetAction_UserScope_ReturnsEqualsOnly()
    {
        var result = EnvironmentEncoding.EncodeName("PATH", EnvironmentVariableAction.Set, isSystem: false);

        Assert.Equal("=PATH", result);
    }

    [Fact]
    public void EncodeName_SetAction_SystemScope_ReturnsEqualsStar()
    {
        var result = EnvironmentEncoding.EncodeName("PATH", EnvironmentVariableAction.Set, isSystem: true);

        Assert.Equal("=*PATH", result);
    }

    [Fact]
    public void EncodeName_AppendAction_UserScope_ReturnsHyphenOnly()
    {
        var result = EnvironmentEncoding.EncodeName("PATH", EnvironmentVariableAction.Append, isSystem: false);

        Assert.Equal("-PATH", result);
    }

    [Fact]
    public void EncodeName_AppendAction_SystemScope_ReturnsHyphenStar()
    {
        var result = EnvironmentEncoding.EncodeName("PATH", EnvironmentVariableAction.Append, isSystem: true);

        Assert.Equal("-*PATH", result);
    }

    [Fact]
    public void EncodeName_PrependAction_UserScope_ReturnsHyphenOnly()
    {
        var result = EnvironmentEncoding.EncodeName("MYVAR", EnvironmentVariableAction.Prepend, isSystem: false);

        Assert.Equal("-MYVAR", result);
    }

    [Fact]
    public void EncodeName_PrependAction_SystemScope_ReturnsHyphenStar()
    {
        var result = EnvironmentEncoding.EncodeName("MYVAR", EnvironmentVariableAction.Prepend, isSystem: true);

        Assert.Equal("-*MYVAR", result);
    }

    [Fact]
    public void EncodeName_AppendAction_DoesNotEmitBuggyEqualsHyphenPrefix()
    {
        // Regression: "=-PATH" forces MSI overwrite semantics and silently drops [~] in Value,
        // causing PATH append to replace the user's existing PATH. Must never be emitted for append.
        var user = EnvironmentEncoding.EncodeName("PATH", EnvironmentVariableAction.Append, isSystem: false);
        var system = EnvironmentEncoding.EncodeName("PATH", EnvironmentVariableAction.Append, isSystem: true);

        Assert.NotEqual("=-PATH", user);
        Assert.NotEqual("=-PATH", system);
        Assert.NotEqual("=-*PATH", system);
        Assert.DoesNotContain('=', user);
        Assert.DoesNotContain('=', system);
    }

    [Fact]
    public void EncodeName_PrependAction_DoesNotEmitBuggyEqualsHyphenPrefix()
    {
        var user = EnvironmentEncoding.EncodeName("PATH", EnvironmentVariableAction.Prepend, isSystem: false);
        var system = EnvironmentEncoding.EncodeName("PATH", EnvironmentVariableAction.Prepend, isSystem: true);

        Assert.DoesNotContain('=', user);
        Assert.DoesNotContain('=', system);
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
        var result = EnvironmentEncoding.EncodeName("MyAppHome", EnvironmentVariableAction.Set, isSystem: false);

        Assert.Equal("=MyAppHome", result);
    }

    [Fact]
    public void EncodeValue_SetAction_EmptyValue_ReturnsEmpty()
    {
        var result = EnvironmentEncoding.EncodeValue("", EnvironmentVariableAction.Set, separator: null);

        Assert.Equal("", result);
    }
}
