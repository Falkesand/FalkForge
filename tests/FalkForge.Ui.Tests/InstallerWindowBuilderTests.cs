namespace FalkForge.Ui.Tests;

using System.Windows;
using System.Windows.Media;
using Xunit;

public class TestWindow : Window { }

public sealed class InstallerWindowBuilderTests
{
    [Fact]
    public void Default_Size_Is600x400()
    {
        var config = new InstallerWindowBuilder().Build();

        Assert.Equal(600, config.Width);
        Assert.Equal(400, config.Height);
    }

    [Fact]
    public void Size_SetsWidthAndHeight()
    {
        var config = new InstallerWindowBuilder()
            .Size(800, 600)
            .Build();

        Assert.Equal(800, config.Width);
        Assert.Equal(600, config.Height);
    }

    [Fact]
    public void Borderless_SetsFlag()
    {
        var config = new InstallerWindowBuilder()
            .Borderless()
            .Build();

        Assert.True(config.IsBorderless);
    }

    [Fact]
    public void CornerRadius_SetsValue()
    {
        var config = new InstallerWindowBuilder()
            .CornerRadius(8.0)
            .Build();

        Assert.Equal(8.0, config.CornerRadius);
    }

    [WpfFact]
    public void Background_ParsesHexColor()
    {
        var config = new InstallerWindowBuilder()
            .Background("#1E1E1E")
            .Build();

        Assert.NotNull(config.BackgroundColor);
        var color = config.BackgroundColor!.Value;
        Assert.Equal(0xFF, color.A);
        Assert.Equal(0x1E, color.R);
        Assert.Equal(0x1E, color.G);
        Assert.Equal(0x1E, color.B);
    }

    [WpfFact]
    public void Accent_ParsesHexColor()
    {
        var config = new InstallerWindowBuilder()
            .Accent("#FF5733")
            .Build();

        Assert.NotNull(config.AccentColor);
        var color = config.AccentColor!.Value;
        Assert.Equal(0xFF, color.A);
        Assert.Equal(0xFF, color.R);
        Assert.Equal(0x57, color.G);
        Assert.Equal(0x33, color.B);
    }

    [Fact]
    public void Title_SetsValue()
    {
        var config = new InstallerWindowBuilder()
            .Title("My Installer")
            .Build();

        Assert.Equal("My Installer", config.Title);
    }

    [Fact]
    public void Icon_SetsPath()
    {
        var config = new InstallerWindowBuilder()
            .Icon("icon.ico")
            .Build();

        Assert.Equal("icon.ico", config.IconPath);
    }

    [Fact]
    public void CustomWindow_SetsType()
    {
        var config = new InstallerWindowBuilder()
            .CustomWindow<TestWindow>()
            .Build();

        Assert.Equal(typeof(TestWindow), config.CustomWindowType);
    }

    [WpfFact]
    public void FluentChaining_AllMethods()
    {
        var builder = new InstallerWindowBuilder();

        var result = builder
            .Size(800, 600)
            .Borderless()
            .CornerRadius(12)
            .Background("#000000")
            .Accent("#FFFFFF")
            .Title("Test")
            .Icon("test.ico")
            .CustomWindow<TestWindow>();

        Assert.Same(builder, result);
    }

    [Fact]
    public void Default_IsBorderless_IsFalse()
    {
        var config = new InstallerWindowBuilder().Build();

        Assert.False(config.IsBorderless);
    }

    [Fact]
    public void Default_CornerRadius_IsZero()
    {
        var config = new InstallerWindowBuilder().Build();

        Assert.Equal(0.0, config.CornerRadius);
    }
}
