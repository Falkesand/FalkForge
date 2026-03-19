# FalkForge

Build Windows installers entirely in C# with a fluent API. Compiles MSI, MSIX, and EXE bundles directly via P/Invoke -- no WiX, no external tools, no XML. NativeAOT bundle engine with WPF custom UI support.

## Install

```bash
dotnet add package FalkForge
```

Or install the CLI:

```bash
dotnet tool install -g FalkForge.Cli
```

## Quick Start

```csharp
using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

return Installer.Build(args, package =>
{
    package.Name = "Hello World";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/hello.txt")
        .To(KnownFolder.ProgramFiles / "Demo" / "HelloWorld"));
}, new MsiCompiler());
```

## Packages

| Package | Description |
|---------|-------------|
| FalkForge.Core | Fluent API, models, validation |
| FalkForge.Compiler.Msi | MSI/MSM/MSP/MST compilation |
| FalkForge.Compiler.Bundle | Self-extracting EXE bundles |
| FalkForge.Compiler.Msix | MSIX packaging |
| FalkForge.Ui | WPF custom installer UI |
| FalkForge.Extensions.Firewall | Windows Firewall rules |
| FalkForge.Extensions.Iis | IIS sites, app pools, bindings |
| FalkForge.Extensions.Sql | SQL Server DB creation and scripts |
| FalkForge.Extensions.DotNet | .NET runtime detection |
| FalkForge.Extensions.Util | XML config, user management, file shares |
| FalkForge.Extensions.Dependency | Ref-counting dependency providers |
| FalkForge.Cli | `forge build`, `validate`, `inspect`, `decompile` |

## Features

- **Pure C#** -- Define installers as regular .NET projects with full IntelliSense
- **MSI compilation** -- Direct P/Invoke to msi.dll, no WiX toolset dependency
- **MSIX support** -- Modern Windows app packages
- **EXE bundles** -- Self-extracting bootstrapper with rollback boundaries and prerequisite chains
- **Custom UI** -- WPF + ReactiveUI page-based installer framework
- **Services** -- Install, configure, permission, and set failure recovery for Windows services
- **File protection** -- `NeverOverwrite()` and `Permanent()` flags for config files
- **Type-safe conditions** -- `Condition.IsWindows10OrLater`, `MsiProperty` comparisons, `&` / `|` / `!` operators
- **ICE validation** -- Internal consistency checks with JSON reports
- **Localization** -- JSON-based with automatic culture fallback chains
- **Decompiler** -- Reverse-engineer existing MSI and WiX Burn bundles to C#
- **52 demos** -- From hello-world to production multi-package suites

## Service Installation

```csharp
package.Service("MyService", svc =>
{
    svc.DisplayName = "My Background Service";
    svc.Executable = "myservice.exe";
    svc.StartMode = ServiceStartMode.Automatic;
    svc.Account = ServiceAccount.LocalService;
    svc.Arguments = "--config=default --log-level=info";
    svc.DependsOn("Tcpip");

    svc.FailureActions(fa =>
    {
        fa.OnFirstFailure = FailureAction.Restart;
        fa.OnSecondFailure = FailureAction.Restart;
        fa.OnSubsequentFailures = FailureAction.None;
        fa.ResetPeriod = TimeSpan.FromDays(1);
    });
});
```

## EXE Bundle

```csharp
return Installer.BuildBundle(args, outputPath =>
{
    var bundle = new BundleBuilder()
        .Name("Product Suite")
        .Manufacturer("Acme Corporation")
        .Version("2.0.0")
        .UpgradeCode(new Guid("F4A5B6C7-D8E9-4F0A-1B2C-3D4E5F6A7B8C"))
        .Scope(InstallScope.PerMachine)
        .UseBuiltInUI(licenseFile, themeColor: "#2563EB")
        .Chain(chain => chain
            .RollbackBoundary("Prerequisites")
            .MsiPackage("App.msi", p => p.Id("App").DisplayName("Application").Vital(true))
            .RollbackBoundary("Services")
            .MsiPackage("Service.msi", p => p.Id("Svc").DisplayName("Service").Vital(true)))
        .Build();

    return new BundleCompiler().Compile(bundle, outputPath);
});
```

## Links

- [Documentation](https://github.com/pekspro/FalkForge)
- [Demos](https://github.com/pekspro/FalkForge/tree/master/demo)
- [CLI Reference](https://github.com/pekspro/FalkForge/tree/master/src/FalkForge.Cli)

## License

See the [LICENSE](https://github.com/pekspro/FalkForge/blob/master/LICENSE) file for details.
