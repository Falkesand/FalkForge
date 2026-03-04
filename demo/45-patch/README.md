# Demo 45: Patch

Creates a patch (.msp) that upgrades an MSI-based application from one version to another. The patch compiler compares the target (old) and updated (new) MSI files and produces a minimal delta that can be applied to existing installations.

## What This Demonstrates

- Building a patch using `Installer.BuildPatch`
- Specifying target and updated MSI files for delta generation
- Setting patch metadata (manufacturer, description)
- Setting patch classification with `patch.Classification()`
- Allowing patch removal with `patch.AllowRemoval()`
- Compiling with `PatchCompiler`

## Key API Calls

| Method | Purpose |
|--------|---------|
| `Installer.BuildPatch(args, config, compile)` | Entry point for building a patch |
| `patch.Manufacturer(string)` | Set the patch manufacturer |
| `patch.Description(string)` | Human-readable description of what the patch does |
| `patch.TargetMsi(string)` | Path to the original (old version) MSI |
| `patch.UpdatedMsi(string)` | Path to the updated (new version) MSI |
| `patch.Classification(PatchClassification)` | Set the patch classification (e.g., Hotfix, SecurityFix, Update) |
| `patch.AllowRemoval(bool)` | Whether the patch can be uninstalled independently |
| `PatchCompiler.Compile(model, outputPath)` | Compile the model into an .msp file |

## How to Build

```bash
dotnet build demo/45-patch/45-patch.csproj
```

## Notes

- Both the target MSI (`app-v1.msi`) and updated MSI (`app-v2.msi`) must exist in the `payload/` directory at compile time.
- The patch compiler generates a binary delta between the two MSIs, so the .msp file is typically much smaller than a full MSI.
- Patches are applied to existing installations via `msiexec /p patch.msp` or through a bundle that references the patch.
- `Classification` categorizes the patch for Windows Installer and enterprise patch management tools. `PatchClassification.Hotfix` indicates a targeted fix rather than a broad update.
- `AllowRemoval(true)` sets the `AllowRemoval` property so users can uninstall the patch via "Programs and Features" to revert to the previous version. When set to `false` (the default), the patch is permanent.
