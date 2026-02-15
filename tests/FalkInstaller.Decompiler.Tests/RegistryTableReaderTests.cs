using FalkInstaller.Decompiler.TableReaders;
using Xunit;

namespace FalkInstaller.Decompiler.Tests;

public sealed class RegistryTableReaderTests
{
    [Fact]
    public void Read_EmptyTable_ReturnsEmptyList()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Registry", []);

        var result = RegistryTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Read_MissingTable_ReturnsEmptyList()
    {
        using var access = new MockMsiTableAccess();

        var result = RegistryTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Read_ParsesRegistryEntries()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Registry",
            [
                // Registry, Root, Key, Name, Value, Component_
                ["reg1", "2", "SOFTWARE\\MyApp", "Version", "1.0", "comp1"]
            ]);

        var result = RegistryTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal(RegistryRoot.LocalMachine, result.Value[0].Root);
        Assert.Equal("SOFTWARE\\MyApp", result.Value[0].Key);
        Assert.Equal("Version", result.Value[0].ValueName);
        Assert.Equal("1.0", result.Value[0].Value);
        Assert.Equal("comp1", result.Value[0].ComponentId);
    }

    [Fact]
    public void MapRegistryRoot_AllValues()
    {
        Assert.Equal(RegistryRoot.ClassesRoot, RegistryTableReader.MapRegistryRoot(0));
        Assert.Equal(RegistryRoot.CurrentUser, RegistryTableReader.MapRegistryRoot(1));
        Assert.Equal(RegistryRoot.LocalMachine, RegistryTableReader.MapRegistryRoot(2));
        Assert.Equal(RegistryRoot.Users, RegistryTableReader.MapRegistryRoot(3));
        Assert.Equal(RegistryRoot.LocalMachine, RegistryTableReader.MapRegistryRoot(99));
    }

    [Fact]
    public void ParseRegistryValue_StringValue()
    {
        var (value, type) = RegistryTableReader.ParseRegistryValue("hello");
        Assert.Equal("hello", value);
        Assert.Equal(RegistryValueType.String, type);
    }

    [Fact]
    public void ParseRegistryValue_DWordDecimal()
    {
        var (value, type) = RegistryTableReader.ParseRegistryValue("#42");
        Assert.Equal(42, value);
        Assert.Equal(RegistryValueType.DWord, type);
    }

    [Fact]
    public void ParseRegistryValue_DWordHex()
    {
        var (value, type) = RegistryTableReader.ParseRegistryValue("#xFF");
        Assert.Equal(255, value);
        Assert.Equal(RegistryValueType.DWord, type);
    }

    [Fact]
    public void ParseRegistryValue_ExpandString()
    {
        var (value, type) = RegistryTableReader.ParseRegistryValue("#%expanded_value");
        Assert.Equal("expanded_value", value);
        Assert.Equal(RegistryValueType.ExpandString, type);
    }

    [Fact]
    public void ParseRegistryValue_MultiString()
    {
        var (value, type) = RegistryTableReader.ParseRegistryValue("[~]multi_value");
        Assert.Equal("multi_value", value);
        Assert.Equal(RegistryValueType.MultiString, type);
    }

    [Fact]
    public void ParseRegistryValue_Null_ReturnsStringType()
    {
        var (value, type) = RegistryTableReader.ParseRegistryValue(null);
        Assert.Null(value);
        Assert.Equal(RegistryValueType.String, type);
    }
}
