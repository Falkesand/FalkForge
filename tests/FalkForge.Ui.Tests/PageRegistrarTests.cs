namespace FalkForge.Ui.Tests;

using System.Windows.Controls;
using Xunit;

public class RegistrarTestView : UserControl { }

public class PageA : InstallerPage<RegistrarTestView>
{
    public override string Title => "A";
}

public class PageB : InstallerPage<RegistrarTestView>
{
    public override string Title => "B";
}

public sealed class PageRegistrarTests
{
    [Fact]
    public void Add_SinglePage_FactoriesCountIsOne()
    {
        var registrar = new PageRegistrar();

        registrar.Add<PageA>();

        Assert.Single(registrar.Factories);
    }

    [Fact]
    public void Add_MultiplePages_FactoriesCountMatchesAndOrderPreserved()
    {
        var registrar = new PageRegistrar();

        registrar.Add<PageA>();
        registrar.Add<PageB>();

        Assert.Equal(2, registrar.Factories.Count);
        var first = registrar.Factories[0]();
        var second = registrar.Factories[1]();
        Assert.IsType<PageA>(first);
        Assert.IsType<PageB>(second);
    }

    [Fact]
    public void Factory_CreatesCorrectPageType()
    {
        var registrar = new PageRegistrar();
        registrar.Add<PageA>();

        var page = registrar.Factories[0]();

        Assert.IsType<PageA>(page);
    }

    [Fact]
    public void Factory_EachCallCreatesNewInstance()
    {
        var registrar = new PageRegistrar();
        registrar.Add<PageA>();

        var first = registrar.Factories[0]();
        var second = registrar.Factories[0]();

        Assert.NotSame(first, second);
    }

    [Fact]
    public void EmptyRegistrar_HasZeroFactories()
    {
        var registrar = new PageRegistrar();

        Assert.Empty(registrar.Factories);
    }
}
