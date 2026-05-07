using FalkForge.Decompiler.Recipe;
using FalkForge.Decompiler.Recipe.Schemas;
using Xunit;

namespace FalkForge.Decompiler.Tests.Schemas;

public sealed class DirectorySchemaTests
{
    [Fact]
    public void Read_FullRow_MapsAllFields()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Directory", [["INSTALLFOLDER", "ProgramFilesFolder", "MyApp"]]);

        var result = TableReadEngine.ReadOne(DirectorySchema.Schema, access);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("INSTALLFOLDER", result.Value[0].Directory);
        Assert.Equal("ProgramFilesFolder", result.Value[0].Directory_Parent);
        Assert.Equal("MyApp", result.Value[0].DefaultDir);
    }

    [Fact]
    public void Read_NullParent_Allowed()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Directory", [["TARGETDIR", null, "SourceDir"]]);

        var result = TableReadEngine.ReadOne(DirectorySchema.Schema, access);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value[0].Directory_Parent);
    }

    [Fact]
    public void Read_MissingTable_ReturnsEmpty()
    {
        using var access = new MockMsiTableAccess();
        var result = TableReadEngine.ReadOne(DirectorySchema.Schema, access);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }
}
