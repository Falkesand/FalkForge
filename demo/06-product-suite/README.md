# Demo 06: Product Suite

A multi-project demo showing how to build a product suite installer: two independent MSI packages (application + background service) wrapped in a single EXE bundle with rollback boundaries. This is the canonical pattern for shipping multiple components as one installer.

## What This Demonstrates

- Multi-project installer architecture: separate MSI projects for each component, one bundle project to combine them
- `Installer.Build` for MSI packages and `Installer.BuildBundle` for the EXE bootstrapper
- Rollback boundaries to isolate failures between packages
- Built-in UI for the bundle with license file and theme color
- Windows Service installation via `p.Service()`
- Environment variable configuration via `p.EnvironmentVariable()`
- Localization with `AddBuiltInCultures()` and `DetectCulture()`

## Project Structure

| Sub-project | Type | Description |
|---|---|---|
| `app-installer/` | MSI | Desktop application with files, shortcuts, and registry entries |
| `service-installer/` | MSI | Windows Service with automatic start and environment variable |
| `suite-bundle/` | Bundle (EXE) | Wraps both MSIs into a single bootstrapper with rollback boundaries |

## How to Build

Build the MSI packages first, then the bundle:

```
dotnet build demo/06-product-suite/app-installer/app-installer.csproj
dotnet build demo/06-product-suite/service-installer/service-installer.csproj
dotnet build demo/06-product-suite/suite-bundle/suite-bundle.csproj
```

## Notes

- The bundle references MSI outputs by relative path. In production, use the FalkForge SDK source generator (`ProjectOutputs.AppInstaller`) for compile-safe references.
- Rollback boundaries ensure that if the service MSI fails, only the service is rolled back -- the application MSI remains installed.
- The app installer uses `MsiDialogSet.InstallDir` (user picks directory), while the service installer uses `MsiDialogSet.Minimal` (no interactive UI) since services typically install to a fixed location.
