# FalkInstaller vs WiX Toolset 6.0.2 — Feature Gap Analysis

**Date:** 2026-02-15  
**Status:** Active  
**Purpose:** Track feature parity between FalkInstaller and WiX Toolset 6.0.2

## Current State

FalkInstaller has 13 source projects across 3 completed phases:
- **Phase 1:** Core domain model, platform abstractions, extensibility, MSBuild SDK, MSI compiler, testing utilities
- **Phase 2:** Cabinet P/Invoke, Environment, Fonts, Close Apps, INI files, Permissions, File Associations, Custom Actions, ICE validation, Code signing
- **Phase 3:** Bundle Engine + UI (Engine, Elevation, Protocol, WPF UI, Bundle Compiler)

**Total:** 22 projects (13 src + 9 test), 522 tests, zero warnings.

---

## 1. MSI Authoring — Core Tables

| Feature | Status | Notes |
|---------|--------|-------|
| Files & Directories | Implemented | Full support |
| Components & Features | Implemented | Full support |
| Registry (Key, Value) | Implemented | Missing `RemoveRegistryKey/Value` |
| Shortcuts | Implemented | Full support |
| Services (Install, Config) | Implemented | Missing `ServiceControl`, `ServiceDependency` |
| Environment Variables | Implemented | Full support |
| INI Files | Implemented | Full support |
| Fonts | Implemented | Full support |
| Permissions/ACLs | Implemented | Full support |
| File Associations | Implemented | Full support |
| Custom Actions | Implemented | Missing `rollback`/`commit` scheduling types |
| Binaries | Implemented | Full support |
| Properties | Implemented | Full support |
| Launch Conditions | Implemented | Full support |
| Upgrades | Implemented | Missing `MajorUpgrade` simplified element |
| Code Signing | Implemented | Full support |
| ICE Validation | Implemented | Full support |
| Cabinet/Compression | Implemented | Full support |
| COM Registration (Class/ProgId/TypeLib) | Deferred | Explicitly deferred to future phase |
| ODBC (DataSource/Driver) | Missing | Niche use case |
| MoveFiles / DuplicateFiles | Missing | |
| RemoveFile / RemoveFolder | Missing | |
| CopyFile | Missing | |
| CreateFolder | Missing | |
| Assembly GAC Registration | Missing | |
| Custom Tables | Missing | Extensibility exists but no CustomTable |
| Conditions on Features/Components | Missing | |
| MediaTemplate | Missing | |
| IsolateComponent | Missing | Niche |
| BindImage | Missing | Niche |
| ReserveCost | Missing | Niche |

## 2. MSI Output Types

| Feature | Status | Notes |
|---------|--------|-------|
| MSI Package (.msi) | Implemented | |
| EXE Bundle (.exe) | Implemented | |
| Merge Module (.msm) | Missing | |
| Patch (.msp) | Missing | |
| Transform (.mst) | Missing | |
| Library (.wixlib) | Missing | N/A for C# API approach |

## 3. MSI UI & Sequences

| Feature | Status | Notes |
|---------|--------|-------|
| MSI-level Dialog Authoring | Missing | FalkInstaller uses bundle-level WPF UI only |
| InstallExecuteSequence | Missing | Implicit only, no custom scheduling |
| InstallUISequence | Missing | |
| AdminExecute/UISequence | Missing | |
| EmbeddedUI | Missing | |
| Pre-built dialog sets (WixUI_*) | Missing | 5 dialog sets in WiX |
| Maintenance mode UI | Missing | |

## 4. Bundle/Burn Engine

| Feature | Status | Notes |
|---------|--------|-------|
| MsiPackage | Implemented | |
| ExePackage | Implemented | |
| .NET Runtime package | Implemented | |
| MspPackage (patches) | Missing | |
| MsuPackage (Windows Update) | Missing | |
| BundlePackage (nested bundles) | Missing | |
| Chain ordering | Implemented | |
| Package conditions (InstallCondition) | Missing | |
| ExitCode mapping | Missing | |
| Variables & condition system | Missing | WiX has 30+ built-in variables |
| Rollback Boundaries | Missing | |
| Related Bundles (upgrade/addon/patch) | Missing | |
| Update feeds | Missing | |
| Containers (payload grouping) | Missing | |
| RemotePayload (download URLs) | Missing | |
| Layout/offline install | Missing | |
| Package cache from URLs | Missing | |
| Embedded bundle mode | Missing | |
| Custom BA SDK (.NET 6+) | Partial | Has WPF UI but no BA SDK for extensibility |
| Themeable standard BA | Missing | |
| Engine-UI bidirectional control | Partial | HandleUiMessageAsync is a no-op |

## 5. Build System & Developer Experience

| Feature | Status | Notes |
|---------|--------|-------|
| MSBuild SDK integration | Implemented | |
| C# fluent API | Implemented | FalkInstaller advantage over WiX |
| Preprocessor | N/A | C# replaces WiX preprocessor |
| Localization | Missing | |
| Harvesting (glob patterns) | Partial | Has wildcard expansion but not full Heat |
| Fragments / Libraries (.wixlib) | N/A | C# project references replace this |
| CLI tooling | Missing | No wix.exe equivalent |
| MSI Decompiler | Missing | |
| Bundle detach/reattach (for signing) | Missing | |
| PDB debug info | Missing | |
| Incremental builds | Missing | |
| Multi-threaded cabinet creation | Missing | |

## 6. Extension Ecosystem

| Extension | Status | Notes |
|-----------|--------|-------|
| Extension framework/interfaces | Implemented | Defined, no built-in extensions |
| Util (CloseApp, Permissions) | Partial | Has CloseApp + Permissions |
| Util (XmlConfig, User/Group, FileShare) | Missing | |
| Util (QuietExec, RemoveFolderEx, InternetShortcut) | Missing | |
| Util (EventManifest, PerfCounter) | Missing | |
| Firewall rules | Missing | |
| IIS (WebSite, AppPool, VirtualDir) | Missing | |
| SQL (Database, Script) | Missing | |
| .NET detection (DotNetCoreSearch, NGen) | Missing | |
| HTTP (URL reservations, SNI SSL) | Missing | |
| Visual Studio (VSIX, detection) | Missing | |
| DirectX (capability detection) | Missing | |
| COM+ (applications, roles) | Missing | |
| Dependency provider/consumer | Missing | |

## 7. Runtime

| Feature | Status | Notes |
|---------|--------|-------|
| Detect/Plan/Apply lifecycle | Implemented | |
| Elevated companion (UAC) | Implemented | |
| Named pipe IPC | Implemented | |
| Package cache (local) | Implemented | |
| Rollback journal | Partial | Journal exists, execution is placeholder |
| Restart Manager | Missing | |
| Verbose structured logging | Missing | |
| Maintenance mode (Modify/Repair) | Partial | Engine supports it; UI flow incomplete |
| Feature state migration (upgrades) | Missing | |
| Instance transforms | Missing | |
| Administrative installs | Missing | |

---

## Summary

| Category | Implemented | Partial | Missing | Coverage |
|----------|:-----------:|:-------:|:-------:|:--------:|
| Core MSI tables | 18 | 0 | 12 | ~60% |
| Output types | 2 | 0 | 4 | 33% |
| MSI UI/Sequences | 0 | 0 | 7 | 0% |
| Bundle engine | 4 | 2 | 14 | ~20% |
| Build system | 2 | 1 | 8 | ~18% |
| Extensions | 1 | 1 | 10 | ~8% |
| Runtime | 4 | 1 | 5 | ~40% |

**Overall estimated coverage: ~30-35% of WiX 6.0.2 functionality.**

## Decisions

- **COM Registration:** Explicitly deferred to a future phase.
- **Preprocessor/Fragments/Libraries:** N/A — C# fluent API replaces WiX XML preprocessor and fragment/library model.
