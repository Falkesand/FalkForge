namespace FalkForge.Ui.Tests;

using System.Windows.Controls;
using FalkForge.Ui.Abstractions;
using Xunit;

public class AppTestView : UserControl { }

public class TestPageForApp : InstallerPage<AppTestView>
{
    public override string Title => "Test";
}

public sealed class InstallerAppTests
{
    [Fact]
    public void Builder_ComposesWindowAndPages()
    {
        var builder = new InstallerUIBuilder();

        builder.Window(w => w.Size(800, 600).Title("Test"))
               .Pages(p => p.Add<TestPageForApp>());

        Assert.Equal(800, builder.WindowConfig.Width);
        Assert.Equal(600, builder.WindowConfig.Height);
        Assert.Equal("Test", builder.WindowConfig.Title);
        Assert.Single(builder.PageFactories);
    }

    [Fact]
    public void PageFactories_CreateDistinctInstances()
    {
        var builder = new InstallerUIBuilder();
        builder.Pages(p => p.Add<TestPageForApp>());

        var page1 = builder.PageFactories[0]();
        var page2 = builder.PageFactories[0]();

        Assert.NotSame(page1, page2);
    }

    [Fact]
    public void Pages_CanBeWiredWithSharedState()
    {
        var builder = new InstallerUIBuilder();
        builder.Pages(p => p.Add<TestPageForApp>());

        var page = builder.PageFactories[0]();
        var state = new InstallerState();
        page.SharedState = state;

        Assert.Same(state, page.SharedState);
    }

    [Fact]
    public void Builder_DefaultWindowConfig_HasExpectedDefaults()
    {
        var builder = new InstallerUIBuilder();

        Assert.Equal(600, builder.WindowConfig.Width);
        Assert.Equal(400, builder.WindowConfig.Height);
        Assert.False(builder.WindowConfig.IsBorderless);
    }
}
