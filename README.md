# FalkForge

Build Windows installers -- MSI, MSIX (experimental), and EXE bundles -- with no external tools. Self-contained compiler, NativeAOT runtime engine, six output formats.

## About This Project

FalkForge is a personal project: I built it to make my own installers, and I keep
building it because it's fun. It's shared here because it might be useful to you
too -- you're welcome to use it to build and ship installers for your own products,
free of charge (see [License](#license)). Issues and ideas are welcome; just know
this is maintained at hobby-project pace by one person.

In the interest of transparency: FalkForge is built with substantial help from
Anthropic's AI models (Claude). The design direction and decisions are mine, but a
large share of the implementation, tests, and documentation is AI-assisted. Every
change goes through the full automated test suite (zero-warning builds, ~7,000+
tests) and review gates before it lands.

This GitHub repository is the project's home page -- docs, demos, and releases all
live here.

## Three Ways to Build

| Approach | Best For | How |
|----------|----------|-----|
| **C# Fluent API** | Developers who want full control | Define installers as C# programs with IntelliSense and type safety. `dotnet build` compiles them. |
| **JSON Configuration** | Simple, no-compiler installers | Write a JSON file, build with `forge build config.json`. No C# required. *Work in progress — experimental subset; Firewall/IIS/SQL/.NET extensions validate but are not yet emitted into the installer, see [documentation.html](documentation.html#cli-json-config).* |
| **FalkForge Studio** | Visual designers, non-developers | WPF desktop IDE. Import from MSI/WiX, export to C# or CI/CD pipelines. *Work in progress — expect visual and functional rough edges during the beta.* |

## Why FalkForge?

- **Self-contained compiler** -- Direct P/Invoke to `msi.dll`. No WiX, no InstallShield, no external tools.
- **Six output formats** -- MSI, MSIX (experimental), MSM (merge modules), MSP (patches), MST (transforms), EXE bundles.
- **NativeAOT engine** -- Sub-10ms startup bundle runtime. Three-process architecture with named-pipe IPC.
- **WPF custom UI** -- Page-based installer UI framework with ReactiveUI, DPAPI-secured passwords, and localization.
- **Modern delivery** -- Delta updates (Octodiff), automatic update feeds, WinGet manifest generation.
- **Provable installers** -- Reproducible builds, CycloneDX SBOM, ECDSA payload integrity, `forge verify`/`plan`/`plan-diff`.
- **Migration path** -- `forge migrate` converts an existing MSI, MSM, or WiX Burn EXE into a buildable FalkForge C# project.
- **60+ demo projects** -- From hello-world to complex multi-package bundles.

## Security

Installer integrity is where FalkForge goes further than the mainstream installer tools:

- **Real payload integrity, not just a signed launcher** -- every payload is hashed into a signed manifest and verified before anything installs, so a swapped payload is caught even when the Authenticode signature on the outer `.exe` still looks valid.
- **Post-quantum ready** -- optional hybrid signing adds an ML-DSA-65 (FIPS 204) signature alongside classical ECDSA-P256.
- **A real trusted-key model** -- pin trusted keys in the engine, assign key roles with M-of-N quorum for sensitive operations, rotate and revoke keys safely.
- **Secure updates** -- require-signed update feeds with key revocation and version epochs, so an update can't be rolled back to a revoked build.
- **Supply-chain transparency** -- reproducible builds, CycloneDX SBOM, and a provable pipeline (`forge plan` / `forge verify --rebuild`) to show what a bundle does and that it matches its source.
- **Hardened install engine** -- the elevated helper is mutually authenticated (HMAC handshake, parent PID verification) and executes only a whitelisted command set.

Depth and how-tos: [documentation.html -- Bundle Signing, Trust & Key Rotation](https://falkesand.github.io/FalkForge/documentation.html#bundle-signing-trust) (section 23, includes the plain-language security manual).

## Get Started in a Minute

**0.5.0-beta.4.** The core compiler and engine are exercised
by ~8,000+ tests and used on real installs, but APIs can still shift before 1.0
and a few features are intentionally incomplete -- see the
[release notes](https://github.com/Falkesand/FalkForge/blob/main/docs/release-notes/v0.5.0-beta.4.md)
for what's stable and what's flagged.

Install the tool **or** add the one meta-package — never the 28 granular packages:

```bash
# Option 1 — the forge CLI scaffolds and builds installers
dotnet tool install -g FalkForge.Tool --prerelease   # --prerelease needed while in beta
forge init --name "My App"        # starter project (add --type bundle for an EXE bundle)
dotnet run                        # -> My_App-1.0.0.msi

# Option 2 — dotnet new templates
dotnet new install FalkForge.Templates
dotnet new falkforge-msi -n MyInstaller --ProductName "My App"
cd MyInstaller && dotnet run

# Option 3 — an existing project: ONE package brings everything
dotnet add package FalkForge --prerelease   # --prerelease needed while in beta
```

The `FalkForge` meta-package transitively delivers the fluent API, the MSI and
EXE-bundle compilers, localization, every extension, and the NativeAOT bundle
engine runtime — so `dotnet run` on a scaffolded project produces a runnable
installer with nothing else installed. The granular `FalkForge.*` packages
remain available when you want a minimal footprint.

### Building from source

```bash
git clone https://github.com/Falkesand/FalkForge.git
cd FalkForge
dotnet build

# Scaffold a starter project with the CLI, run from source:
dotnet run --project src/FalkForge.Cli -- init --name "My App"
cd My_App && dotnet run    # -> My_App-1.0.0.msi
```

`forge init` (see [CLI Tool](#cli-tool) below) writes a project that references the
`FalkForge` meta-package by version. The
[tutorials](https://falkesand.github.io/FalkForge/docs/tutorials/index.html) teach the
same concepts against the checked-out demo repo; [Getting
Started](https://falkesand.github.io/FalkForge/docs/tutorials/getting-started.html) notes
the `forge init` equivalent for anyone following along without a clone.

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
[demo/06-product-suite](https://github.com/Falkesand/FalkForge/tree/main/demo/06-product-suite) and
[demo/10-advanced-bundle](https://github.com/Falkesand/FalkForge/tree/main/demo/10-advanced-bundle). Custom WPF installer UI:
[demo/11-custom-ui-simple](https://github.com/Falkesand/FalkForge/tree/main/demo/11-custom-ui-simple).

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
forge init         Scaffold a starter installer project (csproj + Program.cs + payload)
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
dotnet test                 # fast default: ~7,000+ tests, minutes; heavyweight e2e skipped
dotnet publish -c Release   # NativeAOT for Engine + Elevation
```

**Requirements:** .NET 10 SDK (10.0.103+), Windows (for MSI compilation and P/Invoke)

### Running the full end-to-end suite

The default `dotnet test` skips the heavyweight end-to-end tests (building the entire
60+ demo catalog via `dotnet run`, the `forge verify --rebuild` ceremony, and the live
SignServer Docker-container tests) so a fresh clone gets a green run in minutes. Opt in
with the `FALKFORGE_E2E` environment variable — CI always sets it:

```powershell
$env:FALKFORGE_E2E = '1'; dotnet test FalkForge.slnx   # PowerShell
```

```bash
FALKFORGE_E2E=1 dotnet test FalkForge.slnx             # bash
```

Gated tests carry `[Trait("Category", "E2E")]` and skip through
`tests/FalkForge.Integration.Tests/E2EGate.cs`, which documents the mechanism.
A small set of install-execution tests additionally mutate real machine state
(firewall rules, IIS sites, SQL databases, local users) and require a second
opt-in on a machine you own: `FALKFORGE_REAL_SYSTEM_E2E=1` plus an elevated shell. Tests
with additional external requirements still self-gate on those on top of the opt-in
(e.g. the SignServer tests also need a Linux-capable Docker/Podman runtime, and the
NuGet-consumer e2e needs the local feed produced by `scripts/pack.ps1`).
See [`docs/testing/real-machine-verification.md`](docs/testing/real-machine-verification.md)
for a full runbook (VM setup, exact commands, and the manual checklist for live paths that
have no automated real-machine coverage yet).

A stuck test cannot hang the run indefinitely: every test project runs under the
Microsoft.Testing.Platform hang-dump guard (see `tests/Directory.Build.props`), which
dumps and kills a test host after 10 minutes without test progress
(override: `dotnet test -p:FalkHangDumpTimeout=2m`).

> **NuGet lock files:** The solution uses `RestorePackagesWithLockFile=true` (set in
> `Directory.Build.props`). After adding or changing any package reference, regenerate the
> lock files before committing (`dotnet restore --force-evaluate`) and commit the updated
> `packages.lock.json` files alongside the `.csproj` change.

## Demos and Documentation

- **[demo/](https://github.com/Falkesand/FalkForge/tree/main/demo)** -- 60+ demo
  projects covering every feature, from hello-world to delta updates, signing,
  and reproducible builds. New here? The
  [demo README](https://github.com/Falkesand/FalkForge/blob/main/demo/README.md)
  has a starter track.
- **[docs/tutorials/](https://falkesand.github.io/FalkForge/docs/tutorials/index.html)**
  -- narrative, demo-by-demo walkthroughs: start with
  [Getting Started](https://falkesand.github.io/FalkForge/docs/tutorials/getting-started.html),
  then [MSI Basics](https://falkesand.github.io/FalkForge/docs/tutorials/msi-basics.html);
  coming from WiX? there's a
  [tutorial for that](https://falkesand.github.io/FalkForge/docs/tutorials/coming-from-wix.html) too.
- **[documentation.html](https://falkesand.github.io/FalkForge/documentation.html)**
  -- self-contained full reference (24 sections, searchable, dark/light theme).
  Start with **Concepts** (section 2) if you're new to Windows Installer.
- **[docs/provenance.md](https://github.com/Falkesand/FalkForge/blob/main/docs/provenance.md)**
  -- the supply-chain provenance surface: SBOM, payload integrity, reproducible
  builds, attestations.
- **Online**: <https://falkesand.github.io/FalkForge/> -- the manual and tutorials,
  hosted from this repository via GitHub Pages, browsable without cloning.

## Releases & Provenance

Release artifacts (Engine, Engine.Elevation, CLI) are published on version tag pushes
via the [release workflow](https://github.com/Falkesand/FalkForge/blob/main/.github/workflows/release.yml), each with a
`SHA256SUMS.txt` checksum file and (where plan support allows)
[GitHub build provenance attestations](https://docs.github.com/en/actions/security-guides/using-artifact-attestations-to-establish-provenance-for-builds):

```bash
gh attestation verify forge.exe --repo Falkesand/FalkForge
```

## Support

Found a bug, have a question, or want to request a feature? Open an issue at
<https://github.com/Falkesand/FalkForge/issues>.

## License

[PolyForm Perimeter 1.0.0](https://github.com/Falkesand/FalkForge/blob/main/LICENSE.md) -- in plain words: use FalkForge freely to
build, package, and ship installers for your own or your customers' software,
commercially or not. The only thing you can't do is take FalkForge itself and offer
it (or a derivative) as a competing installer/packaging/setup-authoring product.

Copyright Peter Falkesand.
