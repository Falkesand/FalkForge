using FalkForge.Builders;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Core.Tests.Builders;

/// <summary>
/// Guards that each fluent helper on <see cref="RegistryKeyBuilder"/> stamps the
/// <see cref="RegistryEntryModel.ValueType"/> the compiler needs to encode the value
/// correctly, and stores the value in the CLR representation
/// <c>RegistryTableProducer</c> expects to read back (int for DWord, byte[] for
/// Binary, string[] for MultiString, string for ExpandString). Before this fix the
/// producer ignored ValueType entirely and every helper below would have installed
/// as a plain REG_SZ string.
/// </summary>
public sealed class RegistryKeyBuilderTests
{
    [Fact]
    public void DWord_StoresIntValueAndDWordType()
    {
        var builder = new RegistryKeyBuilder(RegistryRoot.LocalMachine, @"Software\Acme");

        builder.DWord("Count", 5);

        RegistryEntryModel entry = Assert.Single(builder.Build());
        Assert.Equal(RegistryValueType.DWord, entry.ValueType);
        Assert.Equal(5, entry.Value);
    }

    [Fact]
    public void Binary_StoresByteArrayAndBinaryType()
    {
        var builder = new RegistryKeyBuilder(RegistryRoot.LocalMachine, @"Software\Acme");
        byte[] bytes = [0x0A, 0xFF];

        builder.Binary("Blob", bytes);

        RegistryEntryModel entry = Assert.Single(builder.Build());
        Assert.Equal(RegistryValueType.Binary, entry.ValueType);
        Assert.Equal(bytes, entry.Value);
    }

    [Fact]
    public void MultiString_StoresStringArrayAndMultiStringType()
    {
        var builder = new RegistryKeyBuilder(RegistryRoot.LocalMachine, @"Software\Acme");

        builder.MultiString("List", ["a", "b", "c"]);

        RegistryEntryModel entry = Assert.Single(builder.Build());
        Assert.Equal(RegistryValueType.MultiString, entry.ValueType);
        Assert.Equal(new[] { "a", "b", "c" }, entry.Value);
    }

    [Fact]
    public void ExpandString_StoresStringAndExpandStringType()
    {
        var builder = new RegistryKeyBuilder(RegistryRoot.LocalMachine, @"Software\Acme");

        builder.ExpandString("Path", "%SystemRoot%\\App");

        RegistryEntryModel entry = Assert.Single(builder.Build());
        Assert.Equal(RegistryValueType.ExpandString, entry.ValueType);
        Assert.Equal("%SystemRoot%\\App", entry.Value);
    }

    [Fact]
    public void MultiString_SingleElement_StillStoresStringArrayAndMultiStringType()
    {
        // A one-element multi-string is an ordinary call; the model must still carry
        // MultiString so the compiler emits the [~] marker (guarding against a REG_SZ downgrade).
        var builder = new RegistryKeyBuilder(RegistryRoot.LocalMachine, @"Software\Acme");

        builder.MultiString("Solo", ["only"]);

        RegistryEntryModel entry = Assert.Single(builder.Build());
        Assert.Equal(RegistryValueType.MultiString, entry.ValueType);
        Assert.Equal(new[] { "only" }, entry.Value);
    }

    [Fact]
    public void Value_DefaultsToStringType()
    {
        var builder = new RegistryKeyBuilder(RegistryRoot.LocalMachine, @"Software\Acme");

        builder.Value("Name", "hello");

        RegistryEntryModel entry = Assert.Single(builder.Build());
        Assert.Equal(RegistryValueType.String, entry.ValueType);
        Assert.Equal("hello", entry.Value);
    }
}
