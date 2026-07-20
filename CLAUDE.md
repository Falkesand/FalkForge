# FalkForge

C# MSI/Bundle installer framework. Fluent API, MSI compiler via `msi.dll` P/Invoke, NativeAOT bundle engine with WPF UI. Extensions: Firewall, IIS, SQL, .NET, Dependency, Util, Driver, Http. Output: MSI, MSM, MSP, MST, EXE bundle.

## Build & Test
```bash
dotnet build FalkForge.slnx            # 0 warnings (TreatWarningsAsErrors)
dotnet test FalkForge.slnx             # ~8000 tests, xUnit (Microsoft.Testing.Platform)
dotnet publish -c Release              # NativeAOT for Engine + Elevation
```
.NET 10, C# latest, nullable enabled, central package management. SDK 10.0.103. Solution: `FalkForge.slnx` (37 src + 30 test projects).

Heavyweight end-to-end tests (whole demo catalog, `forge verify --rebuild`, SignServer container) are skipped by default; opt in with `FALKFORGE_E2E=1`. A further set that mutates real machine state (firewall, IIS, SQL, users) needs `FALKFORGE_REAL_SYSTEM_E2E=1` plus an elevated shell — run those only on a machine you own.

## Where to look

- **Codebase map** — full project inventory, dependency graph, key patterns, per-project layout, namespaces: [`docs/codebase-map.md`](docs/codebase-map.md). Read on demand; it can drift, so the source wins on conflict.
- **Architecture + API reference** — `documentation.html` (24 sections, searchable) is the maintained source of truth. Hand-authored and tracked in git; edit it directly, there is no generator. Also published at <https://falkesand.github.io/FalkForge/>.
- **Cross-module relationships** — `graphify-out/` knowledge graph: read `graphify-out/GRAPH_REPORT.md` before broad source searches; after changing code, `graphify update .` keeps it current.

## Conventions

- **Result\<T\> over exceptions** — `Core/Result.cs`, `.Success(value)` / `.Failure(error)` with a typed `ErrorKind`. Exceptions only for genuinely unrecoverable situations.
- **One primary type per file** — filename = primary type; partials use a suffix (`Foo.Bar.cs`). A cohesive contract cluster (request + response + handler) may co-locate while small.
- **NativeAOT-safe in Engine/Elevation/Protocol** — no reflection/dynamic/BinaryFormatter, manual DI, source-gen JSON only.
- **Documentation is hand-authored** — when you change public API or behavior, update `documentation.html` (and `docs/codebase-map.md` if the structure moved). Keep both honest: no fabricated codes, no fictional APIs.
- **Extensions attach explicitly** — `new MsiCompiler().Use(extension)`; there is no auto-discovery (NativeAOT, by design).
