using FalkForge.Decompiler.Recipe;
using FalkForge.Decompiler.Recipe.Schemas;
using Xunit;

namespace FalkForge.Decompiler.Tests.Schemas;

public sealed class FileSchemaTests
{
    [Fact]
    public void Read_FullRow_MapsAllFields()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("File",
            [
                ["file1.exe", "comp1", "FILENA~1|file1.exe", "12345", "1.0.0.0", "1033", "0", "1"]
            ]);

        var result = TableReadEngine.ReadOne(FileSchema.Schema, access);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        var row = result.Value[0];
        Assert.Equal("file1.exe", row.File);
        Assert.Equal("comp1", row.Component_);
        Assert.Equal("FILENA~1|file1.exe", row.FileName);
        Assert.Equal(12345, row.FileSize);
        Assert.Equal("1.0.0.0", row.Version);
        Assert.Equal("1033", row.Language);
        Assert.Equal(0, row.Attributes);
        Assert.Equal(1, row.Sequence);
    }

    [Fact]
    public void Read_NullableVersionAndLanguage_Allowed()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("File", [["f1", "c1", "file1.txt", "0", null, null, "0", "1"]]);

        var result = TableReadEngine.ReadOne(FileSchema.Schema, access);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value[0].Version);
        Assert.Null(result.Value[0].Language);
    }

    [Fact]
    public void Read_MissingTable_ReturnsEmpty()
    {
        using var access = new MockMsiTableAccess();
        var result = TableReadEngine.ReadOne(FileSchema.Schema, access);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }
}
