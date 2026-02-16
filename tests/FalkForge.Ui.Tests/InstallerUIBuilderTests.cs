namespace FalkForge.Ui.Tests;

using Xunit;

public sealed class InstallerUIBuilderTests
{
    [Fact]
    public void Window_ConfiguresWindowSettings()
    {
        var builder = new InstallerUIBuilder();

        builder.Window(w => w.Size(800, 600).Title("Custom"));

        Assert.Equal(800, builder.WindowConfig.Width);
        Assert.Equal(600, builder.WindowConfig.Height);
        Assert.Equal("Custom", builder.WindowConfig.Title);
    }

    [Fact]
    public void Pages_RegistersPages()
    {
        var builder = new InstallerUIBuilder();

        builder.Pages(p => p.Add<PageA>().Add<PageB>());

        Assert.Equal(2, builder.PageFactories.Count);
    }

    [Fact]
    public void Window_And_Pages_Compose()
    {
        var builder = new InstallerUIBuilder();

        builder
            .Window(w => w.Size(1024, 768))
            .Pages(p => p.Add<PageA>());

        Assert.Equal(1024, builder.WindowConfig.Width);
        Assert.Single(builder.PageFactories);
    }

    [Fact]
    public void Default_WindowConfig_HasDefaults()
    {
        var builder = new InstallerUIBuilder();

        Assert.Equal(600, builder.WindowConfig.Width);
        Assert.Equal(400, builder.WindowConfig.Height);
        Assert.False(builder.WindowConfig.IsBorderless);
    }

    [Fact]
    public void Pages_FluentChaining_ReturnsSameBuilder()
    {
        var builder = new InstallerUIBuilder();

        var result = builder.Pages(p => p.Add<PageA>());

        Assert.Same(builder, result);
    }
}
