# Phase 8: Build System, CLI & Polish — Design Document

**Date:** 2026-02-15
**Status:** Approved
**Scope:** 4 high-value tasks (8A, 8B, 8C, 8E)

## Overview

Phase 8 adds developer tooling and polish to FalkForge: JSON-based localization, a Spectre.Console CLI tool, an MSI decompiler, and multi-threaded cabinet creation. These are the highest-value remaining features for production readiness.

**Target metrics:** ~26 src projects, ~17 test projects, ~1,550 tests, 0 warnings.

## New Projects

| Task | Project | Type | Dependencies |
|------|---------|------|-------------|
| 8A | FalkForge.Localization | classlib | Core |
| 8B | FalkForge.Cli | exe | Core, Compiler.Msi, Compiler.Bundle, Decompiler, Localization |
| 8C | FalkForge.Decompiler | classlib | Core, Compiler.Msi |
| 8E | *(in Compiler.Msi)* | enhancement | — |

Plus 3 new test projects (Localization.Tests, Cli.Tests, Decompiler.Tests). 8E tests go into existing Compiler.Msi.Tests.

## 8A: Localization

**JSON format** — one file per culture:
```json
{
  "ProductName": "My Application",
  "WelcomeTitle": "Welcome to [ProductName] Setup",
  "LicenseHeader": "License Agreement"
}
```

**Types:**
- `LocalizationModel` — Parsed string table (Dictionary<string, string>)
- `LocalizationLoader` — Loads JSON files, resolves culture fallback chain (de-AT -> de -> en-US)
- `LocalizedStringResolver` — Resolves `!(loc.StringId)` references in MSI property values
- `CultureFallbackChain` — Builds ordered list of cultures to try
- `LocalizationBuilder` — Fluent API: `AddCulture()`, `DefaultCulture()`

**Integration:**
- `PackageBuilder.Localization(Action<LocalizationBuilder>)` — new fluent method
- MsiCompiler substitutes `!(loc.X)` references during table emission
- Bundle engine resolves localized strings for UI display

**Validation:** LOC001 (duplicate string ID), LOC002 (missing default culture), LOC003 (unresolved reference), LOC004 (invalid JSON)

**Tests (~30):** Loader round-trip, fallback chain, substitution, missing key handling, invalid JSON.

## 8B: CLI Tool

**Framework:** Spectre.Console.Cli + Spectre.Console for rich output.

**Commands:**

| Command | Description |
|---------|-------------|
| `forge build <project.cs>` | Compile C# installer definition into MSI/Bundle |
| `forge validate <project.cs>` | Run validators without producing output |
| `forge inspect <file.msi>` | Display MSI metadata (tables, features, summary info) |
| `forge decompile <file.msi>` | Decompile MSI into C# source |

**Architecture:**
- `Program.cs` — CommandApp configuration
- `Commands/BuildCommand.cs` — Roslyn scripting to load C# definitions
- `Commands/ValidateCommand.cs` — Validator-only mode
- `Commands/InspectCommand.cs` — MSI table inspection with tree views
- `Commands/DecompileCommand.cs` — Delegates to Decompiler project
- `Settings/*.cs` — CommandSettings with -o, -c, --verbose options

**C# script loading:** Roslyn scripting (Microsoft.CodeAnalysis.CSharp.Scripting) evaluates .cs files with FalkForge.Core referenced.

**Exit codes:** 0 = success, 1 = validation failure, 2 = compilation error, 3 = runtime error

**Tests (~25):** Command parsing, settings validation, exit code mapping, output formatting.

## 8C: MSI Decompiler

**Windows-only** (`[SupportedOSPlatform("windows")]`).

**API:**
```csharp
Result<PackageModel> Decompile(string msiPath)
Result<string> DecompileToCSharp(string msiPath)
```

**Types:**
- `MsiDecompiler` — Main entry point
- `TableReader` — Generic MSI table row reader
- `TableReaders/` — Per-table readers (File, Registry, Component, Feature, Directory, Service, Shortcut, Property, Upgrade)
- `CSharpEmitter` — PackageModel -> C# source via StringBuilder
- `DirectoryResolver` — MSI directory table parent-child resolution

**Flow:** Open database -> read summary info -> read tables -> assemble PackageModel -> optionally emit C#

**Limitations:** Custom action binaries extracted but not decompiled. Cabinet contents listed but not extracted.

**Validation:** DEC001 (cannot open), DEC002 (unsupported version), DEC003 (table read failure)

**Tests (~35):** Table readers, directory resolution, C# emitter output, round-trip (build -> decompile -> compare).

## 8E: Multi-threaded Cabinet Creation

**Enhancement to existing FalkForge.Compiler.Msi.**

**Types:**
- `ParallelCabinetBuilder` — Builds multiple cabinets concurrently via Parallel.ForEachAsync
- `CabinetWorkItem` — Record struct: CabinetName, FileEntries, CompressionLevel
- `CabinetBuildResult` — Record struct: CabinetName, OutputPath, FileCount, CompressedSize

**Integration:**
- MsiCompiler switches to ParallelCabinetBuilder when file count exceeds threshold (default 100)
- `PackageBuilder.CabinetThreads(int)` — configurable, defaults to Environment.ProcessorCount
- Each thread gets isolated CabinetBuilder instance (no shared state)
- Progress via Interlocked counters

**Thread safety:** Each cabinet is fully independent — separate files, paths, P/Invoke contexts.

**Fallback:** Single cabinet -> single-threaded CabinetBuilder (zero overhead).

**Tests (~15):** Parallel build, result aggregation, thread config, single-cabinet fallback, cancellation.

## Parallelization Strategy

All 4 tasks are independent:
```
Step 1: 8A (Localization) || 8E (Parallel Cabinets) — no dependencies
Step 2: 8C (Decompiler) — depends only on Core + Compiler.Msi
Step 3: 8B (CLI) — depends on all above
Step 4: Final verification + commit
```

Maximum parallelism: 8A + 8E + 8C can run simultaneously. 8B follows after.

## Verification

1. `dotnet build` — zero warnings, zero errors
2. `dotnet test` — all tests pass (target ~1,550)
3. Code review with Opus + Sonnet
4. OWASP security audit (CLI input handling, decompiler path safety)
5. Commit on feature/phase8-cli-polish, merge to main
