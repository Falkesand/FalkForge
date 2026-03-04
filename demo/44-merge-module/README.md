# Demo 44: Merge Module

Creates a merge module (.msm) -- a reusable package of components that can be merged into multiple MSI installers. Merge modules are the Windows Installer mechanism for sharing files, registry entries, and other resources across products.

## What This Demonstrates

- Building a merge module using `Installer.BuildMergeModule`
- Setting module metadata (version, manufacturer, language, description)
- Adding a component to the merge module
- Declaring a module dependency with `module.Dependency()`
- Compiling with `MsmCompiler`

## Key API Calls

| Method | Purpose |
|--------|---------|
| `Installer.BuildMergeModule(args, config, compile)` | Entry point for building a merge module |
| `module.Version(Version)` | Set the merge module version |
| `module.Manufacturer(string)` | Set the manufacturer name |
| `module.Language(int)` | Set the language code (1033 = English US) |
| `module.Description(string)` | Set a human-readable description |
| `module.Component(string)` | Add a named component to the module |
| `module.Dependency(string)` | Declare a dependency on another merge module |
| `MsmCompiler.Compile(model, outputPath)` | Compile the model into an .msm file |

## How to Build

```bash
dotnet build demo/44-merge-module/44-merge-module.csproj
```

## Notes

- A merge module must contain at least one component.
- Merge modules use the `FalkForge.Builders` and `FalkForge.Compiler.Msi` namespaces instead of the bundle namespaces.
- The output is an .msm file, not an .msi or .exe. It is consumed by other MSI projects via `MergeModule()` references.
- Language code 1033 corresponds to English (United States).
- `module.Dependency()` records a dependency in the `ModuleDependency` table. When this module is merged into an MSI, the merge tool verifies that the required dependency module is also included.
