# FalkForge Demos

Practical examples showing how to build Windows Installer packages with FalkForge's fluent C# API and declarative JSON configuration.

## Overview

FalkForge supports two ways to define installers:

1. **C# Fluent API** (demos 01-46) -- Full-featured, programmatic definitions as .NET console apps. Use this for maximum control, conditional logic, and extension integration.
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
- **Extension system** -- Firewall, IIS, SQL, .NET detection, Dependency, and utility actions

## C# Demo Index

### MSI Demos (01-10)

| #  | Name                | Lines | Description                                                | Dialog Set    |
|----|---------------------|-------|------------------------------------------------------------|---------------|
| 01 | Hello World         | ~40   | Minimum installer -- one file, media template, reproducible build | Minimal       |
| 02 | Notepad Clone       | ~72   | App with shortcuts, DWord registry, RemoveRegistry, startup shortcut | InstallDir    |
| 03 | Client-Server       | ~106  | Multi-feature suite with services and conditions           | FeatureTree   |
| 04 | Dev Toolkit         | ~185  | Developer tools with nested features and extensions        | Mondo         |
| 05 | Enterprise Suite    | ~500  | Full enterprise IDE with 15 features and 71 files          | Advanced      |
| 06 | Product Suite       | ~200  | Bundle wrapping multiple MSI packages                      | Built-in WPF  |
| 07 | Extensions Showcase | ~238  | All extensions: Firewall, IIS, SQL, .NET, Util             | InstallDir    |
| 08 | Localization        | ~87   | Multi-language installer with culture fallback              | FeatureTree   |
| 09 | Advanced MSI        | ~302  | Advanced MSI: custom actions, tables, sequences, file ops  | FeatureTree   |
| 10 | Advanced Bundle     | ~163  | Advanced bundle: ExePackage, MsuPackage, MspPackage, rollback | Built-in WPF |

### UI Demos (11-14)

| #  | Name              | Lines | Description                                                |
|----|-------------------|-------|------------------------------------------------------------|
| 11 | Custom UI Simple  | ~616  | Custom WPF installer UI with page navigation and localization |
| 12 | Custom UI VS-Style| ~650  | Visual Studio-style installer with workload selection      |
| 13 | Glass UI          | ~293  | Custom borderless window with acrylic/glass effect         |
| 14 | Lifecycle Hooks   | ~586  | Bundle lifecycle event hooks with custom configuration page |

### Focused MSI Demos (15-28)

| #  | Name                  | Lines | Description                                                      |
|----|-----------------------|-------|------------------------------------------------------------------|
| 15 | Bundle Signing        | ~209  | Detach/sign/reattach workflow with Store, Timestamp, Algorithm   |
| 16 | Features              | ~92   | Feature tree with AllowSameVersionUpgrades, Schedule, MigrateFeatures |
| 17 | Services              | ~119  | Service install with FailureActions, DependsOnGroup, credentials |
| 18 | Environment Variables | ~75   | System and user-scoped environment variables                     |
| 19 | File Associations     | ~67   | Register file extension with verbs and icons                     |
| 20 | Custom Actions        | ~116  | DllFromBinary, ExeFromBinary, Binary, Commit, ContinueOnError   |
| 21 | Launch Conditions     | ~59   | Block install unless conditions are met (admin, OS version)      |
| 22 | INI Files             | ~71   | Write INI file entries during installation                       |
| 23 | Permissions           | ~67   | NTFS permissions via SDDL strings and ForTable                   |
| 24 | Fonts                 | ~60   | Register TrueType fonts with Title override                      |
| 25 | File Operations       | ~76   | MoveFile, DuplicateFile, RemoveFile, CreateFolder, ComponentCondition |
| 26 | Custom Tables         | ~61   | Typed custom MSI tables with row data                            |
| 27 | GAC Assembly          | ~57   | Register assemblies in the Global Assembly Cache                 |
| 28 | Sequence Scheduling   | ~70   | ExecuteSequence and UISequence ordering                          |

### Extension Demos (29-34)

| #  | Name              | Lines | Description                                                      |
|----|-------------------|-------|------------------------------------------------------------------|
| 29 | Ext: Firewall     | ~79   | Windows Firewall rules (inbound TCP)                             |
| 30 | Ext: IIS          | ~84   | IIS application pool and web site                                |
| 31 | Ext: SQL          | ~91   | SQL Server database creation and schema scripts                  |
| 32 | Ext: .NET         | ~75   | .NET runtime detection via factory pattern, launch conditions    |
| 33 | Ext: Util         | ~79   | XmlConfig transformation                                         |
| 34 | Ext: Dependency   | ~80   | Dependency provider/consumer registration                        |

### Bundle Demos (35-43)

| #  | Name                  | Lines | Description                                                      |
|----|-----------------------|-------|------------------------------------------------------------------|
| 35 | Bundle Simple         | ~64   | Basic bundle with RelatedBundle and DependencyProvider            |
| 36 | Bundle EXE Package    | ~67   | EXE prerequisite with exit code mapping                          |
| 37 | Bundle MSU Package    | ~63   | Windows Update (.msu) hotfix prerequisite                        |
| 38 | Bundle Nested         | ~64   | Nested child bundle inside a parent bundle                       |
| 39 | Bundle Remote Payload | ~64   | Download package from URL at install time                        |
| 40 | Bundle Variables      | ~81   | Secret, Hidden, and Persisted bundle variables                   |
| 41 | Bundle Rollback       | ~68   | Rollback boundaries isolating failure domains                    |
| 42 | Bundle Update Feed    | ~62   | Automatic update checking from a feed URL                        |
| 43 | Bundle Layout         | ~68   | Named containers for offline layout scenarios                    |

### Additional Output Types (44-46)

| #  | Name            | Lines | Description                                                      |
|----|-----------------|-------|------------------------------------------------------------------|
| 44 | Merge Module    | ~55   | Reusable .msm component package with Dependency                  |
| 45 | Patch           | ~52   | Delta .msp patch with Classification and AllowRemoval            |
| 46 | Transform       | ~52   | .mst transform for MSI property customization                    |

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

## Feature Matrix -- MSI Demos

Which FalkForge features each MSI-producing demo covers:

| Feature                    | 01 | 02 | 03 | 04 | 05 | 07 | 08 | 09 | 15 | 16 | 17 | 18 | 19 | 20 | 21 | 22 | 23 | 24 | 25 | 26 | 27 | 28 |
|----------------------------|----|----|----|----|----|----|----|----|----|----|----|----|----|----|----|----|----|----|----|----|----|----|
| Files                      | x  | x  | x  | x  | x  | x  | x  | x  | x  | x  | x  | x  | x  | x  | x  | x  | x  | x  | x  | x  | x  | x  |
| Shortcuts                  |    | x  | x  | x  | x  |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |
| Shortcut: OnStartup        |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |
| Shortcut: WithArguments     |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |
| Shortcut: WithWorkingDir    |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |
| Registry                   |    | x  | x  | x  | x  |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |    |
| Registry: DWord            |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |
| Registry: DefaultValue     |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |
| RemoveRegistry             |    | x  |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |    |
| Services                   |    |    | x  |    | x  |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |
| Service: FailureActions    |    |    |    |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |
| Service: DependsOnGroup    |    |    |    |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |
| Service: UserName/Password |    |    |    |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |
| ServiceControl             |    |    |    |    |    |    |    | x  |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |
| ServiceControl: Wait       |    |    |    |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |
| ServiceControl: StopOnInstall |    |    |    |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |
| ServiceControl: Arguments  |    |    |    |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |
| Environment Variables      |    |    | x  | x  | x  |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |
| Env Var: User-scoped       |    |    |    |    |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |
| Features (multi)           |    |    | x  | x  | x  | x  | x  | x  |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |
| Nested Features            |    |    |    | x  | x  |    |    | x  |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |
| Feature Conditions         |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |    |
| Custom Actions             |    |    |    | x  | x  |    |    | x  |    |    |    |    |    | x  |    |    |    |    |    |    |    | x  |
| CA: DllFromBinary          |    |    |    |    |    |    |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |
| CA: ExeFromBinary          |    |    |    |    |    |    |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |
| CA: Binary (embed)         |    |    |    |    |    |    |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |
| CA: Commit                 |    |    |    |    |    |    |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |
| CA: ContinueOnError        |    |    |    |    |    |    |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |
| Custom Tables              |    |    |    |    | x  |    |    | x  |    |    |    |    |    |    |    |    |    |    |    | x  |    |    |
| Execute Sequences          |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |    | x  |
| UISequence                 |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    | x  |
| Media Template             | x  |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |    |
| Reproducible               | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |
| RestartManager             | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |
| File Operations            |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    | x  |    |    |    |
| ComponentCondition         |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    | x  |    |    |    |
| RemoveFile                 |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    | x  |    |    |    |
| Fonts                      |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |    | x  |    |    |    |    |
| Font: Title override       |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    | x  |    |    |    |    |
| File Associations          |    |    |    | x  | x  |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |
| INI Files                  |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |
| Permissions                |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    | x  |    |    |    |    |    |
| Permissions: SDDL          |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    | x  |    |    |    |    |    |
| Permissions: ForTable      |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    | x  |    |    |    |    |    |
| GAC Assembly               |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    | x  |    |
| Major Upgrade              |    | x  | x  | x  | x  | x  | x  | x  | x  | x  |    |    |    |    |    |    |    |    |    |    |    |    |
| MajorUpgrade: AllowSameVer |    |    |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |
| MajorUpgrade: Schedule     |    |    |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |
| MajorUpgrade: MigrateFeatures |    |    |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |
| Launch Conditions          |    |    | x  |    | x  |    |    | x  |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |
| Properties                 |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |    |
| Localization               | x  | x  |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |
| Signing                    |    |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |
| Signing: Store             |    |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |
| Signing: Timestamp         |    |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |
| Signing: Algorithm         |    |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |
| Signing: WithDescription   |    |    |    |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |
| Ext: Firewall              |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |
| Ext: IIS                   |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |
| Ext: SQL                   |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |
| Ext: .NET Detection        |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |
| Ext: Util (XmlConfig)      |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |
| Ext: Util (QuietExec)      |    |    |    |    |    | x  |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |

## Feature Matrix -- Focused Extension Demos

| Feature                     | 29 | 30 | 31 | 32 | 33 | 34 |
|-----------------------------|----|----|----|----|----|----|
| Ext: Firewall               | x  |    |    |    |    |    |
| Ext: IIS                    |    | x  |    |    |    |    |
| Ext: SQL                    |    |    | x  |    |    |    |
| Ext: .NET Detection         |    |    |    | x  |    |    |
| Ext: .NET Factory Pattern   |    |    |    | x  |    |    |
| Launch Conditions (search)  |    |    |    | x  |    |    |
| Ext: Util (XmlConfig)       |    |    |    |    | x  |    |
| Ext: Dependency (Provides)  |    |    |    |    |    | x  |
| Ext: Dependency (Requires)  |    |    |    |    |    | x  |

## Feature Matrix -- Bundle Demos

| Feature                     | 06 | 10 | 15 | 35 | 36 | 37 | 38 | 39 | 40 | 41 | 42 | 43 |
|-----------------------------|----|----|----|----|----|----|----|----|----|----|----|----|
| Bundle                      | x  | x  | x  | x  | x  | x  | x  | x  | x  | x  | x  | x  |
| MsiPackage                  | x  | x  | x  | x  | x  | x  | x  | x  | x  | x  | x  | x  |
| ExePackage                  |    | x  |    |    | x  |    |    |    |    |    |    |    |
| MsuPackage                  |    | x  |    |    |    | x  |    |    |    |    |    |    |
| MspPackage                  |    | x  |    |    |    |    |    |    |    |    |    |    |
| BundlePackage (nested)      |    |    |    |    |    |    | x  |    |    |    |    |    |
| Rollback Boundaries         | x  |    |    |    |    |    |    |    |    | x  |    |    |
| Exit Code Mapping           |    | x  |    |    | x  |    |    |    |    |    |    |    |
| Related Bundles             |    | x  |    | x  |    |    |    |    |    |    |    |    |
| RelatedBundle               |    |    |    | x  |    |    |    |    |    |    |    |    |
| DependencyProvider          |    |    |    | x  |    |    |    |    |    |    |    |    |
| Remote Payload              |    |    |    |    |    |    |    | x  |    |    |    |    |
| Containers                  |    | x  |    |    |    |    |    |    |    |    |    | x  |
| Variables                   |    |    |    |    |    |    |    |    | x  |    |    |    |
| Variable: Secret            |    |    |    |    |    |    |    |    | x  |    |    |    |
| Variable: Hidden            |    |    |    |    |    |    |    |    | x  |    |    |    |
| Variable: Persisted         |    |    |    |    |    |    |    |    | x  |    |    |    |
| InstallCondition            |    |    |    |    |    |    |    |    | x  |    |    |    |
| Update Feed                 |    |    |    |    |    |    |    |    |    |    | x  |    |
| Detach/Sign/Reattach        |    |    | x  |    |    |    |    |    |    |    |    |    |
| Built-in WPF UI             | x  | x  |    | x  | x  | x  | x  | x  | x  | x  | x  | x  |

## Feature Matrix -- Additional Output Types

| Feature                   | 44 (MSM) | 45 (MSP) | 46 (MST) |
|---------------------------|----------|----------|----------|
| Merge Module              | x        |          |          |
| Merge Module: Dependency  | x        |          |          |
| Patch                     |          | x        |          |
| Patch: Classification     |          | x        |          |
| Patch: AllowRemoval       |          | x        |          |
| Transform                 |          |          | x        |
| Transform: SetProperty    |          |          | x        |

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

**Demo 15 (Bundle Signing)** -- Build the MSI package first, then the bundle:

```bash
dotnet run --project demo/15-bundle-signing/msi-package -- -o ./output
dotnet run --project demo/15-bundle-signing/bundle -- -o ./output
```

### Focused demos (16-28)

```bash
dotnet build demo/16-features/
dotnet build demo/17-services/
dotnet build demo/18-environment-variables/
dotnet build demo/19-file-associations/
dotnet build demo/20-custom-actions/
dotnet build demo/21-launch-conditions/
dotnet build demo/22-ini-files/
dotnet build demo/23-permissions/
dotnet build demo/24-fonts/
dotnet build demo/25-file-operations/
dotnet build demo/26-custom-tables/
dotnet build demo/27-gac-assembly/
dotnet build demo/28-sequence-scheduling/
```

### Extension demos (29-34)

```bash
dotnet build demo/29-ext-firewall/
dotnet build demo/30-ext-iis/
dotnet build demo/31-ext-sql/
dotnet build demo/32-ext-dotnet/
dotnet build demo/33-ext-util/
dotnet build demo/34-ext-dependency/
```

### Bundle demos (35-43)

```bash
dotnet build demo/35-bundle-simple/
dotnet build demo/36-bundle-exe-package/
dotnet build demo/37-bundle-msu-package/
dotnet build demo/38-bundle-nested/
dotnet build demo/39-bundle-remote-payload/
dotnet build demo/40-bundle-variables/
dotnet build demo/41-bundle-rollback/
dotnet build demo/42-bundle-update-feed/
dotnet build demo/43-bundle-layout/
```

### Additional output types (44-46)

```bash
dotnet build demo/44-merge-module/
dotnet build demo/45-patch/
dotnet build demo/46-transform/
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

Absolute minimum MSI installer. A single file installed to Program Files with Minimal dialog set. Demonstrates `MediaTemplate` (cabinet naming, compression, embedding), `Reproducible()` for deterministic builds, and `EnableRestartManagerSupport()` for graceful files-in-use handling.

### 02 -- Notepad Clone

Small application installer with InstallDir dialog (user picks install directory). Demonstrates shortcuts (desktop, Start Menu, and Startup with `WithArguments` and `WithWorkingDirectory`), registry entries including `DWord` and `DefaultValue`, `RemoveRegistry` for clean uninstall, a license file, and major upgrade support.

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

### 11 -- Custom UI Simple

Custom WPF installer UI with Welcome, Progress, and Complete pages. Uses localization (en-US, sv-SE) with language selection, custom window sizing, and accent color theming.

### 12 -- Custom UI VS-Style

Visual Studio-inspired installer with borderless window, dark background, workload selection page, and progress tracking. Demonstrates advanced WPF customization for complex installer UIs.

### 13 -- Glass UI

Minimal custom installer with a borderless acrylic/glass window effect using a custom `GlassWindow` subclass. Shows how to replace the default window chrome entirely.

### 14 -- Lifecycle Hooks

Bundle installer with lifecycle event hooks. Custom configuration page collects user input before installation. Demonstrates the page-based navigation model with Welcome, Config, Progress, and Complete pages.

### 15 -- Bundle Signing

Complete code-signing workflow for bundle EXEs:
- Build MSI with `Signing()` configuration: `Store`, `Timestamp`, `Algorithm`, `WithDescription`
- Compile bundle wrapping the signed MSI
- Detach PE stub from bundle data
- Sign the PE stub (placeholder for signtool)
- Reattach signed stub with bundle data

### 16 -- Features

Nested feature tree with required and optional features. Demonstrates `MajorUpgrade` tuning: `AllowSameVersionUpgrades()`, `Schedule(AfterInstallExecute)`, and `MigrateFeatures(true)` to preserve user feature selections across upgrades.

### 17 -- Services

Windows service installation with full configuration:
- Service install with `DependsOn` and `DependsOnGroup` for startup ordering
- `FailureActions`: restart, run command, reboot with configurable delays and messages
- `UserName`/`Password` credentials for domain service accounts
- `ServiceControl`: `Wait`, `StopOnInstall`, `StartOnInstall`, `DeleteOnUninstall`, `Arguments`

### 18 -- Environment Variables

System-level and user-scoped environment variables. Demonstrates `Set` (create new), `Append` (add to PATH with separator), and user-scoped variables (`IsSystem = false`).

### 19 -- File Associations

Register a file extension (`.demo`) with content type, description, icon, and an "open" verb so double-clicking opens the associated application.

### 20 -- Custom Actions

All custom action types in one demo:
- `Binary()` to embed a DLL for use by custom actions
- `DllFromBinary()` -- DLL-based CA with C entry point
- `ExeFromBinary()` -- EXE-based CA from embedded binary
- `SetProperty()` -- Type 51 property-setting CA
- `Deferred()` / `Rollback()` / `Commit()` execution modes
- `ContinueOnError()` -- installer proceeds even if the CA fails

### 21 -- Launch Conditions

Block installation unless conditions are met. Demonstrates `Require(Condition.IsPrivileged)` for admin rights and `Require(Condition.IsWindows10OrLater)` for OS version checks.

### 22 -- INI Files

Write configuration entries to an INI file during installation using section/key/value with `CreateEntry` action.

### 23 -- Permissions

Set NTFS permissions on installed directories:
- Traditional permission mask for `BUILTIN\Users`
- `Sddl` string for fine-grained access control
- `ForTable("CreateFolder")` to target specific MSI tables

### 24 -- Fonts

Register TrueType fonts during installation. Demonstrates basic font registration and `Title` override for custom font display names.

### 25 -- File Operations

File manipulation during installation:
- `CreateFolder` for empty directories
- `DuplicateFile` to copy files at install time
- `RemoveFile` with wildcard patterns on uninstall
- `ComponentCondition` for conditional file installation

### 26 -- Custom Tables

Define custom MSI tables with typed columns (`String`, `Int32`) and primary keys. Insert row data for application-specific metadata.

### 27 -- GAC Assembly

Register a .NET assembly in the Global Assembly Cache with assembly type specification.

### 28 -- Sequence Scheduling

Control installation action ordering:
- `ExecuteSequence` for actions in the server-side execute phase
- `UISequence` for actions in the client-side UI phase
- Conditional execution with `Condition.IsInstalling`

### 29-34 -- Extension Demos

Focused demos for each FalkForge extension, one per project:
- **29 Firewall**: Windows Firewall inbound TCP rule configuration
- **30 IIS**: Application pool + web site with HTTP binding
- **31 SQL**: Database creation + schema script execution
- **32 .NET Detection**: `DotNetExtension` factory pattern (`SearchForRuntime()`) with search result wired as a launch condition via `package.Require()`
- **33 Util**: XmlConfig transformation (XPath-based attribute setting)
- **34 Dependency**: Provider/consumer registration with version constraints

### 35 -- Bundle Simple

Basic bundle with `RelatedBundle()` for detecting bundles with different upgrade codes, and `DependencyProvider()` for declaring this bundle as a dependency that other bundles can reference.

### 36 -- Bundle EXE Package

Bundle an EXE prerequisite (e.g., Visual C++ Redistributable) with `ExitCode()` mapping: success, schedule reboot, and already-installed codes.

### 37 -- Bundle MSU Package

Bundle a Windows Update (.msu) hotfix as a prerequisite before the main MSI application.

### 38 -- Bundle Nested

Nest a child bundle (`BundlePackage`) inside a parent bundle alongside an MSI package.

### 39 -- Bundle Remote Payload

Download an MSI package from a URL at install time using `RemotePayload()` with hash and size instead of embedding it.

### 40 -- Bundle Variables

Bundle variables with visibility controls:
- `Persisted()` -- survives bundle repair/modify sessions
- `Hidden()` -- excluded from install logs
- `Secret()` -- excluded from logs AND persisted state (implies Hidden)
- `InstallCondition` for conditional package installation based on variable values

### 41 -- Bundle Rollback

Rollback boundaries isolating failure domains. If a package in one boundary fails, only packages in that boundary roll back.

### 42 -- Bundle Update Feed

Automatic update checking from a JSON feed URL with `UpdatePolicy.NotifyOnly`.

### 43 -- Bundle Layout

Named containers (`Container()`) for grouping payloads in offline layout scenarios.

### 44 -- Merge Module

Reusable `.msm` component package built with `Installer.BuildMergeModule()`. Includes `Dependency()` declaration for module dependency tracking.

### 45 -- Patch

Delta `.msp` patch built with `Installer.BuildPatch()`. Specifies `Classification(PatchClassification.Hotfix)` and `AllowRemoval(true)` for uninstallable patches.

### 46 -- Transform

`.mst` transform built with `Installer.BuildTransform()`. Overrides MSI properties (`ALLUSERS`, `INSTALLDIR`, `REBOOT`) for enterprise deployment customization.

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
