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

## Known gap: engine stub

Bundles are only runnable when the bundle compiler is given the published NativeAOT
engine (`BundleCompiler.EngineStubPath`); nothing wires it automatically yet, so a
default build embeds an empty placeholder stub. `publish.ps1` produces the engine
binaries; shipping them to consumers (e.g. a runtime package the compiler resolves)
is a tracked follow-up.
