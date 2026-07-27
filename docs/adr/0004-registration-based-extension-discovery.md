# 4. Registration-based extension discovery, not assembly scanning

- Status: Accepted
- Date: 2026-07-27
- Deciders: Peter Falkesand

## Context

FalkForge ships a set of first-party extensions (Firewall, IIS, SQL, .NET, Dependency, Util,
Driver, Http) that attach to the MSI/bundle compilers to contribute custom tables, components,
dialog steps, and elevated execution steps. The extension contract is defined by
`IExtensionRegistry` (`src/FalkForge.Extensibility/IExtensionRegistry.cs`), which exposes
`RegisterTableContributor`, `RegisterComponentContributor`, `RegisterDryRunContributor`,
`RegisterExecutionContributor`, and `RegisterDialogStep` — one method per extension point, each
taking an already-constructed contributor instance.

`ExtensionRegistration.Register` (`src/FalkForge.Extensibility/ExtensionRegistration.cs`) is the
helper most extensions route through; its own XML doc states its purpose: enforce "extension
identity uniqueness and host version compatibility before invoking
`IFalkForgeExtension.Register`" so that "duplicate names and incompatible plugins surface as
`PluginCompatibilityException` at registration time rather than silently shadowing or producing
late, hard-to-diagnose failures" (`ExtensionRegistration.cs:4-9`). Attachment is explicit at the
call site — `new MsiCompiler().Use(extension)` (`src/FalkForge.Compiler.Msi/MsiCompiler.cs:69`,
with a `params IFalkForgeExtension[]` overload at line 77) — and there is no directory- or
assembly-scanning discovery mechanism anywhere in the compiler pipeline: no `Assembly.LoadFrom`
loop over a plugins folder, no `[assembly: ...]` marker attribute scan, no reflection-based type
discovery. The project's own convention states this is deliberate: "Extensions attach explicitly
— `new MsiCompiler().Use(extension)`; there is no auto-discovery (NativeAOT, by design)"
(`CLAUDE.md`, "Conventions").

This is consistent with ADR-0002: `MsiCompiler` and the extensibility types are consumed from the
same object graph that composes `Engine`/`Engine.Elevation` at runtime, and reflection-driven
assembly scanning (`Assembly.GetTypes()` over a plugins directory, attribute-based discovery) is
exactly the kind of dynamic type discovery that is unreliable under trimming/NativeAOT and that
the "no reflection/dynamic" rule rules out.

## Decision

Extensions are attached by explicit, compile-time registration only:
`compiler.Use(extension)`/`compiler.Use(extension1, extension2, ...)`, with
`ExtensionRegistration.Register` enforcing name-uniqueness and `MinHostVersion` compatibility at
the point of registration. We will not add a directory-drop or assembly-scanning discovery
mechanism (no scanning a plugins folder for DLLs implementing `IFalkForgeExtension`, no
attribute-driven auto-registration) to any of the compiler projects.

## Consequences

- **Third-party extensions must be compiled in, not dropped in.** An integrator who wants to add
  IIS/SQL/Firewall/custom-table support to their build must reference the extension assembly and
  call `.Use(...)` themselves; there is no "place a DLL in a plugins folder and it appears" story.
  This is a real limitation for a plugin ecosystem compared to scanning-based frameworks (MEF-style
  composition, ASP.NET's assembly-scanning module discovery).
  - This should not be true forever: `IExecutionContributor`'s documentation already anticipates that
  a discovery layer built on top of this registration surface is a legitimate future addition — see
  the default no-op registration comment in `IExtensionRegistry.cs:9-20` — but it would have to be
  built as an explicit, AOT-safe manifest/registry mechanism, not reflection-based scanning, to stay
  consistent with this decision.
- **Failure mode is fail-fast and attributable.** A name collision or a version-incompatible
  extension throws `PluginCompatibilityException` synchronously at the `.Use(...)` call site,
  with the offending extension's name and version in the message — there is no silent shadowing
  the way a last-registered-wins scanning order could produce.
- **No reflection surface to keep AOT-safe** for extension loading itself; the same guarantee
  ADR-0002 establishes for the Engine/Elevation composition root extends to extension attachment
  in the compilers.
- **Reversal cost.** Adding scanning-based discovery later would require either accepting
  reflection in the compiler assemblies (acceptable there, since `FalkForge.Compiler.Msi`/`Bundle`
  are not NativeAOT-published) or building a source-generator-based discovery mechanism instead —
  either way, a new ADR, since it changes the "must be compiled in" consequence documented here.
