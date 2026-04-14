using FalkForge.Compiler.Msi.Tables;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

public sealed class EnvironmentNameEncoderTests
{
    // Full truth table for (Action, IsSystem) -> Name prefix per the MSI SDK
    // "Environment Table" topic. "Id" is the literal variable name; no transformation
    // other than prefix composition is applied.
    public static TheoryData<EnvironmentVariableAction, bool, string, string> EncodingMatrix()
    {
        const string Id = "PATH";
        return new TheoryData<EnvironmentVariableAction, bool, string, string>
        {
            { EnvironmentVariableAction.Set,     false, Id, "=PATH"   },
            { EnvironmentVariableAction.Set,     true,  Id, "=*PATH"  },
            { EnvironmentVariableAction.Append,  false, Id, "-PATH"   },
            { EnvironmentVariableAction.Append,  true,  Id, "-*PATH"  },
            { EnvironmentVariableAction.Prepend, false, Id, "-PATH"   },
            { EnvironmentVariableAction.Prepend, true,  Id, "-*PATH"  }
        };
    }

    [Theory]
    [MemberData(nameof(EncodingMatrix))]
    public void Encode_MatchesCanonicalMsiPrefix(
        EnvironmentVariableAction action,
        bool isSystem,
        string variableName,
        string expected)
    {
        var result = EnvironmentNameEncoder.Encode(action, isSystem, variableName);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Encode_PreservesVariableNameVerbatim()
    {
        var result = EnvironmentNameEncoder.Encode(
            EnvironmentVariableAction.Append,
            isSystem: true,
            variableName: "My.Weird_Var-Name");

        Assert.Equal("-*My.Weird_Var-Name", result);
    }

    [Fact]
    public void Encode_EmptyName_ReturnsPrefixOnly()
    {
        var result = EnvironmentNameEncoder.Encode(
            EnvironmentVariableAction.Set,
            isSystem: true,
            variableName: string.Empty);

        Assert.Equal("=*", result);
    }

    [Theory]
    [InlineData(EnvironmentVariableAction.Append, false)]
    [InlineData(EnvironmentVariableAction.Append, true)]
    [InlineData(EnvironmentVariableAction.Prepend, false)]
    [InlineData(EnvironmentVariableAction.Prepend, true)]
    public void Encode_AppendOrPrepend_NeverIncludesEqualsPrefix(
        EnvironmentVariableAction action,
        bool isSystem)
    {
        // Regression guard: an '=' in the Name prefix forces MSI overwrite semantics,
        // which silently drops the [~] preservation token in Value and replaces the
        // user's existing variable instead of modifying it.
        var result = EnvironmentNameEncoder.Encode(action, isSystem, "PATH");

        Assert.DoesNotContain('=', result);
    }
}
