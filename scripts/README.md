# FalkForge scripts

| Script | Purpose |
|--------|---------|
| `pack.ps1` | `dotnet pack` every packable project (Release) into a folder that doubles as a **local NuGet feed**. Default output `./artifacts/nuget`. `-Version` overrides the single-source version. |
| `publish.ps1` | Build Release and publish the shippable executables: the `forge` CLI plus the NativeAOT `FalkForge.Engine` / `FalkForge.Engine.Elevation` binaries. Default output `./artifacts/publish`. `-SkipEngine` skips the slow NativeAOT publishes. |
| `coverage.ps1` | Code coverage via `dotnet-coverage` (works under Microsoft.Testing.Platform where `--collect` is a no-op). |
| `scan-deps.ps1` | Dependency scanning. |

## Single-source version

The version for every assembly, the `forge` CLI, and every `.nupkg` is set in **one**
place: `VersionPrefix` + `VersionSuffix` in the root `Directory.Build.props`
(currently `0.1.0-alpha.1`). Intended progression: `alpha.N` → `beta.1` (friend beta)
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

### Follow-up: shipping the engine to NuGet consumers

A consumer who installed `FalkForge.Tool` / referenced the libraries from a feed has
no repo checkout and no publish output. The tracked follow-up is a RID-specific
runtime package (e.g. `FalkForge.Engine.Runtime.win-x64`) carrying the published
NativeAOT engine, referenced by `FalkForge.Tool` (so the engine lands in the tool's
directory, where probe 2 already finds it) and surfaced to SDK builds via an MSBuild
property that sets `FALKFORGE_ENGINE_STUB`. Until that package exists, packaged
consumers set `FALKFORGE_ENGINE_STUB` to a published engine themselves. The engine
binary is deliberately NOT checked into git — resolution always points at a fresh
publish output, never a stale committed blob.
