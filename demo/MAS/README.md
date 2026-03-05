# Demo MAS: MultiAccess Suite Installer

A production-grade, multi-page installer for a complex enterprise application suite (MultiAccess). This demo showcases
the full capabilities of the FalkForge.Ui framework: plugin integration, custom window shell, conditional page
navigation, SQL Server discovery, ODBC configuration, and a rich confirmation summary.

## What This Demonstrates

- Plugin system with `Plugin<SqlPlugin>()`, `Plugin<OdbcPlugin>()`, and `Plugin<FileSystemPlugin>()`
- Custom window shell via `CustomWindow<MasInstallerWindow>()` with cancel confirmation dialog
- Abstract page base class (`MasPageBase<TView>`) for shared page properties (subtitle, button text, print button)
- License agreement page with acceptance gating (`CanGoNext` bound to checkbox)
- Installation type selection (Standard vs. Advanced) with conditional page flow via `PageResult.GoTo<T>()`
- SQL Server discovery using `ISqlServerDiscovery` from the SQL plugin
- Database listing using `IDatabaseLister` from the SQL plugin
- Cross-page state passing with `SharedState.Set`/`SharedState.Get`
- Confirmation page that aggregates all collected parameters into grouped tables
- Localization with embedded JSON resources and `AllowLanguageSelection()`
- Conditional back-navigation that varies based on installation type

## Key API Calls

```csharp
InstallerApp.Run(args, app => app
    .Plugin<SqlPlugin>()                              // SQL Server discovery + database listing
    .Plugin<OdbcPlugin>()                             // ODBC DSN management
    .Plugin<FileSystemPlugin>()                       // File system operations
    .Localization(loc => loc
        .DefaultCulture("en-US")
        .AddJsonResource<WelcomePage>("lang.strings.en-US.json")
        .AddJsonResource<WelcomePage>("lang.strings.sv-SE.json")
        .DetectCulture()
        .AllowLanguageSelection())
    .Window(w => w.CustomWindow<MasInstallerWindow>())
    .Pages(p => p
        .Add<WelcomePage>()
        .Add<LicensePage>()
        .Add<InstallationTypePage>()
        .Add<DatabaseServerPage>()
        .Add<ConfirmParametersPage>()
        ...));

// Plugin service access from a page
var discovery = PluginServices.GetService<ISqlServerDiscovery>();
var result = await discovery.DiscoverServersAsync();

var lister = PluginServices.GetService<IDatabaseLister>();
var result = await lister.ListDatabasesAsync(serverName, integratedSecurity);

// Conditional navigation
public override PageResult OnNext()
{
    var installType = SharedState.Get<string>("InstallationType");
    return installType == "Advanced"
        ? PageResult.GoTo<AdvancedInstallDirMultiServerPage>()
        : PageResult.GoTo<ConfirmParametersPage>();
}
```

## How to Build

```
dotnet build demo/MAS/MAS.csproj
```

## Notes

- The `MasPageBase<TView>` abstract class adds shared properties like `Subtitle`, `NextButtonText`, `ShowPrintButton`,
  and `ShowPreviousButton` that the custom window shell reads to adapt the UI per page.
- `PageResult.GoTo<T>()` enables non-linear wizard flows. The Standard path skips advanced configuration pages, while
  the Advanced path navigates through additional directory and service configuration pages.
- `PluginServices.GetService<T>()` retrieves plugin-provided services. The SQL plugin provides `ISqlServerDiscovery` for
  network-based SQL Server detection and `IDatabaseLister` for enumerating databases on a server.
- The `ConfirmParametersPage` dynamically rebuilds its parameter groups based on the selected installation type and all
  previously collected SharedState values.
- The custom `MasInstallerWindow` includes a cancel confirmation dialog and value converters for null-to-collapsed and
  bool-to-visibility binding.

## MSI Package Projects

The `packages/` subdirectory contains five standalone MSI package projects, each producing a real MSI with a dummy
payload executable:

| Package         | Description                       |
|-----------------|-----------------------------------|
| MultiAccess     | Main client application           |
| MultiServer     | Server component                  |
| MultiServerEx   | Extended server component         |
| Konfigurera     | Configuration tool                |
| Concatenate     | Data concatenation utility        |

Each package uses `Installer.Build()` with `MsiCompiler`, sets `MsiDialogSet.None` (silent install, UI handled by the
bundle), and installs to `%ProgramFiles%\ASSA ABLOY\<Name>`. A shared `stub/` project provides the dummy executable
used as the payload for all five MSIs.

```
dotnet run --project demo/MAS/packages/MultiAccess/MultiAccess.csproj -- -o MultiAccess-8.9.0.msi
```

## Bundle Project

The `bundle/MASBundle.csproj` project chains all five MSI packages into a single bundle using `BundleBuilder`:

```csharp
new BundleBuilder()
    .Name("MultiAccess Suite")
    .Manufacturer("ASSA ABLOY")
    .Version("8.9.0")
    .Scope(InstallScope.PerMachine)
    .UseCustomUI("../MAS.csproj")
    .Chain(chain => chain
        .MsiPackage(MsiPath("MultiAccess"), p => p.Id("MultiAccess").DisplayName("MultiAccess").Version("8.9.0").Vital(true))
        .MsiPackage(MsiPath("MultiServer"), ...)
        .MsiPackage(MsiPath("MultiServerEx"), ...)
        .MsiPackage(MsiPath("Konfigurera"), ...)
        .MsiPackage(MsiPath("Concatenate"), ...))
    .Build();
```

The bundle is compiled with `BundleCompiler`, which can optionally use a pre-published NativeAOT engine binary as the
bootstrapper stub via the `FALKFORGE_ENGINE_PATH` environment variable.

## Engine Integration

The UI connects to the FalkForge engine at runtime. When the bootstrapper launches the custom UI with `--manifest` and
`--pipe` arguments, `ResolveEngine()` creates an `EngineClient` and connects via named pipe. In design-time mode
(no arguments), the UI runs standalone for development without an engine connection.

## Self-Extraction Bootstrapper

The compiled bundle produces a single executable with embedded FALKBUNDLE data. At runtime, the engine extracts the
bundled MSI payloads to a temporary directory, loads the manifest, and launches the custom WPF UI. The extraction logic
lives in `FalkForge.Engine.Protocol` for shared use between the engine and compiler.

## Install Progress Page

`InstallProgressPage` subscribes to `Engine.Progress` and `Engine.StatusMessage` observables to show real-time
installation state. The view displays a progress bar (`ProgressPercent`, 0-100), a status label (`StatusText`), and a
detail line (`ProgressDetail`) showing the current package name. The page triggers `PageResult.Install` on entry, which
starts the engine apply sequence.

### Per-MSI Internal Progress

The progress bar moves smoothly during each MSI package installation rather than jumping between packages. The engine
uses the Windows Installer API (`MsiSetExternalUIW`) callback to receive real-time progress ticks from the active MSI.
`InstallProgress.PackagePercent` (0-100) tracks the percentage within the current package, and the overall progress is
calculated as `((CurrentPackage - 1) * 100 + PackagePercent) / TotalPackages`. Updates are throttled to at most one
per 100ms to avoid flooding the named pipe. This works for both direct and elevated (admin) MSI execution.

Developers control the display text per package through localization keys (e.g., `InstallProgress.Package.MultiAccess`).
In `OnPlanBeginAsync`, collected settings are forwarded as MSI properties to each package, guarded by the selected
install action type so that only relevant packages receive their properties.

## Completion Page

`CompletionPage` reads `SharedState.Get<bool>("InstallSuccess")` to display either a success or failure message. On
failure, the error detail from `SharedState.Get<string>("InstallError")` is shown. Back navigation is disabled on this
page.
