using FalkInstaller.Decompiler.TableReaders;
using Xunit;

namespace FalkInstaller.Decompiler.Tests;

public sealed class ComponentTableReaderTests
{
    [Fact]
    public void Read_EmptyTable_ReturnsEmptyList()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Component", []);

        var result = ComponentTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Read_MissingTable_ReturnsEmptyList()
    {
        using var access = new MockMsiTableAccess();

        var result = ComponentTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Read_ParsesComponentEntry()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Component",
            [
                // Component, ComponentId, Directory_, Attributes, Condition, KeyPath
                ["comp1", "{11111111-1111-1111-1111-111111111111}", "INSTALLFOLDER", "256", "SOME_CONDITION", "file1"]
            ]);

        var result = ComponentTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("comp1", result.Value[0].ComponentName);
        Assert.Equal("{11111111-1111-1111-1111-111111111111}", result.Value[0].ComponentId);
        Assert.Equal("INSTALLFOLDER", result.Value[0].DirectoryId);
        Assert.Equal(256, result.Value[0].Attributes);
        Assert.Equal("SOME_CONDITION", result.Value[0].Condition);
        Assert.Equal("file1", result.Value[0].KeyPath);
    }

    [Fact]
    public void Read_NullOptionalFields_Handled()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Component",
            [
                ["comp1", null, "INSTALLFOLDER", "0", null, null]
            ]);

        var result = ComponentTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Null(result.Value[0].ComponentId);
        Assert.Null(result.Value[0].Condition);
        Assert.Null(result.Value[0].KeyPath);
    }
}
