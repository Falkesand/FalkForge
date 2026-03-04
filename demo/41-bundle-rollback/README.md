# Demo 41: Rollback Boundaries

Uses rollback boundaries to isolate package failures within the install chain. If a package fails, only packages within the same rollback boundary are rolled back, leaving successfully installed packages from other boundaries intact.

## What This Demonstrates

- Inserting rollback boundaries between groups of packages
- Isolating prerequisite failures from application failures
- Controlling rollback scope to avoid unnecessary uninstallation of unrelated packages

## Key API Calls

| Method | Purpose |
|--------|---------|
| `chain.RollbackBoundary(string)` | Insert a named rollback boundary in the chain |
| `.MsiPackage(path, config)` | Packages after a boundary belong to that boundary's rollback scope |

## How to Build

```bash
dotnet build demo/41-bundle-rollback/41-bundle-rollback.csproj
```

## Notes

- Without rollback boundaries, a failure in any package rolls back all previously installed packages.
- In this example, two boundaries are defined: "Prerequisites" and "Application." If the application MSI fails, only it is rolled back -- the runtime prerequisites remain installed.
- Rollback boundaries are evaluated in chain order. Each boundary starts a new rollback scope that ends at the next boundary or the end of the chain.
