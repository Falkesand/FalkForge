# Logging Instrumentation Design (assessment item C2)

Status: **APPROVED 2026-07-08.** Decisions locked: rename `IEngineLogger` → `IFalkLogger` (D1a), add the `Exception?` overload (D2), reuse existing error codes as a `code` structured property (D2), skip Microsoft.Extensions.Logging / LoggerMessage source-gen and extend the own abstraction (D2), and implement **all four phases** (scope). Target: raise the Logging & Observability category (stuck at 6.8 for two assessment cycles) by giving `Compiler.Msi`, `Decompiler`, and the `Plugins.*` projects a real diagnostic trail. Phase 0 is a hard prerequisite for 1/2/3; once it lands, Phases 1/2/3 touch disjoint assemblies and can run in parallel.

## Problem

Three assemblies are observably silent:
- `FalkForge.Compiler.Msi` — `MsiAuthoring.Compile` runs ~14 sequential steps (validate → resolve → recipe/tables → cabinets → DB commit → sign → ICE → SBOM → WinGet). Every failure returns `Result<string>.Failure(...)` and stops; there is no logger, no progress hook, and **two failures are silently swallowed** (SBOM sidecar write, ICE-infrastructure failure). A `forge build` user sees only the final success/failure line — none of the 14 steps, even with `--verbose`.
- `FalkForge.Decompiler` — `MsiDecompiler`/`BundleDecompiler` read ~10 tables sequentially, all `Result<T>` fail-fast, no logger.
- `Plugins.Sql` / `Plugins.Odbc` / `Plugins.FileSystem` — zero logging; diagnostic surface is `Result<T>`/exceptions only.

## The load-bearing constraint: the dependency graph

The project already has an AOT-safe logging abstraction — `IEngineLogger` (`FalkForge.Engine.Logging`), synchronous, string category + `IReadOnlyDictionary<string,string>` structured properties, implementations `EngineLogger` (file writer + size rotation + optional UI-pipe forwarding), `NullLogger`, and `ListLogger` (test). It deliberately does **not** use `Microsoft.Extensions.Logging` (no `ILogger` anywhere in `src`), which is correct for the NativeAOT engine.

But it lives in the **wrong project to share**:

```
Core            (no deps — the only common floor)
 ├─ Compiler.Msi    (→ Core, Extensibility, Localization, Platform*)
 │   └─ Decompiler  (→ Core, Compiler.Msi, Compiler.Bundle, Extensibility)
 ├─ Engine.Protocol (→ Core)   ← defines LogLevel
 └─ Engine  (exe)   (→ Engine.Protocol, Platform.Windows, Compiler.Msi)  ← defines IEngineLogger
 Plugins.Sql/Odbc/FileSystem   (→ Core)
```

`Engine → Compiler.Msi` is a real edge, so `Compiler.Msi`/`Decompiler` **cannot** reference `IEngineLogger` in its current home without a cycle. `Engine.Protocol` is not a candidate either — Compiler.Msi/Decompiler don't reference it. **`FalkForge.Core` is the only project referenced (directly or transitively) by all of Compiler.Msi, Decompiler, Engine, and the three Plugins.** The shared logging contract must live in Core.

Second key fact: the **compiler runs at build time** (CLI `forge build`, Studio), not at install time — the install Engine consumes a pre-built MSI. So compiler/decompiler logs primarily flow to the **CLI's `IConsoleOutput`** (Spectre + `--json`) or a build-time file, *not* to the install Engine's `EngineLogger`. The Engine's file logger is one possible sink, not the main path.

## Proposal

### D1. Move the logging contract down to Core (keep the impls in Engine)

Move the **contract only** — the interface, `LogLevel`, and `LogEntry` — into `FalkForge.Core` (e.g. `FalkForge.Diagnostics` namespace). Leave the concrete `EngineLogger` (file rotation, pipe forwarding, per-session GUID path) in `FalkForge.Engine`; it just implements the now-Core-hosted interface. `NullLogger` moves with the interface (Core needs a no-op default); `ListLogger` stays in `FalkForge.Testing`.

- `Engine.Protocol` already references Core, so its wire `LogLevel` becomes Core's `LogLevel` (single definition, used by both the log contract and the protocol messages).
- Net churn: the interface/enum/record move + `EngineLogger`/`NullLogger` implement the moved interface. Engine behavior unchanged.

**Open decision (D1a): rename `IEngineLogger`?** Once it's used by non-Engine code, the name is a misnomer. Recommend renaming to a neutral `IFalkLogger` (or `IDiagnosticLog`) — mechanical, ~33 call sites, improves readability at the compiler sites. Alternative: keep the name to minimize the diff. *Recommendation: rename.*

### D2. Enrich the contract minimally

- **Add an `Exception?` overload:** `void Log(LogLevel, string category, string message, Exception? exception = null, IReadOnlyDictionary<string,string>? properties = null)`. Today every call site interpolates `ex.Message` and loses the stack trace; the compiler's P/Invoke paths (cabinet FCI, msi.dll) have real exceptions worth capturing. Low cost, high value.
- **Do NOT invent an EventId scheme.** The project already has a diagnostic taxonomy — the error codes (PKG001-011, FEA/SVC/DLG, ICE*, JSN001-014, DEC/BDC, etc.). Reuse it: where a log line corresponds to a coded error, attach it as a structured property `{"code":"PKG001"}`. Category stays the component name (`"MsiAuthoring"`, `"CabinetBuilder"`, `"MsiDecompiler"`). This avoids a second, parallel numbering system.
- **Do NOT introduce `Microsoft.Extensions.Logging` / `LoggerMessage` source-gen.** (The assessment text mentioned it, but MEL's `LoggerMessage` requires `ILogger` and would contradict the deliberate own-abstraction/AOT design.) For hot-path call sites, get the same zero-alloc-when-disabled benefit by guarding: `if (log is not null && log.MinimumLevel <= LogLevel.Debug) log.Debug(...)` before building the message string. This is the manual equivalent and needs no new dependency.

### D3. Thread it exactly like Engine already does

Match the existing pattern verbatim: **optional nullable constructor parameter, `?.`-guarded, defaults to `null`** (no-op, zero overhead, preserves every existing `new MsiCompiler().Compile(...)` call). The static `MsiAuthoring.Compile` takes the logger as a parameter.

- `MsiCompiler(..., IFalkLogger? logger = null)` → passes to `MsiAuthoring.Compile(package, outputPath, extensions, logger)`.
- `MsiDecompiler(..., IFalkLogger? logger = null)`, `BundleDecompiler(...)` same.
- Plugins take the same optional param on their entry types.

### D4. What to log

- **Compiler.Msi** — one `Info` per pipeline step (start), one `Info` at compile complete with output path + size + elapsed; `Warning` for the two currently-swallowed non-fatal failures (SBOM sidecar, ICE-infra) — this is the highest-value fix, it surfaces silent data loss; `Error` (with `code` property + `Exception`) at each `Result.Failure` site before returning; `Debug` per table producer and per `CabinetBuilder.BuildCabinet` (per-cabinet progress).
- **Decompiler** — `Info` at decompile start/complete, `Debug` per table read, `Warning`/`Error` per read failure with the DEC/BDC code.
- **Plugins** — `Debug`/`Info` around SQL discovery / ODBC enumeration / folder-browse, `Warning`/`Error` on failure.

### D5. Wiring the sinks (per host)

- **CLI (`forge build`/`decompile`)** — add a small `IConsoleOutput`-backed adapter implementing the Core logger: route `Error`/`Warning` → `WriteError`, `Info` → normal line, `Debug`/`Verbose` → gated behind the existing `--verbose` flag. In `--json` mode, collect log entries into the JSON envelope. The CLI already has `IConsoleOutput`; the adapter lives in `FalkForge.Cli`, no dependency on `FalkForge.Engine`.
- **Studio** — pass a logger that surfaces to its output panel (Studio-side adapter).
- **Engine install-time** — mostly N/A for the compiler (pre-built MSI), but any Compiler.Msi/Decompiler code reachable from Engine can now receive the existing `EngineLogger` since both sides speak the Core interface.

### D6. AOT / performance

- Core interface is reflection-free; `EngineLogger` unchanged (already AOT-safe). No new package dependency (no MEL).
- Null-default + `?.` guard means zero cost when no logger is supplied (the common in-process/test path).
- Level-guard hot-path call sites (D2) to avoid string interpolation when disabled.

## Phasing

- **Phase 0 — foundation (~4h):** move contract to Core (+ optional rename), add `Exception` overload, Engine impls implement the Core interface, full suite green. Behavior-preserving; Engine logging output unchanged. Merge Gate.
- **Phase 1 — Compiler.Msi (~8h):** thread the logger through `MsiCompiler`/`MsiAuthoring`/`CabinetBuilder`, instrument the 14 steps (fix the 2 swallowed failures), add the CLI `IConsoleOutput` adapter + wire `forge build`. Tests: assert log entries at key steps via `ListLogger`; assert the swallowed-SBOM case now warns.
- **Phase 2 — Decompiler (~4h):** thread through `MsiDecompiler`/`BundleDecompiler`, per-table reads, wire `forge decompile`.
- **Phase 3 — Plugins (~2h):** light logging in the three plugins.

Total ~15–18h, matching the assessment estimate. Phases are independently mergeable; Phase 0 must land first. Logging category could reach ~7.6+ after Phases 0–2 (the two silent assemblies gain a trail); Phase 3 finishes it.

## Decisions (locked 2026-07-08)

1. **D1a — rename `IEngineLogger` → `IFalkLogger`** when moving to Core. ✅ Approved.
2. **D2 — add the `Exception?` overload.** ✅ Approved.
3. **D2 — reuse existing error codes as a `code` structured property** (no new EventId scheme). ✅ Approved.
4. **D2 — skip Microsoft.Extensions.Logging / LoggerMessage; extend the own abstraction with level-guarded call sites.** ✅ Approved.
5. **Scope — all four phases.** ✅ Approved.
