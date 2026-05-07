using FalkForge.Decompiler.Recipe;
using FalkForge.Decompiler.Recipe.Schemas;
using Xunit;

namespace FalkForge.Decompiler.Tests.Schemas;

public sealed class ShortcutSchemaTests
{
    [Fact]
    public void Read_FullRow_MapsAllFields()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Shortcut",
            [
                ["sc1", "DesktopFolder", "APPNA~1|My App", "comp1", "[INSTALLFOLDER]MyApp.exe",
                 "/run", "Launch My App", null, "icon.ico", "0", null, "INSTALLFOLDER"]
            ]);

        var result = TableReadEngine.ReadOne(ShortcutSchema.Schema, access);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        var row = result.Value[0];
        Assert.Equal("sc1", row.Shortcut);
        Assert.Equal("DesktopFolder", row.Directory_);
        Assert.Equal("APPNA~1|My App", row.Name);
        Assert.Equal("/run", row.Arguments);
        Assert.Equal("icon.ico", row.Icon_);
        Assert.Equal(0, row.IconIndex);
    }

    [Fact]
    public void Read_NullOptionalFields_Handled()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Shortcut",
            [
                ["sc1", "DesktopFolder", "App", "comp1", "[INSTALLFOLDER]app.exe",
                 null, null, null, null, null, null, null]
            ]);

        var result = TableReadEngine.ReadOne(ShortcutSchema.Schema, access);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value[0].Arguments);
        Assert.Null(result.Value[0].Icon_);
        Assert.Null(result.Value[0].IconIndex);
    }

    [Fact]
    public void Read_MissingTable_ReturnsEmpty()
    {
        using var access = new MockMsiTableAccess();
        var result = TableReadEngine.ReadOne(ShortcutSchema.Schema, access);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }
}
