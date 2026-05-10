namespace FalkForge.Ui.Tests;

using System.Windows;
using System.Windows.Controls;
using FalkForge.Ui;
using Xunit;

/// <summary>
/// Verifies that InstallerUIBuilder / InstallerWindowBuilder accept a Func&lt;Window&gt;
/// factory delegate instead of a Type, eliminating Activator.CreateInstance and
/// making the API NativeAOT/trimming safe.
/// </summary>
public class InstallerAppFactoryDelegateTests
{
    [Fact]
    public void CustomWindowFactory_CanBeSetViaBuilder()
    {
        var builder = new InstallerUIBuilder();
        var factoryInvoked = false;

        builder.Window(w => w.CustomWindowFactory(() =>
        {
            factoryInvoked = true;
            return new FakeWindow();
        }));

        // The factory should be stored (not yet invoked at builder time).
        Assert.NotNull(builder.WindowConfig.CustomWindowFactory);
        Assert.False(factoryInvoked);
    }

    [Fact]
    public void CustomWindowFactory_WhenInvoked_ReturnsWindowFromDelegate()
    {
        var builder = new InstallerUIBuilder();
        var expected = new FakeWindow();

        builder.Window(w => w.CustomWindowFactory(() => expected));

        var factory = builder.WindowConfig.CustomWindowFactory;
        Assert.NotNull(factory);
        var result = factory();
        Assert.Same(expected, result);
    }

    [Fact]
    public void CustomWindowFactory_EachCallCreatesNewInstance()
    {
        var builder = new InstallerUIBuilder();
        builder.Window(w => w.CustomWindowFactory(() => new FakeWindow()));

        var factory = builder.WindowConfig.CustomWindowFactory!;
        var w1 = factory();
        var w2 = factory();

        Assert.NotSame(w1, w2);
    }

    // Lightweight WPF window stub that avoids requiring STA or real rendering.
    private sealed class FakeWindow : Window { }
}
