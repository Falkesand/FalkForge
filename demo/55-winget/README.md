# Demo 55: WinGet Manifest Generation

Shows how to generate a WinGet (Windows Package Manager) manifest set automatically at compile time
alongside the MSI. After building, users can install your app with a single command:

```
winget install Contoso.MyApp
```

Publishing to WinGet requires submitting three YAML files to the
[winget-pkgs](https://github.com/microsoft/winget-pkgs) repository. FalkForge generates all three
at build time and fills in the SHA-256 hash of the compiled MSI automatically.

## What This Demonstrates

- `PackageBuilder.WinGet(...)` — generates the 3-file WinGet manifest set at compile time
- Automatic SHA-256 computation from the compiled MSI (no manual hashing required)
- The winget-pkgs directory structure (`c/Contoso/MyApp/1.0.0/`)
- Required WinGet fields: `PackageIdentifier`, `InstallerUrl`, `License`, `ShortDescription`
- Optional fields: `Moniker`, `Tags`, `ReleaseNotesUrl`

## Key API Calls

```csharp
package.WinGet(w => w
    .PackageIdentifier("Contoso.MyApp")         // Publisher.AppName — winget-pkgs convention
    .InstallerUrl("https://releases.contoso.com/MyApp/1.0.0/MyApp.msi")  // public download URL
    .License("MIT")                             // required by WinGet submission schema
    .ShortDescription("A simple demo application.")
    .Moniker("myapp")                           // optional short alias: winget install myapp
    .Tags("demo", "sample")
    .ReleaseNotesUrl("https://github.com/contoso/myapp/releases/tag/v1.0.0"));
```

## How to Build

```bash
dotnet build demo/55-winget
```

Or run directly and specify an output path:

```bash
dotnet run --project demo/55-winget -- -o output/MyApp.msi
```

## Output Files

Building produces the MSI plus three YAML files in a subdirectory tree that mirrors the
winget-pkgs repository layout:

```
output/
  MyApp.msi
  c/
    Contoso/
      MyApp/
        1.0.0/
          Contoso.MyApp.yaml                  ← version manifest
          Contoso.MyApp.installer.yaml        ← installer manifest (URL + SHA-256 + arch)
          Contoso.MyApp.locale.en-US.yaml     ← locale manifest (name, publisher, description)
```

The SHA-256 hash in `Contoso.MyApp.installer.yaml` is computed from the actual compiled MSI
at build time — you do not need to compute or paste it manually.

## Submitting to WinGet

1. Fork [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs).
2. Copy the generated `c/Contoso/MyApp/1.0.0/` directory into `manifests/` in your fork.
3. Open a pull request. The WinGet validation pipeline checks the manifest automatically.

## CLI Alternative: `forge winget`

Already have a compiled MSI and want to generate the manifest without rebuilding? Use the CLI:

```bash
forge winget MyApp.msi --id Contoso.MyApp --url https://releases.contoso.com/MyApp/1.0.0/MyApp.msi
```

This reads the MSI metadata, computes the SHA-256, and writes the same three YAML files.

## Notes

- `PackageIdentifier` must follow the `Publisher.PackageName` dot-separated convention.
- `InstallerUrl` is the URL where the MSI will be publicly downloadable. It is embedded in the
  manifest verbatim — update it for each release before submitting.
- `Reproducible()` keeps the SHA-256 hash stable across identical builds, which is important when
  the manifest is generated in CI and the MSI is published separately.
- The `Moniker` field lets users install with a short alias: `winget install myapp`.
