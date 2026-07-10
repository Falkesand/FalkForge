# FalkForge

Build Windows installers -- MSI, MSIX, and EXE bundles -- with no external tools. Self-contained compiler, NativeAOT runtime engine, six output formats.

## About This Project

FalkForge is a personal project: I built it to make my own installers, and I keep
building it because it's fun. It's shared here because it might be useful to you
too -- you're welcome to use it to build and ship installers for your own products,
free of charge (see [License](#license)). Issues and ideas are welcome; just know
this is maintained at hobby-project pace by one person.

This GitHub repository is the project's home page -- docs, demos, and releases all
live here.

## Three Ways to Build

| Approach | Best For | How |
|----------|----------|-----|
| **C# Fluent API** | Developers who want full control | Define installers as C# programs with IntelliSense and type safety. `dotnet build` compiles them. |
| **JSON Configuration** | Declarative definitions, CI/CD | Write a JSON file, build with `forge build config.json`. No C# required. |
| **FalkForge Studio** | Visual designers, non-developers | WPF desktop IDE. Import from MSI/WiX, export to C# or CI/CD pipelines. |

## Why FalkForge?

- **Self-contained compiler** -- Direct P/Invoke to `msi.dll`. No WiX, no InstallShield, no external tools.
- **Six output formats** -- MSI, MSIX, MSM (merge modules), MSP (patches), MST (transforms), EXE bundles.
- **NativeAOT engine** -- Sub-10ms startup bundle runtime. Three-process architecture with named-pipe IPC.
- **WPF custom UI** -- Page-based installer UI framework with ReactiveUI, DPAPI-secured passwords, and localization.
- **Modern delivery** -- Delta updates (Octodiff), automatic update feeds, WinGet manifest generation.
- **Provable installers** -- Reproducible builds, CycloneDX SBOM, ECDSA payload integrity, `forge verify`/`plan`/`plan-diff`.
- **Migration path** -- `forge migrate` converts an existing MSI, MSM, or WiX Burn EXE into a buildable FalkForge C# project.
- **60+ demo projects** -- From hello-world to complex multi-package bundles.

## Quick Start

```csharp
using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

return Installer.Build(args, package =>
{
    package.Name = "Hello World";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.UseDialogSet(MsiDialogSet.Minimal);

    package.Files(files => files
        .Add("payload/hello.txt")
        .To(KnownFolder.ProgramFiles / "Demo" / "HelloWorld"));
}, new MsiCompiler());
```

Build it:

```bash
forge build hello-world.csx
```

Chaining multiple packages into a self-extracting EXE bundle with rollback
boundaries, built-in UI, and update feeds works the same way -- see
[demo/06-product-suite](demo/06-product-suite) and
[demo/10-advanced-bundle](demo/10-advanced-bundle). Custom WPF installer UI:
[demo/11-custom-ui-simple](demo/11-custom-ui-simple).

## Extensions

| Extension | Capabilities |
|-----------|-------------|
| **Firewall** | Inbound/outbound TCP/UDP rules |
| **IIS** | AppPool, WebSite, Bindings, Certificates |
| **SQL** | Database creation, script execution |
| **.NET** | Runtime detection via registry + filesystem |
| **Dependency** | Provider/consumer ref-counting (WiX-compatible) |
| **Util** | XmlConfig, UserMgmt, FileShare, QuietExec, InternetShortcut |
| **Http** | URL ACL reservations, SNI SSL bindings |
| **Driver** | Device driver installation via PnP |

## CLI Tool

```
forge build        Build an installer from .csx or .json definition
forge validate     Validate an installer definition
forge inspect      Inspect a compiled MSI (Windows)
forge decompile    Decompile MSI or EXE bundle to C# (Windows)
forge migrate      Migrate an existing MSI/EXE to a buildable FalkForge project (Windows)
forge extract      Extract files from an MSI or EXE bundle to disk
forge bundle       Detach/reattach bundles for code signing
forge winget       Generate WinGet manifest from a compiled MSI
forge verify       Verify installer artifact integrity (ECDSA signatures + hashes)
forge plan         Preview the install/uninstall plan without executing
forge plan-diff    Diff install plans between two installer versions
```

## Architecture

FalkForge uses a three-process model for bundle installation:

```
[UI  WPF + ReactiveUI] <-- Named Pipe A --> [Engine  NativeAOT] <-- Named Pipe B --> [Elevated  NativeAOT]
```

The UI process runs unprivileged. The Engine coordinates detection, planning, and
execution. Elevation is requested only when needed, with PID verification and
HMAC-SHA256 handshake security. MSI operations use direct `msi.dll` P/Invoke
(`MsiInstallProduct` / `MsiConfigureProduct`) -- never `msiexec.exe`.

## Building from Source

```bash
dotnet build                # 0 warnings required (TreatWarningsAsErrors)
dotnet test                 # ~7,000+ tests
dotnet publish -c Release   # NativeAOT for Engine + Elevation
```

**Requirements:** .NET 10 SDK (10.0.103+), Windows (for MSI compilation and P/Invoke)

> **NuGet lock files:** The solution uses `RestorePackagesWithLockFile=true` (set in
> `Directory.Build.props`). After adding or changing any package reference, regenerate the
> lock files before committing (`dotnet restore --force-evaluate`) and commit the updated
> `packages.lock.json` files alongside the `.csproj` change.

## Demos and Documentation

- **[demo/](demo/)** -- 60+ demo projects covering every feature, from hello-world
  to delta updates, signing, and reproducible builds.
- **[documentation.html](documentation.html)** -- self-contained full reference
  (18 sections, searchable, dark/light theme).
- **[docs/provenance.md](docs/provenance.md)** -- the supply-chain provenance
  surface: SBOM, payload integrity, reproducible builds, attestations.

## Releases & Provenance

Release artifacts (Engine, Engine.Elevation, CLI) are published on version tag pushes
via the [release workflow](.github/workflows/release.yml), each with a
`SHA256SUMS.txt` checksum file and (where plan support allows)
[GitHub build provenance attestations](https://docs.github.com/en/actions/security-guides/using-artifact-attestations-to-establish-provenance-for-builds):

```bash
gh attestation verify forge.exe --repo Falkesand/FalkForge
```

## License

[PolyForm Perimeter 1.0.0](LICENSE.md) -- in plain words: use FalkForge freely to
build, package, and ship installers for your own or your customers' software,
commercially or not. The only thing you can't do is take FalkForge itself and offer
it (or a derivative) as a competing installer/packaging/setup-authoring product.

Copyright Peter Falkesand.
