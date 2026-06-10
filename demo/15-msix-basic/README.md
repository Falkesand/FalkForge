# Demo 15: MSIX Basic

> **Experimental:** The FalkForge MSIX compiler (`Compiler.Msix`) is experimental. Studio is wired to it, but CLI dispatch (`forge build --format msix`) is not yet implemented. This demo builds via the API only.

Minimal MSIX package using the FalkForge MSIX compiler.

## Prerequisites

- Windows 10 1809+ (MSIX packaging APIs)
- Code signing certificate (PFX)

## Build

```bash
dotnet script demo/15-msix-basic/msix-basic.csx -- -o ./output
```

## What This Shows

- `MsixBuilder` fluent API
- Single application entry point
- Capability declaration
- Signing configuration
