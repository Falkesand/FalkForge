# `forge decompile` — Supported Formats

The `forge decompile` command reconstructs C# source from an already-built
installer artifact. It is intended for inspecting third-party packages,
recovering source from lost builds, and authoring migrations.

## Supported inputs

| Extension       | Format                | Platform          | Decompiler class        |
|-----------------|-----------------------|-------------------|-------------------------|
| `.msi`          | Windows Installer     | Windows only (1)  | `MsiDecompiler`         |
| `.exe`          | FALKBUNDLE self-extracting bundle | Cross-platform | `BundleDecompiler`      |
| `.exe`          | WiX Burn bundle       | Windows only (2)  | `WixBundleDecompiler`   |

1. MSI decompilation uses the `msi.dll` P/Invoke surface and is therefore
   Windows-only.
2. WiX Burn decompilation requires `cabinet.dll` for UX cab extraction and
   is therefore Windows-only.

When `.exe` is supplied, the CLI first attempts the cross-platform
FALKBUNDLE path; if that fails and the host is Windows, it falls back to
the WiX Burn decompiler.

## Unsupported inputs

### `.msix` / `.msixbundle`

MSIX is **not supported** and the CLI returns
`ErrorKind.NotSupported` with the message
`"MSIX decompile is not supported; see docs/decompile.md"`.

Rationale:

- MSIX is a ZIP container holding `AppxManifest.xml` plus a VFS payload
  tree. It has no MSI tables, no Burn manifest, and no FALKBUNDLE TOC.
- Projecting an MSIX package onto FalkForge's `PackageModel` or
  `BundleModel` would be lossy: most semantic concepts (capabilities,
  extensions, target device families, package dependencies, virtual
  registry, VFS roots, StartupTasks, etc.) have no equivalent in the
  MSI/Burn models. A decompiled-to-C# artifact would mislead users into
  believing the recompiled output is equivalent.
- A faithful MSIX decompiler would emit a new, MSIX-specific model. That
  work is tracked as a separate RFC and will, when it lands, ship behind
  an explicit `--experimental` flag.

If you need to inspect an MSIX package today, use the Windows 10+ SDK
tooling (`makeappx.exe unpack`, then read `AppxManifest.xml` directly).

### Other extensions

Any other extension produces
`"Unsupported file extension '<ext>'. Expected .msi, .exe, .msix, or .msixbundle."`
and a runtime-error exit code.

## Exit codes

| Code | Condition                                    |
|------|----------------------------------------------|
| 0    | Decompilation succeeded                      |
| 1    | Validation failure inside the decompiler     |
| 2    | Compilation-error failure inside the decompiler |
| 3    | Runtime error (file not found, unsupported format, IO, platform, etc.) |

See `src/FalkForge.Cli/ExitCodes.cs` for the `ErrorKind` → exit code
mapping.
