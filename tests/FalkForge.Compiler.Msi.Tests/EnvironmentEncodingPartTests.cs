using FalkForge.Compiler.Msi.Tables;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

/// <summary>
/// Unit-level coverage for the <see cref="EnvironmentVariableModel.Part"/> value-placement
/// axis (WiX <c>Environment/@Part</c>: all / first / last). When <c>Part</c> is set it is the
/// authority for where the new text sits relative to the existing value; when it is null the
/// model's <see cref="EnvironmentVariableAction"/> drives placement (back-compatible default).
/// The compiled-MSI proof lives in <see cref="EnvironmentPartCompiledMsiTests"/>.
/// </summary>
public sealed class EnvironmentEncodingPartTests
{
    [Fact]
    public void EncodeValue_PartLast_ProducesAppendForm()
    {
        var result = EnvironmentEncoding.EncodeValue(
            "v", EnvironmentVariableAction.Set, separator: null, part: EnvironmentVariablePart.Last);

        Assert.Equal("[~];v", result);
    }

    [Fact]
    public void EncodeValue_PartFirst_ProducesPrependForm()
    {
        var result = EnvironmentEncoding.EncodeValue(
            "v", EnvironmentVariableAction.Set, separator: null, part: EnvironmentVariablePart.First);

        Assert.Equal("v;[~]", result);
    }

    [Fact]
    public void EncodeValue_PartAll_ProducesRawValue()
    {
        // Part wins over a conflicting Action: All means "replace the whole value" even though
        // the Action says Append. The two are kept consistent by the fluent builder; this proves
        // the documented Part-wins precedence when they are set directly.
        var result = EnvironmentEncoding.EncodeValue(
            "v", EnvironmentVariableAction.Append, separator: null, part: EnvironmentVariablePart.All);

        Assert.Equal("v", result);
    }

    [Fact]
    public void EncodeValue_PartIsCaseInsensitive()
    {
        var result = EnvironmentEncoding.EncodeValue(
            "v", EnvironmentVariableAction.Set, separator: null, part: "LAST");

        Assert.Equal("[~];v", result);
    }

    [Fact]
    public void EncodeValue_NullPart_FallsBackToAction()
    {
        var result = EnvironmentEncoding.EncodeValue(
            "v", EnvironmentVariableAction.Append, separator: null, part: null);

        Assert.Equal("[~];v", result);
    }

    [Fact]
    public void EncodeName_PartAll_ProducesEqualsPrefix()
    {
        // All -> set/overwrite -> '=' prefix, even though the Action says Append.
        var result = EnvironmentEncoding.EncodeName(
            "PATH", EnvironmentVariableAction.Append, isSystem: false, part: EnvironmentVariablePart.All);

        Assert.Equal("=PATH", result);
    }

    [Fact]
    public void EncodeName_PartLast_OmitsEqualsPrefix()
    {
        var result = EnvironmentEncoding.EncodeName(
            "PATH", EnvironmentVariableAction.Set, isSystem: false, part: EnvironmentVariablePart.Last);

        Assert.DoesNotContain('=', result);
    }
}
