# Demo MAS: MultiAccess Suite Installer

A production-grade, multi-page installer for a complex enterprise application suite (MultiAccess). This demo showcases the full capabilities of the FalkForge.Ui framework: plugin integration, custom window shell, conditional page navigation, SQL Server discovery, ODBC configuration, and a rich confirmation summary.

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

- The `MasPageBase<TView>` abstract class adds shared properties like `Subtitle`, `NextButtonText`, `ShowPrintButton`, and `ShowPreviousButton` that the custom window shell reads to adapt the UI per page.
- `PageResult.GoTo<T>()` enables non-linear wizard flows. The Standard path skips advanced configuration pages, while the Advanced path navigates through additional directory and service configuration pages.
- `PluginServices.GetService<T>()` retrieves plugin-provided services. The SQL plugin provides `ISqlServerDiscovery` for network-based SQL Server detection and `IDatabaseLister` for enumerating databases on a server.
- The `ConfirmParametersPage` dynamically rebuilds its parameter groups based on the selected installation type and all previously collected SharedState values.
- The custom `MasInstallerWindow` includes a cancel confirmation dialog and value converters for null-to-collapsed and bool-to-visibility binding.
