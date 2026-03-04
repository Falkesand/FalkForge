# Demo 35: Simple Bundle

Creates the simplest possible bundle (bootstrapper): a single MSI package wrapped in a self-extracting executable with
built-in UI. This is the starting point for any bundle-based installer.

## What This Demonstrates

- Minimal `BundleBuilder` setup with name, manufacturer, version, and identifiers
- Assigning a stable `BundleId` and `UpgradeCode` for identity and major-upgrade detection
- Setting per-machine install scope
- Enabling the built-in bootstrapper UI with a custom theme color
- Adding a single MSI package to the install chain
- Detecting related bundles with `RelatedBundle()` for cross-upgrade-code detection
- Declaring this bundle as a dependency provider with `DependencyProvider()`

## Key API Calls

| Method                                    | Purpose                                                                                               |
|-------------------------------------------|-------------------------------------------------------------------------------------------------------|
| `BundleBuilder.Name()`                    | Display name shown in the bootstrapper UI                                                             |
| `.BundleId(Guid)`                         | Unique identifier for this specific bundle build                                                      |
| `.UpgradeCode(Guid)`                      | Stable identifier used to detect and replace previous versions                                        |
| `.Scope(InstallScope.PerMachine)`         | Install for all users on the machine                                                                  |
| `.UseBuiltInUI(themeColor:)`              | Enable the built-in bootstrapper UI with a custom accent color                                        |
| `.Chain(chain => ...)`                    | Define the ordered list of packages to install                                                        |
| `chain.MsiPackage(path, config)`          | Add an MSI package to the chain                                                                       |
| `.Vital(true)`                            | Mark the package as required; failure aborts the entire bundle                                        |
| `.RelatedBundle(guid)`                    | Detect a related bundle by its upgrade code (e.g., a previous version using a different upgrade code) |
| `.DependencyProvider(key, version, name)` | Register this bundle as a dependency provider so other bundles can depend on it                       |
| `BundleCompiler.Compile()`                | Compile the bundle model into an executable                                                           |

## How to Build

```bash
dotnet build demo/35-bundle-simple/35-bundle-simple.csproj
```

## Notes

- The `BundleId` should be regenerated for each release. The `UpgradeCode` must remain stable across versions.
- The `themeColor` parameter accepts a hex color string used as the accent color in the built-in UI.
- The MSI file (`MyApp.msi`) must exist at the expected path when the bundle is compiled.
- `RelatedBundle()` detects bundles installed under a different upgrade code. This is useful when a product was
  previously shipped with a different identity and the new bundle needs to detect and handle the old installation.
- `DependencyProvider()` registers a provider key so that other bundles or packages can declare a dependency on this
  bundle. The bundle will refuse to uninstall while dependents are still installed.
