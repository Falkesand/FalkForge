# Demo 43: Bundle Layout (Containers)

Groups packages into named containers for offline layout scenarios. Containers allow the bootstrapper to organize
payloads into separate cabinet files, enabling partial downloads or offline installation from a network share.

## What This Demonstrates

- Assigning packages to named containers
- Declaring containers on the bundle for payload grouping
- Separating core and optional payloads into distinct downloadable units

## Key API Calls

| Method                             | Purpose                                                |
|------------------------------------|--------------------------------------------------------|
| `.Container(string)`               | Assign a package to a named container within the chain |
| `builder.Container(string)`        | Declare a named container on the bundle itself         |
| Package-level `.Container(string)` | Associate a specific package with a declared container |

## How to Build

```bash
dotnet build demo/43-bundle-layout/43-bundle-layout.csproj
```

## Notes

- Each container produces a separate cabinet file in the bundle layout. This is useful for network deployments where
  only specific components need to be staged.
- Containers must be both declared on the bundle (via `builder.Container()`) and referenced from packages (via
  `p.Container()`).
- In this example, "CoreContainer" holds required components and "ExtrasContainer" holds optional components, allowing
  administrators to distribute them independently.
