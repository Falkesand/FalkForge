# FalkForge Demos

Practical examples showing how to build Windows Installer packages with FalkForge's fluent C# API and declarative JSON configuration.

## Overview

FalkForge supports two ways to define installers:

1. **C# Fluent API** (demos 01-10) -- Full-featured, programmatic definitions as .NET console apps. Use this for maximum control, conditional logic, and extension integration.
2. **JSON Configuration** (demos 01-07 in `demo/json/`) -- Declarative JSON files validated and built by the `forge` CLI. Use this for straightforward packages that do not need custom code.

Both approaches produce standard `.msi` Windows Installer packages (or `.exe` bundles). The C# demos cover the full API surface; the JSON demos show the subset available through declarative configuration.

## What is FalkForge?

FalkForge is a C# MSI/Bundle installer framework. Instead of writing XML (like WiX), you define your installer as a regular C# console application using a fluent API. The framework compiles your definition into a standard `.msi` Windows Installer package (or `.exe` bundle, `.msm` merge module, `.msp` patch, or `.mst` transform).

Key properties:
- **Pure C#** -- installers are regular .NET console apps, debuggable and testable
- **Fluent API** -- discoverable builder pattern with IntelliSense support
- **JSON mode** -- declarative configuration for common scenarios, no code required
- **MSI native** -- generates standard Windows Installer databases via `msi.dll` P/Invoke
- **NativeAOT engine** -- 3-5 MB self-extracting bundle runtime with WPF UI
- **Extension system** -- Firewall, IIS, SQL, .NET detection, and utility actions

## C# Demo Index

| #  | Name                | Lines | Description                                                | Dialog Set    |
|----|---------------------|-------|------------------------------------------------------------|---------------|
| 01 | Hello World         | ~15   | Absolute minimum installer -- one file, no options         | Minimal       |
| 02 | Notepad Clone       | ~55   | Small app with shortcuts, registry, upgrade                | InstallDir    |
| 03 | Client-Server       | ~106  | Multi-feature suite with services and conditions           | FeatureTree   |
| 04 | Dev Toolkit         | ~185  | Developer tools with nested features and extensions        | Mondo         |
| 05 | Enterprise Suite    | ~500  | Full enterprise IDE with 15 features and 71 files          | Advanced      |
| 06 | Product Suite       | ~200  | Bundle wrapping multiple MSI packages                      | Built-in WPF  |
| 07 | Extensions Showcase | ~238  | All extensions: Firewall, IIS, SQL, .NET, Util             | InstallDir    |
| 08 | Localization        | ~87   | Multi-language installer with culture fallback              | FeatureTree   |
| 09 | Advanced MSI        | ~302  | Advanced MSI: custom actions, tables, sequences, file ops  | FeatureTree   |
| 10 | Advanced Bundle     | ~163  | Advanced bundle: ExePackage, MsuPackage, MspPackage, rollback | Built-in WPF |

## JSON Demo Index

| #  | File                | Description                                            | Dialog Set  |
|----|---------------------|--------------------------------------------------------|-------------|
| 01 | 01-minimal.json     | Minimal UI, single file                                | Minimal     |
| 02 | 02-installdir.json  | InstallDir UI, desktop shortcut, registry              | InstallDir  |
| 03 | 03-featuretree.json | FeatureTree UI, nested features, services              | FeatureTree |
| 04 | 04-mondo.json       | Mondo UI, environment variables, license, services     | Mondo       |
| 05 | 05-advanced.json    | Advanced UI, nested services, env vars, all features   | Advanced    |
| 06 | 06-web-server.json  | IIS app pool + web site, firewall rules                | InstallDir  |
| 07 | 07-database-app.json| SQL database + scripts, .NET runtime detection         | InstallDir  |

## Feature Matrix -- C# Demos

Which FalkForge features each C# demo covers:

| Feature                 | 01 | 02 | 03 | 04 | 05 | 06 | 07 | 08 | 09 | 10 |
|-------------------------|----|----|----|----|----|----|----|----|----|----|
| Files                   | x  | x  | x  | x  | x  | x  | x  | x  | x  | x  |
| Shortcuts               |    | x  | x  | x  | x  |    |    | x  |    |    |
| Registry                |    | x  | x  | x  | x  |    |    |    | x  | x  |
| Services                |    |    | x  |    | x  | x  |    |    |    |    |
| Service Control         |    |    |    |    |    |    |    |    | x  |    |
| Environment Variables   |    |    | x  | x  | x  |    |    |    |    |    |
| Features (multi)        |    |    | x  | x  | x  |    | x  | x  | x  |    |
| Nested Features         |    |    |    | x  | x  |    |    |    | x  |    |
| Feature Conditions      |    |    |    |    |    |    |    |    | x  |    |
| Custom Actions          |    |    |    | x  | x  |    |    |    | x  |    |
| Custom Tables           |    |    |    |    | x  |    |    |    | x  |    |
| Execute Sequences       |    |    |    |    |    |    |    |    | x  |    |
| Media Template          |    |    |    |    |    |    |    |    | x  |    |
| File Operations         |    |    |    |    |    |    |    |    | x  |    |
| RemoveFile / RemoveReg  |    |    |    |    |    |    |    |    | x  |    |
| Fonts                   |    |    |    |    | x  |    |    |    |    |    |
| File Associations       |    |    |    | x  | x  |    |    |    |    |    |
| Major Upgrade           |    | x  | x  | x  | x  |    | x  | x  | x  | x  |
| Launch Conditions       |    |    | x  |    | x  |    |    |    | x  | x  |
| Properties              |    |    |    |    |    |    |    |    | x  |    |
| Localization            |    |    |    |    |    |    |    | x  |    |    |
| Bundle                  |    |    |    |    |    | x  |    |    |    | x  |
| Rollback Boundaries     |    |    |    |    |    | x  |    |    |    | x  |
| ExePackage              |    |    |    |    |    |    |    |    |    | x  |
| MsuPackage              |    |    |    |    |    |    |    |    |    | x  |
| MspPackage              |    |    |    |    |    |    |    |    |    | x  |
| Related Bundles         |    |    |    |    |    |    |    |    |    | x  |
| Containers              |    |    |    |    |    |    |    |    |    | x  |
| Exit Code Mapping       |    |    |    |    |    |    |    |    |    | x  |
| Ext: Firewall           |    |    |    |    |    |    | x  |    |    |    |
| Ext: IIS                |    |    |    |    |    |    | x  |    |    |    |
| Ext: SQL                |    |    |    |    |    |    | x  |    |    |    |
| Ext: .NET Detection     |    |    |    |    |    |    | x  |    |    |    |
| Ext: Util (XmlConfig)   |    |    |    |    |    |    | x  |    |    |    |
| Ext: Util (QuietExec)   |    |    |    |    |    |    | x  |    |    |    |

## Feature Matrix -- JSON Demos

| Feature                 | 01 | 02 | 03 | 04 | 05 | 06 | 07 |
|-------------------------|----|----|----|----|----|----|-----|
| Files                   | x  | x  | x  | x  | x  | x  | x  |
| Shortcuts               |    | x  |    | x  | x  |    |    |
| Registry                |    | x  |    | x  | x  |    |    |
| Services                |    |    | x  | x  | x  |    |    |
| Environment Variables   |    |    |    | x  | x  |    |    |
| Features (multi)        |    |    | x  | x  | x  |    |    |
| Nested Features         |    |    | x  |    | x  |    |    |
| Major Upgrade           |    | x  | x  | x  | x  | x  | x  |
| Launch Conditions       |    |    | x  | x  | x  |    |    |
| License File            |    |    |    | x  | x  |    |    |
| Ext: Firewall           |    |    |    |    |    | x  |    |
| Ext: IIS                |    |    |    |    |    | x  |    |
| Ext: SQL                |    |    |    |    |    |    | x  |
| Ext: .NET Detection     |    |    |    |    |    |    | x  |

## Building C# Demos

Each C# demo is a standalone .NET console application.

**Build** (compiles the project, works on any OS):

```bash
dotnet build demo/01-hello-world/
dotnet build demo/02-notepad-clone/
dotnet build demo/03-client-server/
dotnet build demo/04-dev-toolkit/
dotnet build demo/05-enterprise-suite/
dotnet build demo/07-extensions-showcase/
dotnet build demo/08-localization/
dotnet build demo/09-advanced-msi/
```

**Run** (produces an `.msi` file, requires Windows with `msi.dll`):

```bash
dotnet run --project demo/01-hello-world/ -- -o ./output
dotnet run --project demo/02-notepad-clone/ -- -o ./output
dotnet run --project demo/03-client-server/ -- -o ./output
dotnet run --project demo/04-dev-toolkit/ -- -o ./output
dotnet run --project demo/05-enterprise-suite/ -- -o ./output
dotnet run --project demo/07-extensions-showcase/ -- -o ./output
dotnet run --project demo/08-localization/ -- -o ./output
dotnet run --project demo/09-advanced-msi/ -- -o ./output
```

### Multi-project demos

**Demo 06 (Product Suite)** -- Build the MSI packages first, then the bundle:

```bash
dotnet run --project demo/06-product-suite/app-installer -- -o ./output
dotnet run --project demo/06-product-suite/service-installer -- -o ./output
dotnet run --project demo/06-product-suite/suite-bundle -- -o ./output
```

**Demo 10 (Advanced Bundle)** -- Build the MSI package first, then the bundle:

```bash
dotnet run --project demo/10-advanced-bundle/msi-package -- -o ./output
dotnet run --project demo/10-advanced-bundle/bundle -- -o ./output
```

## Validating JSON Demos

JSON demos are validated and built by the `forge` CLI tool. Use the `validate` command to check a JSON definition without producing an MSI:

```bash
forge validate demo/json/01-minimal.json
forge validate demo/json/02-installdir.json
forge validate demo/json/03-featuretree.json
forge validate demo/json/04-mondo.json
forge validate demo/json/05-advanced.json
forge validate demo/json/06-web-server.json
forge validate demo/json/07-database-app.json
```

To build an MSI from a JSON definition (requires Windows with `msi.dll`):

```bash
forge build demo/json/01-minimal.json -o ./output
```

## Payload Files

All `payload/` directories contain **dummy placeholder files** (zero-byte or minimal content). They exist so that the demos compile and the file references resolve. Replace them with real application binaries when adapting a demo for production use.

## Demo Details

### 01 -- Hello World

Absolute minimum MSI installer. A single file installed to Program Files with no UI interaction (Minimal dialog set). Start here to understand the basic `Installer.Build()` pattern.

### 02 -- Notepad Clone

Small application installer with InstallDir dialog (user picks install directory). Demonstrates shortcuts (desktop and Start Menu), registry entries under HKCU, a license file, and major upgrade support.

### 03 -- Client-Server

Multi-component application with FeatureTree dialog. Installs a client application and a Windows service as separate selectable features. Covers services, environment variables, launch conditions, and feature-level conditions.

### 04 -- Dev Toolkit

Developer tools suite with Mondo dialog (features + install directory). Shows deeply nested feature hierarchies, file associations, custom actions, and conditional feature installation.

### 05 -- Enterprise Suite

Full enterprise IDE with Advanced dialog set. The largest MSI demo with 15 features, 71 files, custom tables, fonts, properties, and launch conditions. Demonstrates the full breadth of the MSI feature set.

### 06 -- Product Suite (Bundle)

Multi-project bundle that wraps two MSI packages (application + service) into a single self-extracting EXE. Uses `Installer.BuildBundle()` with rollback boundaries to isolate failure domains. The bundle uses the built-in WPF UI.

### 07 -- Extensions Showcase

Demonstrates all five FalkForge extension APIs in a single project:
- **Firewall**: Inbound/outbound TCP rules (ports 8080, 1433)
- **IIS**: Application pool + web site with HTTP binding
- **SQL**: Database creation + schema script execution
- **.NET Detection**: Runtime detection (.NET 8.0+ x64)
- **Util**: XmlConfig transformation + QuietExec post-install command

### 08 -- Localization

Multi-language installer using JSON localization files with culture fallback. Loads three language files (en-US, de, fr) plus an inline Austrian German (de-AT) culture. All user-visible strings use `!(loc.StringId)` references resolved through the fallback chain: de-AT -> de -> en-US.

### 09 -- Advanced MSI

Comprehensive showcase of advanced MSI features:
- Complex feature tree with conditions (disable on Server Core)
- File operations: MoveFile, DuplicateFile, RemoveFile, CreateFolder
- Service control (start/stop existing services, not service install)
- Custom actions: SetProperty, deferred DLL from binary, rollback actions
- Custom tables with typed columns and row data (deployment metadata, health checks)
- Execute sequence configuration
- Media template control (cabinet naming, compression, size limits)
- RemoveRegistry for clean uninstall
- Launch conditions and secure properties

### 10 -- Advanced Bundle

Full-featured bundle demonstrating all chain package types:
- **ExePackage**: Visual C++ Redistributable with exit code mapping
- **MsuPackage**: Windows security hotfix with KB article reference
- **MsiPackage**: Main application with MSI properties
- **MspPackage**: Cumulative patch with conditional application
- Rollback boundaries isolating prerequisites from the application
- Related bundle detection (upgrade and detect relations)
- Named containers for logical payload grouping with download URLs
- Built-in WPF UI with theme color customization

### JSON 01-05 -- Core MSI Features

Progressive complexity from a single-file Minimal installer through to an Advanced dialog set with nested service features, environment variables, and multiple registry entries. These mirror the C# demos 01-05 in declarative form.

### JSON 06 -- Web Server

IIS + Firewall extension demo. Configures an IIS application pool and web site with HTTP/HTTPS bindings, plus inbound firewall rules for ports 80 and 443.

### JSON 07 -- Database App

SQL + .NET extension demo. Creates a SQL Server database with schema and seed scripts, and detects .NET 8.0+ runtime availability.

## Architecture Overview

```
                    +------------------+
                    |   Your Program   |  <-- Console app with Installer.Build()
                    +--------+---------+
                             |
                    +--------v---------+
                    |    FalkForge      |  <-- Domain model, fluent builders, validation
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

## Output Types

FalkForge supports five output types:

| Type       | Extension | Entry Point                     | Description                                       |
|------------|-----------|-------------------------------- |---------------------------------------------------|
| MSI        | `.msi`    | `Installer.Build()`            | Standard Windows Installer package                |
| MSM        | `.msm`    | `Installer.BuildMergeModule()` | Merge module (shared components)                  |
| MSP        | `.msp`    | `Installer.BuildPatch()`       | Patch (delta updates)                             |
| MST        | `.mst`    | `Installer.BuildTransform()`   | Transform (MSI customization)                     |
| EXE Bundle | `.exe`    | `Installer.BuildBundle()`      | Self-extracting bundle chaining multiple packages |
