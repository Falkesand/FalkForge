# Dependency Scan — snitch + nugone — 2026-05-11

## Summary

**Result: Clean baseline. No unused PackageReferences found in src/.**

Both tools ran against all 29 src + 27 test projects. The two nugone findings were verified as
false positives (package name / namespace mismatch). The snitch per-project mode hit a known
.NET 10 + central package management incompatibility; the equivalent SDK-native analysis
(`dotnet list package --include-transitive`) was used as documented fallback.

---

## Tool Versions

| Tool | Version | Command |
|------|---------|---------|
| snitch | 2.0.0 | `snitch <project>.csproj` |
| nugone | 2.1.1 | `nugone analyze --project FalkForge.slnx --dry-run` |
| dotnet SDK | 10.0.103 | `dotnet list package --include-transitive` (snitch fallback) |

---

## Command Lines Used

```powershell
# nugone — solution-level (supports .slnx)
nugone analyze --project D:\Git\FalkInstaller\FalkForge.slnx --dry-run --format text

# snitch — attempted per-project (see Limitations below)
snitch <project>.csproj   # run from each project's own directory

# snitch fallback — SDK-native transitive package listing
dotnet list D:\Git\FalkInstaller\FalkForge.slnx package --include-transitive
```

---

## Snitch Limitations

snitch v2.0.0 uses a bundled MSBuild instance rather than the SDK's MSBuild. It cannot build
projects that require:

- .NET 10 SDK (SDK 10.0.103)
- Central Package Management (`Directory.Packages.props`)
- `.slnx` solution format

**Observed error:** `Error: Could not build <project>.csproj` on all 28 of 29 src projects.
The sole exception was `FalkForge.Sdk` (target `netstandard2.0`, no external NuGet
dependencies beyond analyzers) — snitch reported "Everything looks good!" for that project.

**Mitigation:** `dotnet list package --include-transitive` provides equivalent signal:
it lists every project's direct + transitive NuGet graph using the real SDK restore output,
allowing manual verification that no project holds a redundant explicit reference to a package
it receives transitively.

---

## nugone Findings

nugone v2.1.1 supports `.slnx` natively and completed successfully.

| Project | nugone Finding | Verdict | Evidence |
|---------|---------------|---------|----------|
| FalkForge.Compiler.Bundle | `Octopus.Octodiff 2.0.549 (Direct, Unused)` | **False positive** | `using Octodiff.Core; using Octodiff.Diagnostics;` in `Compression/DeltaCompressor.cs` |
| FalkForge.Engine | `Octopus.Octodiff 2.0.549 (Direct, Unused)` | **False positive** | `using Octodiff.Core; using Octodiff.Diagnostics;` in `Download/DeltaApplicator.cs` |

**Root cause of false positives:** nugone matches by NuGet package ID (`Octopus.Octodiff`)
but the package publishes its types under the `Octodiff` namespace, not `Octopus.Octodiff`.
nugone does not find a `using Octopus.Octodiff` statement, so incorrectly reports unused.

No packages were removed.

---

## snitch-equivalent Findings (dotnet list package --include-transitive)

The table below covers all src projects. "Transitive only" means the package appears in the
project's graph but only as a pull-through from a dependency — no `<PackageReference>` exists
in the project's own csproj. "Direct" means an explicit `<PackageReference>` is present.

### src/ projects — Octopus.Octodiff distribution

| Project | Reference Type | Action |
|---------|---------------|--------|
| FalkForge.Compiler.Bundle | Direct (top-level) | Kept — actively used in `DeltaCompressor.cs` |
| FalkForge.Engine | Direct (top-level) | Kept — actively used in `DeltaApplicator.cs` |
| FalkForge.Cli | Transitive | No action — correct (via Compiler.Bundle dep) |
| FalkForge.Decompiler | Transitive | No action — correct (via Compiler.Bundle dep) |
| FalkForge.Studio | Transitive | No action — correct (via Compiler.Bundle dep) |
| FalkForge.Testing | Transitive | No action — correct (via Engine dep) |
| All other src/ projects | Not present | Clean |

### All 29 src/ projects — per-project finding table

| Project | Unused direct refs found | Action |
|---------|--------------------------|--------|
| FalkForge.Cli | 0 | Clean |
| FalkForge.Compiler.Bundle | 0 (nugone FP) | Clean |
| FalkForge.Compiler.Msi | 0 | Clean |
| FalkForge.Compiler.Msix | 0 | Clean |
| FalkForge.Core | 0 | Clean |
| FalkForge.Decompiler | 0 | Clean |
| FalkForge.Engine | 0 (nugone FP) | Clean |
| FalkForge.Engine.Elevation | 0 | Clean |
| FalkForge.Engine.Protocol | 0 | Clean |
| FalkForge.Extensibility | 0 | Clean |
| FalkForge.Extensions.Dependency | 0 | Clean |
| FalkForge.Extensions.DotNet | 0 | Clean |
| FalkForge.Extensions.Driver | 0 | Clean |
| FalkForge.Extensions.Firewall | 0 | Clean |
| FalkForge.Extensions.Http | 0 | Clean |
| FalkForge.Extensions.Iis | 0 | Clean |
| FalkForge.Extensions.Sql | 0 | Clean |
| FalkForge.Extensions.Util | 0 | Clean |
| FalkForge.Localization | 0 | Clean |
| FalkForge.Platform | 0 | Clean |
| FalkForge.Platform.Windows | 0 | Clean |
| FalkForge.Plugins.FileSystem | 0 | Clean |
| FalkForge.Plugins.Odbc | 0 | Clean |
| FalkForge.Plugins.Sql | 0 | Clean |
| FalkForge.Sdk | 0 (snitch: clean) | Clean |
| FalkForge.Studio | 0 | Clean |
| FalkForge.Testing | 0 | Clean |
| FalkForge.Ui | 0 | Clean |
| FalkForge.Ui.Abstractions | 0 | Clean |

Test projects were inspected; all transitive Octodiff references are correct pull-throughs.
No test project carries a spurious direct dependency.

---

## Final Status

| Check | Result |
|-------|--------|
| nugone solution scan | 2 findings — both verified false positives, no action taken |
| snitch per-project | Incompatible with .NET 10 + CPM; fallback used |
| snitch fallback (dotnet list --include-transitive) | Clean — no redundant direct refs |
| Packages removed | **0** |
| Build after scan | Not required (no PackageReferences changed) |

---

## Re-running the Scan

See `scripts/scan-deps.ps1` for a reproducible one-command re-run.
