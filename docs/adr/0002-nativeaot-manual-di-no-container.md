# 2. NativeAOT with manual DI, no container

- Status: Accepted
- Date: 2026-07-27
- Deciders: Peter Falkesand

## Context

Three assemblies sit on the trust-critical / performance-critical path of every install:
`FalkForge.Engine` (the install-time engine) and `FalkForge.Engine.Elevation` (the elevated
companion process) both set `<PublishAot>true</PublishAot>` and `<InvariantGlobalization>true</InvariantGlobalization>`
(`src/FalkForge.Engine/FalkForge.Engine.csproj`, `src/FalkForge.Engine.Elevation/FalkForge.Engine.Elevation.csproj`).
`FalkForge.Engine.Protocol`, the wire-format library shared by both processes and the UI, is
written to stay NativeAOT-safe even though it does not itself publish as an AOT exe.

The project's own governing rule states the constraint plainly (`CLAUDE.md`, "Conventions"):

> **NativeAOT-safe in Engine/Elevation/Protocol** — no reflection/dynamic/BinaryFormatter, manual
> DI, source-gen JSON only.

A generic DI container (Microsoft.Extensions.DependencyInjection or similar) typically resolves
services via reflection over constructors and attributes, and most `IOptions<T>` /
configuration-binding patterns rely on the same reflection-based model. A search of `src/` finds
**zero** `IOptions<T>` registrations and no hosting container (`Microsoft.Extensions.Hosting`,
`IServiceCollection`) anywhere in the solution. JSON is exclusively handled through
`System.Text.Json` source-generated contexts — 17 `*JsonContext` classes across the solution
(e.g. `src/FalkForge.Engine.Protocol/Integrity/IntegrityEnvelopeJsonContext.cs`,
`src/FalkForge.Engine/Planning/PlanJsonContext.cs`, `src/FalkForge.Cli/InstallerConfigJsonContext.cs`)
rather than the default reflection-based serializer.

Composition instead happens by hand: `EngineSession`, `InstallerPipelineBuilder`, and the
extension registry (`FalkForge.Extensibility.IExtensionRegistry`) wire up their dependencies
through explicit constructor calls and builder methods, not container resolution.

## Decision

We will not take a dependency on a general-purpose DI container or the
`Microsoft.Extensions.Hosting`/`IOptions<T>` configuration-binding model anywhere in
`FalkForge.Engine`, `FalkForge.Engine.Elevation`, or `FalkForge.Engine.Protocol` (and, by
extension, in code those assemblies pull in transitively). Object graphs in these assemblies are
composed by hand — explicit constructors, factory methods, and builder chains — and configuration
is bound by explicit, source-generated (or manually written) deserialization, never reflection- or
attribute-driven binding.

This is a direct consequence of NativeAOT publication for the Engine and Elevation processes:
reflection-heavy container resolution and `IOptions<T>` binding are unreliable or unsupported
under trimming/AOT, and the install-time and elevated-command paths are exactly where a runtime
`MissingMethodException` from a trimmed constructor would be least acceptable (it fails mid-install,
possibly mid-elevation, with no fallback).

## Consequences

- **Startup and configuration validation is manual.** There is no `IOptions<T>` validation
  pipeline, no `IValidateOptions<T>`, no `ConfigurationBinder` producing typed errors for free.
  Each configuration surface (e.g. `EngineSessionOptions`, the JSON config loaders in
  `FalkForge.Cli`) must implement its own validation and error reporting, and that validation
  logic itself must stay reflection-free.
- **ASP.NET-shaped patterns are unreachable by design.** Idioms common in web codebases —
  registering cross-cutting concerns via `IServiceCollection` extension methods, resolving
  scoped/singleton lifetimes from a container, binding strongly-typed options sections — do not
  transplant into `Engine`/`Engine.Elevation`/`Engine.Protocol`. A contributor arriving from ASP.NET
  work will look for a `Startup`/`Program.ConfigureServices` shape and not find one; this must be
  documented (this ADR) rather than rediscovered per session.
- **Composition roots are larger and more explicit.** `EngineSession` and the pipeline builders own
  more wiring code than a container would otherwise hide, in exchange for AOT-safety and
  predictable, trim-friendly startup with no reflection surprises.
- **The constraint is process-scoped, not solution-wide.** `FalkForge.Studio` (WPF) and other
  non-AOT tooling are free to use richer composition patterns if a future need arises — this ADR
  binds only the NativeAOT-published processes and the protocol library shared between them.
- **Reversal cost.** Adopting a DI container later in these three assemblies would require
  re-auditing every registration for AOT/trimming compatibility and would reopen the reflection
  surface the "no reflection/dynamic/BinaryFormatter" rule exists to close — a future ADR would
  need to explicitly accept that risk, not merely add a package reference.
