using System.Runtime.Versioning;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

[SupportedOSPlatform("windows")]
public sealed class MsmCompilerTests
{
    [Fact]
    public void DeterministicComponentGuid_SameInputs_ProducesSameGuid()
    {
        var moduleGuid = Guid.Parse("12345678-1234-1234-1234-123456789ABC");
        var componentId = "MyComponent";

        var guid1 = MsmCompiler.DeterministicComponentGuid(moduleGuid, componentId);
        var guid2 = MsmCompiler.DeterministicComponentGuid(moduleGuid, componentId);

        Assert.Equal(guid1, guid2);
    }

    [Fact]
    public void DeterministicComponentGuid_DifferentComponents_ProduceDifferentGuids()
    {
        var moduleGuid = Guid.Parse("12345678-1234-1234-1234-123456789ABC");

        var guid1 = MsmCompiler.DeterministicComponentGuid(moduleGuid, "ComponentA");
        var guid2 = MsmCompiler.DeterministicComponentGuid(moduleGuid, "ComponentB");

        Assert.NotEqual(guid1, guid2);
    }

    [Fact]
    public void DeterministicComponentGuid_DifferentModules_ProduceDifferentGuids()
    {
        var module1 = Guid.Parse("12345678-1234-1234-1234-123456789ABC");
        var module2 = Guid.Parse("ABCDEF01-2345-6789-ABCD-EF0123456789");

        var guid1 = MsmCompiler.DeterministicComponentGuid(module1, "SameComponent");
        var guid2 = MsmCompiler.DeterministicComponentGuid(module2, "SameComponent");

        Assert.NotEqual(guid1, guid2);
    }

    [Fact]
    public void DeterministicComponentGuid_ResultIsValidRfc4122()
    {
        var moduleGuid = Guid.Parse("12345678-1234-1234-1234-123456789ABC");
        var result = MsmCompiler.DeterministicComponentGuid(moduleGuid, "TestComponent");

        // Check version nibble is 5 (SHA-based)
        var bytes = result.ToByteArray();
        Assert.Equal(0x50, bytes[7] & 0xF0);

        // Check variant bits are RFC4122 (10xx xxxx)
        Assert.Equal(0x80, bytes[8] & 0xC0);
    }

    [Fact]
    public void PrefixComponentId_ShortId_ReturnsSimplePrefix()
    {
        var moduleGuid = "12345678123412341234123456789ABC";
        var result = MsmCompiler.PrefixComponentId(moduleGuid, "Comp1");

        Assert.Equal($"{moduleGuid}.Comp1", result);
        Assert.True(result.Length <= 72);
    }

    [Fact]
    public void PrefixComponentId_LongId_ReturnsHashedPrefix()
    {
        var moduleGuid = "12345678123412341234123456789ABC";
        // Create a component ID that when prefixed exceeds 72 chars
        // moduleGuid is 32 chars + "." = 33 chars prefix, so component > 39 chars triggers
        var longComponent = new string('A', 50);
        var result = MsmCompiler.PrefixComponentId(moduleGuid, longComponent);

        Assert.True(result.Length <= 72);
        // Should start with first 8 chars of moduleGuid
        Assert.StartsWith("12345678.", result);
    }

    [Fact]
    public void PrefixComponentId_TwoLongIdsSharingPrefix_ProduceDifferentResults()
    {
        var moduleGuid = "12345678123412341234123456789ABC";
        // Two component IDs that share the first 39 chars but differ after
        var component1 = new string('A', 39) + "XXXXX_One";
        var component2 = new string('A', 39) + "XXXXX_Two";

        var result1 = MsmCompiler.PrefixComponentId(moduleGuid, component1);
        var result2 = MsmCompiler.PrefixComponentId(moduleGuid, component2);

        Assert.NotEqual(result1, result2);
        Assert.True(result1.Length <= 72);
        Assert.True(result2.Length <= 72);
    }

    [Fact]
    public void PrefixComponentId_ExactlyAt72Chars_ReturnsSimplePrefix()
    {
        var moduleGuid = "12345678123412341234123456789ABC";
        // moduleGuid is 32 chars + "." = 33 chars prefix
        // 72 - 33 = 39 chars remaining for component ID
        var component = new string('X', 39);
        var result = MsmCompiler.PrefixComponentId(moduleGuid, component);

        Assert.Equal($"{moduleGuid}.{component}", result);
        Assert.Equal(72, result.Length);
    }

    [Fact]
    public void PrefixComponentId_OneCharOver72_UsesHashedPrefix()
    {
        var moduleGuid = "12345678123412341234123456789ABC";
        // 40 chars component = 33 + 40 = 73, exceeds 72
        var component = new string('X', 40);
        var result = MsmCompiler.PrefixComponentId(moduleGuid, component);

        Assert.True(result.Length <= 72);
        Assert.StartsWith("12345678.", result);
    }
}
