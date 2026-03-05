# Demo Library Improvements Design

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix broken demos, expand existing demos to showcase all builder API methods, standardize extension API patterns, and make the demo library the primary discoverability tool for FalkForge features.

**Architecture:** Extend existing demos rather than creating new ones. Each demo becomes the complete reference for its feature area. One API change: add factory method to DotNetExtension.

## 1. Extension API Consistency

Only DotNet needs a real API change — add `SearchForRuntime()` factory method to `DotNetExtension` class. All other extensions (Firewall, IIS, SQL, Util, Dependency) already use factory/callback patterns and stay as-is.

The `DotNetCoreSearchBuilder` constructor stays public for backwards compatibility, but the extension gets a convenience factory method:

```csharp
// Before (standalone builder, disconnected from extension)
var dotnet = new DotNetExtension(); // unused
var search = new DotNetCoreSearchBuilder()
    .RuntimeType(DotNetRuntimeType.Runtime)
    .MinVersion(new Version(8, 0))
    .Variable("DOTNET8_FOUND")
    .Build();

// After (extension as factory)
var dotnet = new DotNetExtension();
var search = dotnet.SearchForRuntime()
    .RuntimeType(DotNetRuntimeType.Runtime)
    .MinVersion(new Version(8, 0))
    .Variable("DOTNET8_FOUND")
    .Build();
```

## 2. Demo Expansions

### High-Impact

| Demo | Features to Add |
|------|----------------|
| 17-services | FailureActions (restart/reboot/command), DependsOnGroup, UserName/Password, ServiceControl.StopOnInstall, Wait, Arguments |
| 20-custom-actions | DllFromBinary with embedded DLL, ExeFromBinary, Commit(), ContinueOnError() |
| 02-notepad-clone | DWord registry value, DefaultValue, RemoveRegistry, Shortcut.OnStartup, WithArguments, WithWorkingDirectory |
| 32-ext-dotnet | Fix unused variable — use factory pattern, wire DOTNET8_FOUND as launch condition |
| 35-bundle-simple | RelatedBundle, DependencyProvider/DependencyConsumer |
| 40-bundle-variables | Secret(), Hidden(), Persisted() |

### Medium-Impact

| Demo | Features to Add |
|------|----------------|
| 16-features | MajorUpgrade.AllowSameVersionUpgrades, Schedule, MigrateFeatures |
| 01-hello-world | MediaTemplate (cabinet naming, compression, embedding), Reproducible(), EnableRestartManagerSupport() |
| 23-permissions | Sddl string, ForTable |
| 15-bundle-signing | Algorithm, Timestamp, Store, WithDescription on SigningOptions |
| 44-merge-module | Dependency on another merge module |
| 45-patch | Classification, AllowRemoval, TargetProduct, TargetVersion |

### Low-Impact

| Demo | Features to Add |
|------|----------------|
| 18-environment-variables | User-scoped variable (IsSystem = false) |
| 24-fonts | Custom Title override |
| 25-file-operations | ComponentCondition on FileSetBuilder |
| 28-sequence-scheduling | UISequence scheduling |
| 14-lifecycle-hooks | Property IsSecure, IsAdmin, IsHidden |

## 3. README Updates

Every expanded demo gets its README updated:
- New bullet points in "What This Demonstrates"
- New code snippets in "Key API Calls"
- New entries in "Notes" for caveats

Master index (demo/README.md) updated with complete feature matrix.

## 4. Execution Order

Phase 1: DotNet extension factory (API + test)
Phase 2: Expand demos (3 parallel batches: high/medium/low impact)
Phase 3: Update READMEs
Phase 4: Verify (dotnet build demo/FalkForge.Demos.slnx)
