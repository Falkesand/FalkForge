using FalkForge.Decompiler.TableReaders;
using Xunit;

namespace FalkForge.Decompiler.Tests;

public sealed class PropertyTableReaderTests
{
    [Fact]
    public void Read_EmptyTable_ReturnsEmptyList()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Property", []);

        var result = PropertyTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Read_MissingTable_ReturnsEmptyList()
    {
        using var access = new MockMsiTableAccess();

        var result = PropertyTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Read_FiltersInternalProperties()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Property",
            [
                ["ProductCode", "{12345678-1234-1234-1234-123456789012}"],
                ["ProductName", "Test App"],
                ["Manufacturer", "Test Corp"],
                ["ProductVersion", "1.0.0"],
                ["MY_CUSTOM_PROP", "custom_value"]
            ]);

        var result = PropertyTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("MY_CUSTOM_PROP", result.Value[0].Name);
        Assert.Equal("custom_value", result.Value[0].Value);
    }

    [Fact]
    public void Read_UpperCaseProperty_MarkedAsSecure()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Property",
            [
                ["CUSTOM_SECURE", "value1"]
            ]);

        var result = PropertyTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.True(result.Value[0].IsSecure);
    }

    [Fact]
    public void ReadAll_IncludesInternalProperties()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Property",
            [
                ["ProductName", "Test App"],
                ["Manufacturer", "Test Corp"],
                ["MY_PROP", "value"]
            ]);

        var result = PropertyTableReader.ReadAll(access);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);
        Assert.Equal("Test App", result.Value["ProductName"]);
    }

    [Fact]
    public void Read_NullValue_DefaultsToEmptyString()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Property",
            [
                ["MY_PROP", null]
            ]);

        var result = PropertyTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal(string.Empty, result.Value[0].Value);
    }
}
