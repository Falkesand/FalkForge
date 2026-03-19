# FalkForge

Build Windows installers -- MSI, MSIX, and EXE bundles -- with no external tools. Self-contained compiler, NativeAOT runtime engine, six output formats.

## Three Ways to Build

| Approach | Best For | How |
|----------|----------|-----|
| **C# Fluent API** | Developers who want full control | Define installers as C# programs with IntelliSense and type safety. `dotnet build` compiles them. |
| **JSON Configuration** | Declarative definitions, CI/CD | Write a JSON file, build with `forge build config.json`. No C# required. |
| **FalkForge Studio** | Visual designers, non-developers | WPF desktop IDE. Import from MSI/WiX, export to C# or CI/CD pipelines. |

## Why FalkForge?

- **Self-contained compiler** -- Direct P/Invoke to `msi.dll`. No WiX, no InstallShield, no external tools.
- **Six output formats** -- MSI, MSIX, MSM (merge modules), MSP (patches), MST (transforms), EXE bundles.
- **NativeAOT engine** -- Sub-10ms startup bundle runtime. Three-process architecture with named-pipe IPC.
- **WPF custom UI** -- Page-based installer UI framework with ReactiveUI, DPAPI-secured passwords, and localization.
- **Prerequisite management** -- Built-in package groups for .NET Framework, VC++, ODBC drivers, SQL Server Express.
- **52+ demo projects** -- From hello-world to complex multi-package bundles.

## Quick Start

### Hello World

```csharp
using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

return Installer.Build(args, package =>
{
    package.Name = "Hello World";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.UseDialogSet(MsiDialogSet.Minimal);

    package.Files(files => files
        .Add("payload/hello.txt")
        .To(KnownFolder.ProgramFiles / "Demo" / "HelloWorld"));
}, new MsiCompiler());
```

Build it:

```bash
forge build hello-world.csx
```

## Features

### MSI Packages

Define packages with a fluent builder -- files, features, registry entries, shortcuts, services, environment variables, and more. The compiler emits standards-compliant MSI databases directly.

```csharp
Installer.Build(args, p =>
{
    p.Name = "Acme Client-Server Suite";
    p.Manufacturer = "Acme Corporation";
    p.Version = new Version(3, 5, 0);

    p.UseDialogSet(MsiDialogSet.FeatureTree);

    p.Feature("Client", f =>
    {
        f.Title = "Client Application";
        f.Description = "Desktop client with user interface";
        f.IsDefault = true;
    });

    p.Files(f => f
        .Add("payload/client/client.exe")
        .Add("payload/client/client.core.dll")
        .To(KnownFolder.ProgramFiles / "Acme" / "Client"));

    p.Shortcut("Acme Client", "client.exe")
        .OnDesktop()
        .OnStartMenu("Acme");
}, new MsiCompiler());
```

### Windows Services

Full service lifecycle management -- startup mode, account, dependencies, failure recovery, and conditional installation.

```csharp
package.Service("DemoService", svc =>
{
    svc.DisplayName = "Demo Background Service";
    svc.Executable = @"[ProgramFilesFolder]Demo\ServiceDemo\myservice.exe";
    svc.StartMode = ServiceStartMode.Automatic;
    svc.Account = ServiceAccount.LocalService;
    svc.Arguments = "--config=default --log-level=info";
    svc.Condition("INSTALLSERVICE ~= \"true\"");
    svc.DependsOn("Tcpip");
});
```

### EXE Bundles

Chain multiple packages into a single bootstrapper with rollback boundaries, built-in UI, and automatic update feeds.

```csharp
Installer.BuildBundle(args, outputPath =>
{
    var bundle = new BundleBuilder()
        .Name("Acme Product Suite")
        .Manufacturer("Acme Corporation")
        .Version("2.0.0")
        .Scope(InstallScope.PerMachine)
        .UseBuiltInUI(licenseFile, themeColor: "#2563EB")
        .Chain(chain => chain
            .RollbackBoundary("Prerequisites")
            .MsiPackage(appMsiPath, p => p
                .Id("AcmeApp")
                .DisplayName("Acme Application")
                .Vital(true))
            .RollbackBoundary("Services")
            .MsiPackage(serviceMsiPath, p => p
                .Id("AcmeService")
                .DisplayName("Acme Background Service")
                .Vital(true)))
        .Build();

    return new BundleCompiler().Compile(bundle, outputPath);
});
```

### MSIX Packages

Build modern MSIX packages for Windows 10+ with application declarations, capabilities, and code signing.

```csharp
InstallerMsix.BuildMsix(args, msix =>
{
    msix
        .Name("MyApp")
        .Publisher("CN=Contoso")
        .DisplayName("My Application")
        .Version(new Version(1, 0, 0, 0))
        .Architecture(ProcessorArchitecture.X64)
        .Application("App", "myapp.exe", app => app
            .DisplayName("My Application")
            .Square150x150Logo("assets/Logo.png"))
        .Capability("internetClient")
        .Signing(s => s.CertificatePath("cert.pfx"));
}, (model, outputPath) => new MsixCompiler().Compile(model, outputPath));
```

### Custom Installer UI

Build fully custom WPF installer experiences with the page-based UI framework, ReactiveUI bindings, localization, and DPAPI-secured password handling.

```csharp
return InstallerApp.Run(args, app => app
    .Localization(loc => loc
        .DefaultCulture("en-US")
        .AddJsonResource<WelcomePage>("lang.strings.en-US.json")
        .AddJsonResource<WelcomePage>("lang.strings.sv-SE.json")
        .DetectCulture()
        .AllowLanguageSelection())
    .Window(w => w
        .Size(500, 350)
        .Title("My App Setup")
        .Accent("#2563EB"))
    .Pages(p => p
        .Add<WelcomePage>()
        .Add<ProgressPage>()
        .Add<CompletePage>()));
```

### Extensions

| Extension | Capabilities |
|-----------|-------------|
| **Firewall** | Inbound/outbound TCP/UDP rules |
| **IIS** | AppPool, WebSite, Bindings, Certificates |
| **SQL** | Database creation, script execution |
| **.NET** | Runtime detection via registry + filesystem |
| **Dependency** | Provider/consumer ref-counting (WiX-compatible) |
| **Util** | XmlConfig, UserMgmt, FileShare, QuietExec, InternetShortcut |

### Type-Safe Conditions and Properties

MSI properties and conditions are first-class C# types with operator overloads -- no more string typos.

```csharp
var installDir = MsiProperty.Custom("INSTALLDIR");
var condition = Condition.IsWindows10OrLater & Condition.IsPrivileged;

package.LaunchCondition(condition, "Requires Windows 10+ and admin rights.");
```

### CLI Tool

```
forge build        Build an installer from .csx or .json definition
forge validate     Validate an installer definition
forge inspect      Inspect a compiled MSI (Windows)
forge decompile    Decompile MSI or EXE bundle to C# (Windows)
forge bundle       Detach/reattach bundles for code signing
```

## Architecture

FalkForge uses a three-process model for bundle installation:

```
[UI  WPF + ReactiveUI] <-- Named Pipe A --> [Engine  NativeAOT] <-- Named Pipe B --> [Elevated  NativeAOT]
```

**Phases:** Initializing, Detecting, Planning, Elevating, Applying, Completing, Shutdown

The UI process runs unprivileged. The Engine coordinates detection, planning, and execution. Elevation is requested only when needed, with PID verification and HMAC-SHA256 handshake security.

MSI operations use direct `msi.dll` P/Invoke (`MsiInstallProduct` / `MsiConfigureProduct`) -- never `msiexec.exe`.

## Solution Structure

| Project | Purpose |
|---------|---------|
| **Core** | Domain model, fluent API, validation, `Result<T>` |
| **Compiler.Msi** | MSI/MSM/MSP/MST generation via `msi.dll` P/Invoke |
| **Compiler.Bundle** | Self-extracting EXE bundle compiler |
| **Compiler.Msix** | MSIX package compiler |
| **Engine** | NativeAOT installer runtime (detection, planning, execution) |
| **Engine.Elevation** | NativeAOT elevated companion process |
| **Engine.Protocol** | Binary IPC messages + serialization (AOT-safe) |
| **Platform / Platform.Windows** | OS abstractions, Windows P/Invoke |
| **Ui.Abstractions** | `IInstallerEngine`, `PageResult`, `InstallerState` |
| **Ui** | WPF + ReactiveUI installer UI framework |
| **Extensibility** | Extension system interfaces |
| **Extensions.\*** | Firewall, IIS, SQL, .NET, Dependency, Util |
| **Localization** | JSON-based localization with culture fallback |
| **Decompiler** | MSI and Bundle decompiler to `PackageModel` + C# |
| **Cli** | `forge` command-line tool (Spectre.Console) |
| **Sdk** | MSBuild SDK targets for build integration |

25 source projects, 21 test projects.

## Building from Source

```bash
dotnet build                # 0 warnings required (TreatWarningsAsErrors)
dotnet test                 # ~2,500+ tests
dotnet publish -c Release   # NativeAOT for Engine + Elevation
```

**Requirements:** .NET 10 SDK (10.0.103+), Windows (for MSI compilation and P/Invoke)

## Demos

55 demo projects covering every feature:

| Range | Focus |
|-------|-------|
| 01-05 | MSI basics: hello-world, shortcuts, features, registry, services |
| 06, 10 | EXE bundles: multi-package suites, rollback boundaries |
| 11-14 | Custom UI: simple, VS-style dark theme, glass, lifecycle hooks |
| 15-28 | Focused features: MSIX, signing, services, custom actions, fonts, permissions |
| 29-34 | Extensions: firewall, IIS, SQL, .NET, util, dependency |
| 35-43 | Advanced bundles: exe packages, MSU, nested, remote payloads, update feeds |
| 44-46 | MSM, MSP, MST: merge modules, patches, transforms |
| 47-52 | PowerShell, COM, HTTP, driver install, ICE validation, advanced MSIX |
| json/ | JSON-based definitions (no C# required) |

## Documentation

Full documentation is available in [documentation.html](documentation.html) -- a self-contained 9,000+ line reference with dark/light theme and searchable sidebar covering all 18 sections.

## License

Proprietary. All rights reserved.
