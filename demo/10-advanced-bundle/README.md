# Demo 10: Advanced Bundle

Full-featured bundle demonstrating all four chain package types (ExePackage, MsuPackage, MsiPackage, MspPackage), exit code mapping, rollback boundaries, named containers, related bundles, and install conditions.

## What This Demonstrates

- `BundleBuilder` fluent API: `Name()`, `Version()`, `BundleId()`, `UpgradeCode()`, `Scope()`, `UseBuiltInUI(themeColor:)`
- **ExePackage**: EXE prerequisite with `ExitCode()` mapping (success, reboot-required, already-installed, cancelled)
- **MsuPackage**: Windows Update hotfix with `KbArticle()` and `InstallCondition()`
- **MsiPackage**: Main application with `Property()` injection and `Container()` assignment
- **MspPackage**: Cumulative patch with `PatchCode()`, `TargetProductCode()`, and `InstallCondition()`
- `DefineContainer()` — typed `ContainerRef` for logical payload grouping with download URLs
- `DefineRollbackBoundary()` — typed `RollbackBoundaryRef` for failure isolation between prerequisites and app
- `RelatedBundle()` — upgrade and detect relations to previous bundle versions
- Two-project structure: `msi-package/` builds the MSI; `bundle/` references it and compiles the EXE

## Key API Calls

```csharp
// Typed container and rollback boundary references
var prereqs = bundleBuilder.DefineContainer("Prerequisites", c => c
    .DownloadUrl("https://cdn.example.com/prereqs/"));
var prereqBoundary = bundleBuilder.DefineRollbackBoundary("PrereqBoundary");
var appBoundary = bundleBuilder.DefineRollbackBoundary("AppBoundary");

// Chain with all package types
bundleBuilder.Chain(chain => chain
    .RollbackBoundary(prereqBoundary)

    .ExePackage(prereqExePath, exe => exe
        .Id("VCRedist")
        .Vital(true)
        .InstallCondition("NOT VCRedistInstalled")
        .Container(prereqs)
        .ExitCode(0, ExitCodeBehavior.Success)
        .ExitCode(3010, ExitCodeBehavior.RebootRequired)
        .ExitCode(1638, ExitCodeBehavior.Success))

    .MsuPackage(hotfixMsuPath, msu => msu
        .Id("SecurityHotfix")
        .KbArticle("KB5034441")
        .Vital(false)
        .InstallCondition("VersionNT >= 603 AND NOT KB5034441Installed"))

    .RollbackBoundary(appBoundary, rb => rb.Vital(true))

    .MsiPackage(msiPath, msi => msi
        .Id("NorthwindApp")
        .Vital(true)
        .Container(appContainer)
        .Property("INSTALLFOLDER", "[ProgramFiles64Folder]Northwind Traders\\NorthwindApp"))

    .MspPackage(patchMspPath, msp => msp
        .Id("NorthwindPatch")
        .PatchCode("{E5F6A7B8-...}")
        .TargetProductCode("{D1E2F3A4-...}")
        .Vital(false)
        .InstallCondition("PATCH_AVAILABLE")));

// Related bundles
bundleBuilder.RelatedBundle(upgradeGuid, rb => rb.Relation(RelatedBundleRelation.Upgrade));

// Compile
new BundleCompiler().Compile(bundle, outputPath);
```

## How to Build

Build the MSI package first, then the bundle:

```bash
dotnet run --project demo/10-advanced-bundle/msi-package -- -o ./output
dotnet run --project demo/10-advanced-bundle/bundle -- -o ./output
```

## Notes

- `DefineContainer()` and `DefineRollbackBoundary()` return typed reference objects (`ContainerRef`, `RollbackBoundaryRef`). Pass them to chain items via `Container()` and `RollbackBoundary()` overloads to avoid stringly-typed ID mismatches.
- Rollback boundaries isolate failure domains: if the application package fails, only packages after `appBoundary` roll back; the prerequisites (already installed) are left intact.
- `ExitCode(1638, ExitCodeBehavior.Success)` treats "already installed" exit codes as success so the bundle does not abort when a prerequisite is already present.
- `Property()` on `MsiPackage` injects MSI properties into the package at install time, equivalent to passing `PROPERTY=value` on the `msiexec` command line.
