# 3. Bespoke IFalkLogger instead of Microsoft.Extensions.Logging.ILogger

- Status: Accepted
- Date: 2026-07-27
- Deciders: Peter Falkesand

## Context

FalkForge runs as three cooperating processes for a single install — the WPF UI, the NativeAOT
`FalkForge.Engine`, and the NativeAOT `FalkForge.Engine.Elevation` companion — plus build-time
compilers and plugins that run in-process inside a developer's build. A search of the solution
finds `Microsoft.Extensions.Logging.ILogger` referenced **zero** times in `src/` (the one hit in
the whole tree is an incidental line in a NuGet `packages.lock.json` transitive-dependency
listing, not code). By contrast, `IFalkLogger` (`src/FalkForge.Core/Diagnostics/IFalkLogger.cs`)
is referenced in 347 places across roughly 47 files, spanning `FalkForge.Core`,
`FalkForge.Engine`, `FalkForge.Engine.Elevation`, `FalkForge.Compiler.*`, and the extension
projects.

`IFalkLogger`'s own XML documentation describes a concurrency contract that a plain
`Microsoft.Extensions.Logging.ILogger` does not make explicit at the interface level: reads and
writes of `MinimumLevel` are documented as safe to perform concurrently with in-flight `Log(...)`
calls (`IFalkLogger.cs:13-18`), and every log call carries a `SessionCorrelationId` (`Guid`) that
is "stamped on every `LogEntry` and forwarded in `FalkForge.Engine.Protocol.Messages.LogMessage`
frames so that log streams from the UI, Engine, and Elevation processes can be correlated"
(`IFalkLogger.cs:27-33`). That correlation id is set once per session and flows across the named-pipe
boundary between the three processes — a cross-process concern a general-purpose logging
abstraction is not shaped around, since `ILogger` scopes and category names are per-process and
have no built-in wire representation.

`IFalkLogger` methods are also deliberately synchronous ("All methods are synchronous to avoid
async overhead in hot paths", `IFalkLogger.cs:6`), and the interface lives in `FalkForge.Core`,
which is consumed by the NativeAOT `Engine`/`Engine.Elevation` assemblies bound by ADR-0002 —
so it is source-gen/reflection-free by construction, whereas the full
`Microsoft.Extensions.Logging` provider ecosystem (configuration binding, DI-based provider
registration) pulls in exactly the patterns ADR-0002 excludes from those assemblies.

## Decision

We will keep `FalkForge.Diagnostics.IFalkLogger` as the one logging abstraction used throughout
FalkForge, and we will not introduce `Microsoft.Extensions.Logging.ILogger` (or any
`ILoggerFactory`/provider-based logging package) as a parallel or replacement abstraction. Any
process- or session-level concern that needs to travel with a log entry — correlation id, level,
structured properties — is added directly to `IFalkLogger`/`LogEntry`, not bolted on via a second
logging library.

## Consequences

- **No free integration with the ASP.NET/Extensions logging ecosystem.** Application hosts that
  already stand up `Microsoft.Extensions.Logging` (Serilog sinks, `ILoggerFactory`, OpenTelemetry
  exporters that expect `ILogger`) cannot point directly at FalkForge's log stream. A consumer
  embedding FalkForge must write and maintain their own adapter from `IFalkLogger` calls to their
  logging stack; FalkForge ships no `ILoggerFactory`-backed implementation of `IFalkLogger` today.
- **The cross-process correlation id and concurrency guarantees are first-class**, not
  reconstructed from `ILogger` scopes after the fact — `SessionCorrelationId` is a typed property
  on the interface itself, and every implementation must honor the documented concurrent-access
  contract.
- **One fewer package dependency** in the NativeAOT-published processes, consistent with ADR-0002.
- **Reversal cost.** Migrating to `ILogger` later would mean re-threading `SessionCorrelationId`
  through `ILogger`'s scope mechanism (which is per-process, not wire-serializable) across three
  processes and re-verifying AOT-safety of whichever provider package was chosen — a non-trivial,
  cross-cutting change touching all ~47 files that currently take an `IFalkLogger` dependency.
