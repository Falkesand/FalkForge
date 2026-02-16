using FalkForge.Decompiler.TableReaders;
using Xunit;

namespace FalkForge.Decompiler.Tests;

public sealed class ShortcutTableReaderTests
{
    [Fact]
    public void Read_EmptyTable_ReturnsEmptyList()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Shortcut", []);

        var result = ShortcutTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Read_MissingTable_ReturnsEmptyList()
    {
        using var access = new MockMsiTableAccess();

        var result = ShortcutTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Read_ParsesShortcutEntry()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Shortcut",
            [
                // Shortcut, Directory_, Name, Component_, Target, Arguments, Description,
                // Hotkey, Icon_, IconIndex, ShowCmd, WkDir
                ["sc1", "DesktopFolder", "SHORT~1|My App", "comp1", "[INSTALLFOLDER]app.exe", "--flag", "Launch My App", null, "app.ico", "0", "1", "INSTALLFOLDER"]
            ]);

        var result = ShortcutTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("My App", result.Value[0].Name);
        Assert.Equal("[INSTALLFOLDER]app.exe", result.Value[0].TargetFile);
        Assert.Equal("--flag", result.Value[0].Arguments);
        Assert.Equal("Launch My App", result.Value[0].Description);
        Assert.Contains(ShortcutLocation.Desktop, result.Value[0].Locations);
    }

    [Fact]
    public void MapShortcutLocation_Desktop()
    {
        Assert.Equal(ShortcutLocation.Desktop, ShortcutTableReader.MapShortcutLocation("DesktopFolder"));
    }

    [Fact]
    public void MapShortcutLocation_StartMenu()
    {
        Assert.Equal(ShortcutLocation.StartMenu, ShortcutTableReader.MapShortcutLocation("StartMenuFolder"));
    }

    [Fact]
    public void MapShortcutLocation_ProgramMenu()
    {
        Assert.Equal(ShortcutLocation.StartMenu, ShortcutTableReader.MapShortcutLocation("ProgramMenuFolder"));
    }

    [Fact]
    public void MapShortcutLocation_UnknownDirectory_ReturnsNull()
    {
        Assert.Null(ShortcutTableReader.MapShortcutLocation("CustomFolder"));
    }
}
