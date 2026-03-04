# Demo 37: MSU Package in Bundle

Bundles a Windows Update standalone package (.msu) as a prerequisite before the main MSI application. This is useful when your application requires a specific Windows hotfix or servicing update.

## What This Demonstrates

- Adding a Windows Update (.msu) package to the install chain
- Using MSU packages as prerequisites before application packages
- Mixing MSU and MSI package types in a single bundle

## Key API Calls

| Method | Purpose |
|--------|---------|
| `chain.MsuPackage(path, config)` | Add a Windows Update standalone package to the chain |
| `.Id(string)` | Assign a unique identifier to the package within the bundle |
| `.DisplayName(string)` | Human-readable name shown in progress UI |
| `chain.MsiPackage(path, config)` | Add the main application MSI after the hotfix |

## How to Build

```bash
dotnet build demo/37-bundle-msu-package/37-bundle-msu-package.csproj
```

## Notes

- MSU packages are applied via the Windows Update Standalone Installer (wusa.exe) and require elevation.
- The bundle scope must be `PerMachine` since MSU packages always install system-wide.
- The MSU package is listed before the MSI in the chain to ensure the hotfix is applied first.
