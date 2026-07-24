# FalkForge scripts

| Script | Purpose |
|--------|---------|
| `pack.ps1` | `dotnet pack` every packable project (Release) into a folder that doubles as a **local NuGet feed**. Default output `./artifacts/nuget`. `-Version` overrides the single-source version. By default it first publishes the NativeAOT engine + elevation companion and packs them into `FalkForge.Engine.Runtime.win-x64` and `FalkForge.Tool` (see below); `-SkipEnginePublish` reuses an existing `artifacts/publish/engine`, `-NoEngine` packs without the engine (explicit opt-out). |
| `publish.ps1` | Build Release and publish the shippable executables: the `forge` CLI plus the NativeAOT `FalkForge.Engine` / `FalkForge.Engine.Elevation` binaries. Default output `./artifacts/publish`. `-SkipEngine` skips the slow NativeAOT publishes. |
| `coverage.ps1` | Code coverage via `dotnet-coverage` (works under Microsoft.Testing.Platform where `--collect` is a no-op). See [`docs/testing/coverage-baseline.md`](../docs/testing/coverage-baseline.md) for the current baseline. |
| `mutation.ps1` | Runs Stryker.NET mutation testing (`-TestProject <Name>.Tests -SourceProject <Name>`) against one FalkForge project. Periodic, not per-commit. See [`docs/testing/mutation-testing.md`](../docs/testing/mutation-testing.md) for how to read the results and the traps that make this not "just run Stryker". |
| `scan-deps.ps1` | Dependency scanning. |
| `verify-real-machine.ps1` | Runs the real-system e2e suite (`FALKFORGE_E2E=1` + `FALKFORGE_REAL_SYSTEM_E2E=1`) on an elevated machine. **Mutates machine state** — run only on a disposable VM. See [`docs/testing/real-machine-verification.md`](../docs/testing/real-machine-verification.md) for the full runbook, including the manual checks it can't automate. |
| `pack-docs.ps1` | Zips the static HTML documentation site (`documentation.html` as both itself and `index.html`, plus `docs/tutorials/*.html` and its `shared/` assets -- no markdown, no internal plans) into `./artifacts/falkforge-docs-<version>.zip`. Same content published to GitHub Pages ([`.github/workflows/pages.yml`](../.github/workflows/pages.yml)) and attached to GitHub Releases ([`.github/workflows/release.yml`](../.github/workflows/release.yml)). `-Version` overrides the single-source version; `-Output` overrides the destination folder. |

## Single-source version

The version for every assembly, the `forge` CLI, and every `.nupkg` is set in **one**
place: `VersionPrefix` + `VersionSuffix` in the root `Directory.Build.props`
(currently `0.5.0-beta.1`). Intended progression: `alpha.N` → `beta.1` (friend beta)
→ `beta.N` (public beta) → `1.0.0` GA.

When bumping, also update the pinned `ExpectedVersion` in
`tests/FalkForge.Core.Tests/VersionSingleSourceTests.cs` and
`tests/FalkForge.Cli.Tests/VersionInfoTests.cs` — the pin makes every version change a
deliberate, reviewed act.

## License and package metadata

FalkForge ships under the **PolyForm Perimeter License 1.0.0** (`LICENSE.md` at the
repo root — free for any use except providing a competing installer/packaging/
setup-authoring product). PolyForm Perimeter is not an SPDX expression, so packages
use `PackageLicenseFile` and `LICENSE.md` is packed into every `.nupkg`. Shared
NuGet metadata (authors, URLs, tags, readme) lives in the root
`Directory.Build.props`; packability is deny-by-default there and each shippable
project opts in with `IsPackable=true`.

Notable package shapes:

- `FalkForge.Tool` — the `forge` CLI as a .NET global tool
  (`dotnet tool install -g FalkForge.Tool --add-source <feed>` → `forge`).
- `FalkForge.Sdk` — MSBuild project SDK (`PackageType=MSBuildSdk`).

## Using the local feed

```powershell
./scripts/pack.ps1                     # produces ./artifacts/nuget
dotnet nuget add source ./artifacts/nuget --name falkforge-local
dotnet tool install -g FalkForge.Tool --prerelease
```

## Engine stub resolution

> For the full list of environment variables FalkForge defines (name, type, effect,
> default), see the "Environment Variables" reference in `documentation.html` — the
> canonical, single source of truth. This section covers `FALKFORGE_ENGINE_STUB`'s
> resolution order in more depth than that table does.

Bundles embed the published NativeAOT engine as their self-extracting front. The
bundle compiler resolves it automatically (`EngineStubLocator`), in order:

1. `FALKFORGE_ENGINE_STUB` environment variable — path to `FalkForge.Engine.exe` or
   to a directory containing it. Authoritative when set.
2. Well-known locations next to the host application: the engine beside it, an
   `engine/` subdirectory, or a sibling `engine/` directory (the `publish.ps1`
   layout, where `<Output>/forge` sits next to `<Output>/engine`).
3. The repository publish output: walk up to the `FalkForge.slnx` marker, then
   `artifacts/publish/engine/FalkForge.Engine.exe` — so after running `publish.ps1`
   once, any bundle build inside the repo is runnable.

Framework-dependent apphosts (a `FalkForge.Engine.exe` with `FalkForge.Engine.dll`
beside it) are rejected: only the self-contained NativeAOT engine can run alone as a
bundle front.

When no engine resolves, the build **fails loud**. The design-time placeholder stub
(a non-runnable bundle for signing/verification tooling) is an explicit opt-in:
`BundleCompiler.AllowPlaceholderStub = true` or `forge build --no-engine`.

### Shipping the engine to NuGet consumers

A consumer who installed `FalkForge.Tool` / referenced the libraries from a feed has
no repo checkout and no publish output. The engine reaches them through packaging —
resolution order and fail-loud behavior above are unchanged, the packages simply land
the binaries where the existing probes already look:

- **`FalkForge.Engine.Runtime.win-x64`** — RID-specific runtime package carrying the
  published NativeAOT `FalkForge.Engine.exe` + `FalkForge.Engine.Elevation.exe` under
  `tools/engine/`. Its `build/` (and `buildTransitive/`) props copy both binaries into
  the consuming project's output under `engine\` — probe 2's "engine subdirectory
  beside the host" — so a code-first installer (`dotnet exec <installer.dll>
  --forge-build`) embeds the real engine with no environment variable. The props also
  expose `$(FalkForgeEngineDir)`; opt out of the output copy with
  `FalkForgeCopyEngineToOutput=false`.
- **`FalkForge.Tool`** — .NET tool packages cannot declare package dependencies, so
  the tool packs the same two binaries itself into `tools/net10.0/any/engine/`.
  Installed, they sit in the `engine\` subdirectory beside the tool host — probe 2 —
  so `dotnet tool install FalkForge.Tool` → `forge build` yields a runnable bundle
  with zero manual setup.
- **`FalkForge.Sdk`** — `Sdk.props` implicitly references
  `FalkForge.Engine.Runtime.win-x64` (version pinned at pack time via a generated
  `Sdk/FalkForgeVersion.props`; opt out with
  `<FalkForgeImplicitEngineRuntime>false</FalkForgeImplicitEngineRuntime>`), and the
  `FalkGenerateInstaller` target passes `FALKFORGE_ENGINE_STUB=$(FalkForgeEngineDir)`
  to the installer execution as belt-and-braces beside the output-copy probe.

Packing fails **loud** when the published binaries are missing (a runtime package or
tool with a placeholder engine must never ship); `-NoEngine` /
`-p:FalkForgePackEngine=false` is the explicit opt-out. Note that a feed packed with
`-NoEngine` omits `FalkForge.Engine.Runtime.win-x64` entirely, so a
`<Project Sdk="FalkForge.Sdk/x.y.z">` consumer restoring from it hits NU1101 on the
SDK's implicit engine-runtime reference — set
`<FalkForgeImplicitEngineRuntime>false</FalkForgeImplicitEngineRuntime>` (fine when a
runnable bundle isn't needed, e.g. MSI-only projects). Only win-x64 exists today —
the engine is the bundle front for the TARGET machine, so the package works from any
build host, but non-win-x64 target engines are a future task. The engine binaries are
deliberately NOT checked into git — packing always consumes a fresh publish output,
never a stale committed blob.

The end-to-end proof lives in
`tests/FalkForge.Integration.Tests/NuGetConsumerEndToEndTests.cs` (gated on the
`pack.ps1` feed): tool installed from a local feed builds a self-extracting bundle,
and the runtime package's props land the engine exactly where `EngineStubLocator`
resolves it.
