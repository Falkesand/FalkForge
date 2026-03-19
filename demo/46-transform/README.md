# Demo 46: Transform

Creates a transform (.mst) that customizes MSI properties at deployment time without modifying the original MSI.
Transforms are commonly used in enterprise environments to enforce organization-specific settings like install
directories and reboot behavior.

## What This Demonstrates

- Building a transform using `Installer.BuildTransform`
- Referencing a base MSI that the transform modifies
- Overriding MSI properties (install scope, install directory, reboot behavior)
- Compiling with `TransformCompiler`

## Key API Calls

| Method                                            | Purpose                                         |
|---------------------------------------------------|-------------------------------------------------|
| `Installer.BuildTransform(args, config, compile)` | Entry point for building a transform            |
| `transform.BaseMsi(string)`                       | Path to the MSI that this transform modifies    |
| `transform.Description(string)`                   | Human-readable description of the customization |
| `transform.SetProperty(name, value)`              | Override an MSI property value                  |
| `TransformCompiler.Compile(model, outputPath)`    | Compile the model into an .mst file             |

## How to Build

```bash
dotnet build demo/46-transform/46-transform.csproj
```

## Notes

- The base MSI (`base.msi`) must exist in the `payload/` directory at compile time.
- `ALLUSERS=1` forces per-machine installation.
- `REBOOT=ReallySuppress` prevents automatic reboots during silent deployment.
- Transforms are applied at install time via `msiexec /i product.msi TRANSFORMS=custom.mst` or through Group Policy
  deployment.
- The original MSI is not modified; the transform is applied as an overlay at install time.
