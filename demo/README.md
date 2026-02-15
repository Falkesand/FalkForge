# FalkInstaller Demos

Practical examples showing how to build Windows Installer packages with FalkInstaller's fluent C# API.

## What is FalkInstaller?

FalkInstaller is a C# MSI/Bundle installer framework. Instead of writing XML (like WiX), you define your installer as a regular C# console application using a fluent API. The framework compiles your definition into a standard `.msi` Windows Installer package (or `.exe` bundle, `.msm` merge module, `.msp` patch, or `.mst` transform).

Key properties:
- **Pure C#** -- installers are regular .NET console apps, debuggable and testable
- **Fluent API** -- discoverable builder pattern with IntelliSense support
- **MSI native** -- generates standard Windows Installer databases via `msi.dll` P/Invoke
- **NativeAOT engine** -- 3-5 MB self-extracting bundle runtime with WPF UI
- **Extension system** -- Firewall, IIS, SQL, .NET detection, and utility actions

## How It Works

```
Define (C# fluent API)  -->  Validate (model rules)  -->  Compile (msi.dll P/Invoke)  -->  Output (.msi file)
```

1. **Define**: Write a C# console app that calls `Installer.Build()` with a lambda configuring a `PackageBuilder`.
2. **Validate**: The framework validates the model (package metadata, file references, registry entries, services, etc.) and reports errors with structured error codes (PKG001, FEA001, SVC001, etc.).
3. **Compile**: The `MsiCompiler` generates a valid MSI database by calling Windows Installer APIs via P/Invoke -- creating tables, embedding files into cabinets, writing summary information.
4. **Output**: A standard `.msi` file that can be installed with `msiexec`, deployed via Group Policy, or wrapped in a bundle.

## Architecture Overview

```
                    +------------------+
                    |   Your Program   |  <-- Console app with Installer.Build()
                    +--------+---------+
                             |
                    +--------v---------+
                    |  FalkInstaller    |  <-- Domain model, fluent builders, validation
                    |      .Core       |
                    +--------+---------+
                             |
               +-------------+-------------+
               |                           |
      +--------v---------+       +--------v---------+
      | Compiler.Msi     |       | Compiler.Bundle  |
      | (MSI generation) |       | (EXE bundles)    |
      +------------------+       +------------------+

      +------------------+       +------------------+
      |     Engine       |       |       UI         |
      | (NativeAOT       |       |  (WPF +          |
      |  runtime)        |       |   ReactiveUI)    |
      +------------------+       +------------------+
```

- **Core**: The domain model (`PackageModel`, `FeatureModel`, etc.), fluent builders (`PackageBuilder`, `FileSetBuilder`, etc.), and validation logic. No platform dependencies.
- **Compiler.Msi**: Generates `.msi` files by creating MSI database tables, embedding files into cabinets, and writing summary information streams -- all via `msi.dll` P/Invoke.
- **Compiler.Bundle**: Creates self-extracting `.exe` bundles that chain multiple packages (MSI, MSU, MSP, nested bundles).
- **Engine**: NativeAOT runtime that executes bundle installations -- detection, planning, elevation, download, caching, execution, and rollback.
- **UI**: WPF + ReactiveUI installer user interface with page-based navigation (Welcome, License, InstallDir, Features, Progress, Complete, Maintenance).

## Quick Start

The simplest possible installer:

```csharp
using FalkInstaller;
using FalkInstaller.Builders;
using FalkInstaller.Models;

return Installer.Build(args, package =>
{
    package.Name = "My Application";
    package.Manufacturer = "My Company";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/myapp.exe")
        .To(KnownFolder.ProgramFiles / "MyCompany" / "MyApp"));
});
```

Build it: `dotnet run` (or pass `-o ./output` to control where the `.msi` is written).

## Fluent API Reference

### Package Metadata

```csharp
package.Name = "My Application";           // Product name (required)
package.Manufacturer = "My Company";        // Manufacturer (required)
package.Version = new Version(2, 1, 0);     // Product version (required)
package.Description = "A useful tool";      // Optional description
package.Scope = InstallScope.PerMachine;    // PerMachine (default) or PerUser
package.Architecture = ProcessorArchitecture.X64;  // X64 (default), X86, or Arm64
package.UpgradeCode = Guid.Parse("...");    // Auto-generated if omitted
package.LicenseFile = "payload/license.rtf"; // License shown in UI
package.HelpUrl = "https://example.com";    // Support URL
package.AboutUrl = "https://example.com";   // About URL
```

### Files and Directories

```csharp
// Add individual files
package.Files(files => files
    .Add("payload/app.exe")
    .Add("payload/app.dll")
    .To(KnownFolder.ProgramFiles / "Company" / "App"));

// Add all files from a directory
package.Files(files => files
    .FromDirectory("payload/bin")
    .To(KnownFolder.ProgramFiles / "Company" / "App"));
```

Available `KnownFolder` values: `ProgramFiles`, `ProgramFiles64`, `CommonAppData`, `LocalAppData`, `AppData`, `SystemFolder`, `System64Folder`, `WindowsFolder`, `TempFolder`, `DesktopFolder`, `StartMenuFolder`, `ProgramMenuFolder`, `StartupFolder`, `CommonFilesFolder`, `CommonFiles64Folder`, `FontsFolder`.

The `/` operator builds paths: `KnownFolder.ProgramFiles / "Company" / "Product"` becomes `[ProgramFilesFolder]Company/Product`.

### Features

```csharp
// Explicit features with nested hierarchy
package.Feature("Core", f =>
{
    f.Title = "Core Files";
    f.Description = "Required application files";
    f.IsRequired = true;

    f.Feature("Plugins", sub =>
    {
        sub.Title = "Optional Plugins";
        sub.IsDefault = false;
    });
});

// Conditional features
package.Feature("x64Support", f =>
{
    f.Title = "64-bit Support";
    f.Condition("VersionNT64");
});
```

If no features are defined, an implicit "Complete" feature is created automatically.

### Shortcuts

```csharp
// Desktop shortcut
package.Shortcut("My App", "myapp.exe")
    .WithIcon("payload/app.ico")
    .WithDescription("Launch My App")
    .OnDesktop();

// Start menu shortcut (with subfolder)
package.Shortcut("My App", "myapp.exe")
    .OnStartMenu("My Company");

// Startup shortcut (runs at login)
package.Shortcut("My App", "myapp.exe")
    .OnStartup();
```

Note: Call configuration methods (`WithIcon`, `WithDescription`, `WithArguments`) before the location method (`OnDesktop`, `OnStartMenu`, `OnStartup`), because the location method finalizes the shortcut.

### Registry

```csharp
package.Registry(reg => reg
    .Key(RegistryRoot.LocalMachine, @"Software\MyCompany\MyApp", key =>
    {
        key.Value("Version", "2.1.0");
        key.Value("InstallPath", "[INSTALLDIR]");
        key.DWord("Installed", 1);
        key.DefaultValue("My Application");
    }));
```

Registry roots: `LocalMachine`, `CurrentUser`, `ClassesRoot`, `Users`.
Value types: `String` (default), `ExpandString`, `MultiString`, `DWord`, `QWord`, `Binary`.

### Services

```csharp
package.Service("MyService", svc =>
{
    svc.DisplayName = "My Background Service";
    svc.Executable = "myservice.exe";
    svc.Description = "Processes background tasks";
    svc.StartMode = ServiceStartMode.Automatic;
    svc.Account = ServiceAccount.LocalSystem;
    svc.DependsOn("Tcpip");
});
```

### Environment Variables

```csharp
package.EnvironmentVariable("MY_APP_HOME", "[INSTALLDIR]");
package.EnvironmentVariable("PATH", "[INSTALLDIR]bin", env =>
{
    env.Action = EnvironmentVariableAction.Set;
    env.Separator = ";";
});
```

### Major Upgrade

```csharp
package.MajorUpgrade(upgrade =>
{
    upgrade.DowngradeErrorMessage("A newer version is already installed.");
    upgrade.AllowSameVersionUpgrades();
});
```

### Custom Actions

```csharp
// DLL custom action from embedded binary
package.Binary("CustomDll", "payload/custom.dll");
package.CustomAction("RunSetup", ca =>
{
    ca.DllFromBinary("CustomDll", "Setup");
    ca.After = "InstallFinalize";
    ca.Deferred();
    ca.NoImpersonate();
});

// Set property action
package.CustomAction("SetConfig", ca =>
{
    ca.SetProperty("CONFIG_PATH", "[INSTALLDIR]config.json");
    ca.Before = "InstallInitialize";
});
```

### Dialog Sets

FalkInstaller includes five built-in MSI dialog sets:

```csharp
package.UseDialogSet(MsiDialogSet.Minimal);     // Progress-only, no user interaction
package.UseDialogSet(MsiDialogSet.InstallDir);   // User picks install directory
package.UseDialogSet(MsiDialogSet.FeatureTree);  // User selects features
package.UseDialogSet(MsiDialogSet.Mondo);        // Full wizard (features + directory)
package.UseDialogSet(MsiDialogSet.Advanced);     // All options including per-user/per-machine
```

| Dialog Set    | Welcome | License | InstallDir | Features | Progress | Maintenance |
|---------------|---------|---------|------------|----------|----------|-------------|
| Minimal       | No      | No      | No         | No       | Yes      | No          |
| InstallDir    | Yes     | Yes     | Yes        | No       | Yes      | Yes         |
| FeatureTree   | Yes     | Yes     | No         | Yes      | Yes      | Yes         |
| Mondo         | Yes     | Yes     | Yes        | Yes      | Yes      | Yes         |
| Advanced      | Yes     | Yes     | Yes        | Yes      | Yes      | Yes         |

## Demo Index

| #  | Name           | Description                                       | Dialog Set  | Key Concepts                                  |
|----|----------------|---------------------------------------------------|-------------|-----------------------------------------------|
| 01 | Hello World    | Absolute minimum installer -- one file, no options | Minimal     | `Installer.Build`, `Files`, `KnownFolder`     |
| 02 | Notepad Clone  | Small app with shortcuts, registry, upgrade        | InstallDir  | Shortcuts, Registry, MajorUpgrade, LicenseFile|

## Building Demos

Each demo is a standalone .NET console application. Build with:

```bash
dotnet build demo/01-hello-world/
dotnet build demo/02-notepad-clone/
```

To actually produce an `.msi` file, run the demo on Windows (requires `msi.dll`):

```bash
dotnet run --project demo/01-hello-world/ -- -o ./output
dotnet run --project demo/02-notepad-clone/ -- -o ./output
```

Note: The demos reference `FalkInstaller.Core` and `FalkInstaller.Compiler.Msi` as project references. Building with `dotnet build` always works. Running with `dotnet run` to generate an `.msi` requires Windows with `msi.dll` available.

## Output Types

FalkInstaller supports five output types:

| Type      | Extension | Entry Point                    | Description                                    |
|-----------|-----------|--------------------------------|------------------------------------------------|
| MSI       | `.msi`    | `Installer.Build()`           | Standard Windows Installer package             |
| MSM       | `.msm`    | `Installer.BuildMergeModule()`| Merge module (shared components)               |
| MSP       | `.msp`    | `Installer.BuildPatch()`      | Patch (delta updates)                          |
| MST       | `.mst`    | `Installer.BuildTransform()`  | Transform (MSI customization)                  |
| EXE Bundle| `.exe`    | `Installer.BuildBundle()`     | Self-extracting bundle chaining multiple packages |
