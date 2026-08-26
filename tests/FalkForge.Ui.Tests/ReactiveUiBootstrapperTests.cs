namespace FalkForge.Ui.Tests;

using FalkForge.Ui;
using FalkForge.Ui.Tests.ViewModels;
using FalkForge.Ui.ViewModels;
using ReactiveUI;
using ReactiveUI.Builder;
using Xunit;

/// <summary>
/// ReactiveUI 22.x set itself up on first use. From 23.x an application must run the builder
/// (<c>RxAppBuilder.CreateReactiveUIBuilder()...BuildApp()</c>) first, and any earlier call to
/// <c>WhenAnyValue</c> throws <c>InvalidOperationException("ReactiveUI has not been
/// initialized...")</c>. This repo moved from 22.3.1 to 23.1.8 in commit <c>3084c197</c>
/// (2026-03-20) and nothing ran the builder, so building the built-in wizard threw at startup:
/// <c>DefaultShellViewModel</c> constructs <c>MaintenancePageViewModel</c>, which calls
/// <c>WhenAnyValue</c>.
/// <para>
/// The tests below used to pass anyway, because this test project carried its own
/// <c>[ModuleInitializer]</c> that ran the builder in the test host. The shipping
/// <c>FalkForge.Ui.exe</c> never did. That initializer has been deleted; the production
/// assembly now carries one, so these tests exercise the same code path the installer does.
/// </para>
/// </summary>
public sealed class ReactiveUiBootstrapperTests
{
    [Fact]
    public void TouchingTheUiAssembly_InstallsTheWpfMainThreadScheduler()
    {
        // Calling any FalkForge.Ui member runs that assembly's module initializer first.
        _ = BuiltInUiHost.ResolveArgs(["--manifest", "installer.manifest.json"]);

        // Asserting on the exact WPF scheduler instance, not merely "no exception", pins that the
        // builder ran WithWpf(). WithCoreServices() alone also silences the throw but leaves
        // RxSchedulers.MainThreadScheduler on its DefaultScheduler fallback, which is not the WPF
        // dispatcher.
        Assert.Same(WpfReactiveUIBuilderExtensions.WpfMainThreadScheduler, RxSchedulers.MainThreadScheduler);
    }

    [Fact]
    public void ConstructingTheBuiltInShell_DoesNotThrow()
    {
        // The narrowest reproduction of the startup failure: BuiltInUiHost.BuildWindow builds this
        // view model, which registers MaintenancePageViewModel, which calls WhenAnyValue.
        var shell = new DefaultShellViewModel(new TestInstallerEngine());

        Assert.Contains(shell.Pages, page => page is MaintenancePageViewModel);
    }
}
