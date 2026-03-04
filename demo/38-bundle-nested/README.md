# Demo 38: Nested Bundle

Nests a child bundle (another bootstrapper executable) inside a parent bundle. This allows a parent installer to orchestrate the installation of an entire child product suite as a single step in its chain.

## What This Demonstrates

- Embedding a child bundle package inside a parent bundle
- Orchestrating multiple bootstrapper executables in a single install flow
- Mixing bundle packages with MSI packages in the same chain

## Key API Calls

| Method | Purpose |
|--------|---------|
| `chain.BundlePackage(path, config)` | Add another bundle (.exe) as a chained package |
| `.Id(string)` | Unique identifier for the nested bundle within the parent chain |
| `.DisplayName(string)` | Name shown in the parent bootstrapper UI |
| `.Vital(true)` | Failure of the child bundle aborts the parent install |

## How to Build

```bash
dotnet build demo/38-bundle-nested/38-bundle-nested.csproj
```

## Notes

- The child bundle (`ChildSetup.exe`) must itself be a valid FalkForge or WiX bootstrapper executable.
- The parent bundle manages the child bundle's install, uninstall, and repair lifecycle automatically.
- Nested bundles share the same elevation context as the parent bundle.
