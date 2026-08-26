using System.Runtime.CompilerServices;
using ReactiveUI.Builder;

namespace FalkForge.Ui;

/// <summary>
/// Runs ReactiveUI's builder once, before anything in this assembly is used.
/// <para>
/// ReactiveUI up to 22.x set itself up on first use. From 23.x an application must run the
/// builder first, and any earlier call to <c>WhenAnyValue</c> throws
/// <c>InvalidOperationException("ReactiveUI has not been initialized...")</c>. This repo moved
/// from 22.3.1 to 23.1.8 in commit <c>3084c197</c> (2026-03-20) without adding the builder call,
/// so every attempt to build the built-in wizard threw: <c>BuiltInUiHost.BuildWindow</c> creates
/// <c>DefaultShellViewModel</c>, which creates <c>MaintenancePageViewModel</c>, which calls
/// <c>WhenAnyValue</c>.
/// </para>
/// <para>
/// This is a module initializer rather than a call at the top of <c>App.OnStartup</c> because the
/// assembly has several entry points that all reach ReactiveUI-backed view models: the built-in
/// host (<c>App</c> plus <see cref="BuiltInUiHost"/>), its manifest-only design/preview mode when
/// the engine passes no pipe, and the UI-first custom path (<see cref="InstallerApp.Run"/>, which
/// builds view models before it ever constructs an <c>Application</c>). The runtime guarantees a
/// module initializer runs before the first access to any member of the module, so every one of
/// those paths is covered without a call site to keep in sync.
/// </para>
/// <para>
/// <c>WithWpf()</c> rather than <c>WithCoreServices()</c>: it registers the WPF converters and
/// sets the main-thread scheduler to ReactiveUI's <c>WaitForDispatcherScheduler</c>, which
/// resolves <c>DispatcherScheduler.Current</c> lazily on first use. That is what makes it safe to
/// run here, before any WPF <c>Application</c> or dispatcher exists.
/// </para>
/// </summary>
internal static class ReactiveUiBootstrapper
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithWpf()
            .BuildApp();
    }
}
