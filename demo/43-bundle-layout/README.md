# Demo 43: Bundle Layout (Containers)

Shows how packages can reference named container metadata.

> **Current limitation:** plain named containers remain embedded in the bundle. The engine has no `/layout` command and `LayoutManager` has no production caller, so this demo does not create an offline layout. A container becomes a separate downloadable artifact only when it has `DownloadUrl(...)`.

## What This Demonstrates

- Assigning packages to named containers
- Declaring containers on the bundle for payload grouping
- Distinguishing grouping-only containers from downloadable external containers

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

- Containers must be both declared on the bundle (via `builder.Container()`) and referenced from packages (via `p.Container()`).
- Without `DownloadUrl(...)`, the container names are grouping metadata and both payloads remain embedded.
- To produce separate external container files, declare each container with a download URL. The compiler writes those files next to the bundle, and the engine downloads and verifies them at runtime. Hosting and publishing those files remains the author's responsibility.
