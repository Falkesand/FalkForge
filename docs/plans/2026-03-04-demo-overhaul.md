# Demo Overhaul — One Feature Per Demo

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace overloaded demos with focused, minimal, single-concept demos so users can learn any feature in isolation.

**Architecture:** Each demo is a standalone console app with one Program.cs (~20-50 lines) showing the bare minimum for one feature. Every demo uses `Installer.Build()` or equivalent entry point and includes only the project references needed for that feature.

**Tech Stack:** .NET 10, FalkForge fluent API, xUnit for demos that need testing.

---

## Strategy

**Keep as-is (already focused):** 01, 02, 06, 08, 11, 12, 13, 14, 15, MAS, json/

**Delete (overloaded):** 03, 04, 05, 07, 09, 10

**Add new focused demos (16+):**

### MSI Core (16-28)

| # | Name | Feature | Key API |
|---|------|---------|---------|
| 16 | features | Nested feature tree + conditions | `Feature()`, `FeatureCondition()` |
| 17 | services | Service install + control + dependencies | `Service()`, `ServiceControl()`, `DependsOn()` |
| 18 | environment-variables | System + user env vars | `EnvironmentVariable()` |
| 19 | file-associations | Extension + verb registration | `FileAssociation()` |
| 20 | custom-actions | Deferred + rollback custom actions | `CustomAction()`, `Binary()` |
| 21 | launch-conditions | Require admin + OS version | `Require()`, `Condition` |
| 22 | ini-files | Write INI entries | `IniFile()` |
| 23 | permissions | Folder ACLs | `Permission()` |
| 24 | fonts | Font registration | `Font()` |
| 25 | file-operations | Move, duplicate, remove, create folder | `MoveFile()`, `DuplicateFile()`, `RemoveFile()`, `CreateFolder()` |
| 26 | custom-tables | Typed custom data table | `CustomTable()` |
| 27 | gac-assembly | GAC registration | `GacAssembly()` |
| 28 | sequence-scheduling | Execute sequence control | `ExecuteSequence()`, `MediaTemplate()` |

### Extensions (29-34)

| # | Name | Feature | Key API |
|---|------|---------|---------|
| 29 | ext-firewall | Firewall rules | `FirewallRuleBuilder` |
| 30 | ext-iis | App pool + website | `AppPoolBuilder`, `WebSiteBuilder` |
| 31 | ext-sql | Database + scripts | `SqlDatabaseBuilder`, `SqlScriptBuilder` |
| 32 | ext-dotnet | .NET runtime detection | `DotNetCoreSearchBuilder` |
| 33 | ext-util | XmlConfig + QuietExec | Util builders |
| 34 | ext-dependency | Provider/consumer | `DependencyProvider()`, `DependencyConsumer()` |

### Bundle (35-43)

| # | Name | Feature | Key API |
|---|------|---------|---------|
| 35 | bundle-simple | Basic MSI bundle | `BundleBuilder`, `Chain`, `MsiPackage` |
| 36 | bundle-exe-package | EXE prerequisite | `ExePackage()`, `ExitCode()` |
| 37 | bundle-msu-package | Windows Update | `MsuPackage()` |
| 38 | bundle-nested | Bundle within bundle | `BundlePackage()` |
| 39 | bundle-remote-payload | Download URLs | `RemotePayload()` |
| 40 | bundle-variables | Variables + conditions | `Variable()`, `InstallCondition()` |
| 41 | bundle-rollback | Rollback boundaries | `RollbackBoundary()` |
| 42 | bundle-update-feed | Auto-update | `UpdateFeed()` |
| 43 | bundle-layout | Offline layout | `Container()`, layout options |

### Output Types (44-46)

| # | Name | Feature | Key API |
|---|------|---------|---------|
| 44 | merge-module | .msm creation | `Installer.BuildMergeModule()` |
| 45 | patch | .msp creation | `Installer.BuildPatch()` |
| 46 | transform | .mst creation | `Installer.BuildTransform()` |

---

## Execution Plan

### Task 1: Delete overloaded demos
Delete directories: 03, 04, 05, 07, 09, 10

### Task 2: Create MSI core demos (16-28)
13 new demo projects, each with Program.cs + .csproj + payload/ as needed

### Task 3: Create extension demos (29-34)
6 new demo projects

### Task 4: Create bundle demos (35-43)
9 new demo projects, some need sub-projects (msi-package/)

### Task 5: Create output type demos (44-46)
3 new demo projects

### Task 6: Verify all demos build
`dotnet build` each demo project

### Task 7: Commit and review
