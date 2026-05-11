# RFC: Deepen ModelValidator into a rules-as-data engine

**Status:** COMPLETED 2026-05-11 — all 18 phases shipped on `main`. Phases 1–12: commits b66f113, f0cc78f, ea8990f, 1024374, 4b0fd55, 1b1fe99, 043c45c, 790b874, 6b8c00b, 5702426 (per-area rule engine + facades). Phase 15: 3fbcb6e (call-site migration). Phase 13/14: 31767b5, 24ed6e1, a37fabf, e996f71, db87190, 926a5c3, 0450cba (extension rule merge + Firewall/Dependency/IIS/SQL/Util migrations). Phase 16: a91d675, f2fdc13, a9b9859, 7f98dcf (legacy deletion). Phase 17: 6d84941, b81258e, 4d4483f (CLI flags). Phase 18: 5cf5cd5 (architecture doc + plugin-extensibility refresh).
**Author:** architectural review, 2026-04-11
**Scope:** `src/FalkForge.Core/Validation/`, `src/FalkForge.Extensibility/`, `src/FalkForge.Extensions.*/`, `tests/FalkForge.Core.Tests/Validation/`

## Problem

`src/FalkForge.Core/Validation/ModelValidator.cs` is a 519-LOC static god class with one public method, `Validate(PackageModel) → ValidationResult`, and 23 private `ValidateX` methods plus 11 inline top-level rules covering package metadata. It contains 61 validation rule checks identified by hardcoded string-literal error codes (PKG001-011, FEA001-005, SVC001-008, REG001-007, CTB001-011, SHC, FNT, INI, PRM, FAS, CA, RRG, RMF, CRF, MVF, DPF, SCT, SDP, ASM, MDT, SGN, MUP, DNG), all stored in-line as `result.AddError("PKG001", "...")` calls with no central registry. `PatchValidator`, `MergeModuleValidator`, and `TransformValidator` are parallel static classes in the same folder, each following the same pattern but with zero rule reuse — even "product code is a GUID" has to be re-expressed per validator.

The `ValidationResult` shape is flat: a sealed class with an internal `List<ValidationMessage>`, where each message is just `(Severity, Code, Message)`. There is no structured location, no rule metadata, no way for a caller to programmatically ask "which feature index triggered FEA002" short of parsing the message text. The only severity levels are `Warning` and `Error`; there is no `Info` tier, no category tagging, no description field for documentation or CLI help output.

The testability consequence is that every rule is private and therefore untestable in isolation. The 345-LOC `ModelValidatorTests.cs` file exists because someone wrote one test per error code by standing up a minimal valid `PackageModel`, mutating one field, and asserting the error appeared through the orchestrator. This works but has three problems: cross-rule interactions are impossible to test because short-circuiting in one rule affects whether a later rule runs; rules are coupled to the orchestrator's order of operations; and rule metadata (what does PKG001 mean, what severity, which model section) only exists in the rule author's head — not queryable from code.

The extension consequence is a live bug analogous to the Firewall-contributor half-wiring fixed by Cycle 2. The `IExtensionValidator` interface exists in `src/FalkForge.Extensibility/` as an 8-LOC contract, but **nothing in the codebase implements it**. The Firewall, IIS, SQL, and Dependency extensions each have their own parallel validation logic with custom error types (`DependencyValidationError`, `FirewallValidationError`, etc.) that return from helper classes nothing else calls. `MsiCompiler.Compile` invokes `ModelValidator.Validate` but no extension validator runs. A compile can succeed with invalid firewall, IIS, or SQL configuration as long as the extension's emission code doesn't throw — and some of it doesn't throw, it just emits broken MSI rows.

The cross-table consequence is that rules requiring more than one collection in the model have no mechanism to express the relationship cleanly. "Every `FeatureComponentRef.ComponentId` must reference an existing Component" is not checked today because the orchestrator provides no shared pre-computed index. `FEA002` (duplicate feature ID) is the one exception: `ValidateFeature` recurses through the tree carrying a `HashSet<string>` as an accumulator parameter, which works but duplicates the traversal logic the moment any other tree-shaped rule is added. There is no general solution, and every rule that wants to iterate the feature tree has to re-walk it from scratch.

Finally, navigability is bad. Answering "where does `SVC005` (service with `Account == User` must have `UserName`) fire and what's its severity" requires reading `ModelValidator.cs` line-by-line until finding the inline `AddError("SVC005", ...)` call. There is no central rule catalog, no `--list-rules` CLI support, no documentation path.

This is a shallow-modules problem concentrated in a single file, with a missing testability seam, a missing extension integration point, a missing cross-table state mechanism, and a missing metadata catalog all feeding off the same root cause: rules are expressed as imperative code buried inside an orchestrator, not as declarative data. Deepening the module means rewriting each rule as an immutable `ValidationRule` record stored in a flat registry, adding a `RuleContext` that pre-builds shared indexes once per run, collapsing the four validator silos into filter expressions over a common registry, and wiring extensions into the registry through a new `IFalkForgeExtension.GetValidationRules()` default method.

## Proposed Interface

The design splits the public surface into a facade for the 95% case (one-line `Check` returning `Result<Unit>`) and a rule-engine contract for the 5% case (tests, CLI `--list-rules`, per-project severity overrides, future Studio integration).

### Public facade — 95% case

```csharp
namespace FalkForge.Validation;

public static class ModelValidator
{
    // 95% caller — zero-allocation happy path
    public static Result<Unit> Check(PackageModel package);
    public static Result<Unit> Check(MergeModuleModel module);
    public static Result<Unit> Check(PatchModel patch);
    public static Result<Unit> Check(TransformModel transform);

    // 5% caller — rich report with warnings, locations, and metadata
    public static ValidationReport Inspect(PackageModel package, ValidationOptions? options = null);
    public static ValidationReport Inspect(MergeModuleModel module, ValidationOptions? options = null);
    public static ValidationReport Inspect(PatchModel patch, ValidationOptions? options = null);
    public static ValidationReport Inspect(TransformModel transform, ValidationOptions? options = null);

    // Rule discovery — powers CLI --list-rules and future Studio tooling
    public static IReadOnlyList<ValidationRule> ListRules(ValidationTarget target = ValidationTarget.Package);
}
```

Real `MsiCompiler.Compile` integration shrinks to a one-liner:

```csharp
public Result<string> Compile(PackageModel package, string outputPath)
{
    var check = ModelValidator.Check(package);
    if (check.IsFailure) return Result<string>.Failure(check.Error);

    // ... resolve components, build tables, emit MSI ...
}
```

CLI `ValidateCommand` uses `Inspect` because it wants warnings:

```csharp
var report = ModelValidator.Inspect(package);
foreach (var warning in report.Warnings)
    _console.MarkupLine($"[yellow]Warning {warning.RuleId}:[/] {Markup.Escape(warning.Message)}");
foreach (var error in report.Errors)
    _console.MarkupLine($"[red]Error {error.RuleId}:[/] {Markup.Escape(error.Message)}");
return report.IsValid ? 0 : ExitCodes.ValidationFailure;
```

### Rules-as-data core — 5% case

```csharp
namespace FalkForge.Validation;

/// <summary>
/// Immutable record describing a single validation rule.
/// Metadata fields carry ID, severity, section, title, description.
/// Evaluate delegate closes over the model via a shared RuleContext.
/// Static readonly fields are the canonical storage — one field per rule.
/// </summary>
public sealed record ValidationRule(
    RuleId Id,
    Severity Severity,
    ModelSection Section,
    string Title,
    string Description,
    Func<RuleContext, IEnumerable<Violation>> Evaluate)
{
    /// <summary>
    /// Helper for the common "one scalar check" rule shape. Wraps a predicate
    /// returning a nullable Violation into the full Evaluate signature.
    /// </summary>
    public static ValidationRule Single(
        RuleId id,
        Severity severity,
        ModelSection section,
        string title,
        string description,
        Func<RuleContext, Violation?> check);
}

public readonly record struct RuleId(string Value)
{
    public string Prefix { get; }
    public static implicit operator string(RuleId id) => id.Value;
}

public enum Severity { Error, Warning, Info }

public enum ModelSection
{
    Package, Feature, Component, File, Service, Registry, Shortcut,
    CustomAction, CustomTable, Sequence, MajorUpgrade, Property,
    LaunchCondition, Signing, MediaTemplate,
    MergeModule, Patch, Transform,
    Extension_Util, Extension_Dependency, Extension_Firewall,
    Extension_DotNet, Extension_Iis, Extension_Sql
}

public enum ValidationTarget { Package, MergeModule, Patch, Transform }
```

### RuleContext — shared per-run state with pre-built indexes

```csharp
/// <summary>
/// Per-validation-run context. Built once by the engine in an O(n) pre-pass.
/// Rules read from it; rules never mutate it. Cross-table lookups become
/// O(1) dictionary hits; tree-walking rules iterate a flat pre-computed list.
/// </summary>
public sealed class RuleContext
{
    public PackageModel Package { get; }

    // Pre-built indexes shared by every rule in one run
    public FrozenDictionary<string, ComponentModel> ComponentsById { get; }
    public FrozenDictionary<string, FeatureModel> FeaturesById { get; }
    public FrozenDictionary<string, CustomTableModel> CustomTablesByName { get; }

    // Pre-walked feature tree — flat list of (feature, depth, path) tuples
    // Any rule that wants to iterate features uses this instead of recursing
    public ImmutableArray<FeatureWalkEntry> FeatureWalk { get; }

    // Factory for tests — builds a minimal context without needing the engine
    public static RuleContext ForTest(PackageModel package);

    internal RuleContext(PackageModel package /* ... */);
}

public readonly record struct FeatureWalkEntry(
    FeatureModel Feature,
    int Depth,
    ModelPath Path);
```

### Structured model path — location typing

```csharp
/// <summary>
/// Typed path through the PackageModel. Built compositionally via fluent
/// calls. Allocated only when a violation actually fires — the happy path
/// never constructs one.
/// </summary>
public readonly record struct ModelPath(ImmutableArray<PathSegment> Segments)
{
    public static readonly ModelPath Root;

    public ModelPath Field(string name);
    public ModelPath Index(int i);
    public ModelPath Key(string key);

    public override string ToString(); // "package.Features[2].Services[0].Name"
}

public readonly record struct PathSegment
{
    public enum Kind : byte { Root, Field, Index, Key }
    public Kind SegmentKind { get; }
    public string? Text { get; }
    public int NumIndex { get; }

    public static PathSegment Root { get; }
    public static PathSegment Field(string name);
    public static PathSegment Index(int i);
    public static PathSegment Key(string key);
}
```

### Violation + report

```csharp
public sealed record Violation(
    RuleId RuleId,
    Severity Severity,
    ModelPath Path,
    string Message);

public sealed record ValidationReport(ImmutableArray<Violation> Violations)
{
    public bool IsValid => Violations.All(v => v.Severity != Severity.Error);
    public IEnumerable<Violation> Errors { get; }
    public IEnumerable<Violation> Warnings { get; }

    public Result<Unit> ToResult();
    public ILookup<string, Violation> ByRule();
}

public sealed record ValidationOptions
{
    public IReadOnlySet<string> IgnoredRules { get; init; } = FrozenSet<string>.Empty;
    public bool WarningsAsErrors { get; init; }
    public bool StopOnFirstError { get; init; }
    public RuleRegistry? Rules { get; init; }

    public static ValidationOptions Default { get; } = new();
}
```

### Registry + engine

```csharp
/// <summary>
/// Immutable registry of rules. Filter operations return new registries.
/// Frozen FrozenDictionary backing for O(1) lookup by ID.
/// </summary>
public sealed class RuleRegistry
{
    public RuleRegistry(ImmutableArray<ValidationRule> rules);

    public ImmutableArray<ValidationRule> Rules { get; }
    public FrozenDictionary<RuleId, ValidationRule> ById { get; }

    public ValidationRule? Find(RuleId id);

    // Pure operations — all return new registries, never mutate
    public RuleRegistry Without(params RuleId[] ids);
    public RuleRegistry WithAdded(params ValidationRule[] extra);
    public RuleRegistry OverrideSeverity(RuleId id, Severity severity);
    public RuleRegistry FilterSection(ModelSection section);
}

/// <summary>
/// Canonical built-in registries. Static readonly, built once at module load.
/// </summary>
public static class CoreRuleCatalog
{
    public static RuleRegistry Package { get; }
    public static RuleRegistry MergeModule { get; }
    public static RuleRegistry Patch { get; }
    public static RuleRegistry Transform { get; }
}

/// <summary>
/// Runs a rule registry against a package. Builds RuleContext once per run,
/// then walks the registry accumulating violations into a ValidationReport.
/// </summary>
public sealed class ValidationEngine
{
    public ValidationEngine(RuleRegistry registry);
    public ValidationReport Run(PackageModel package);
}
```

### Extension integration

```csharp
namespace FalkForge.Extensibility;

public interface IFalkForgeExtension
{
    string Name { get; }
    void Register(ExtensionContext context);

    // NEW — default empty. Extensions override to contribute rules.
    // Called once during engine setup; rules join the shared registry.
    ImmutableArray<ValidationRule> GetValidationRules() => [];
}
```

The orphan `IExtensionValidator` interface is deleted.

### What the deepened module owns

- `ValidationRule` as the hero immutable record type, one static readonly field per rule in per-area classes (`PackageRules`, `ServiceRules`, `FeatureRules`, `ComponentRules`, `CustomTableRules`, `RegistryRules`, `MajorUpgradeRules`, etc.).
- `RuleContext` with pre-built `ComponentsById`, `FeaturesById`, `CustomTablesByName`, and `FeatureWalk`. Built once per run in an O(n) pre-pass; shared by every rule.
- `ModelPath` as the typed location, built only when a violation fires.
- `RuleRegistry` as the immutable collection with filter operations returning new registries.
- `ValidationEngine.Run` as the pure orchestrator: pre-pass + walk registry + accumulate violations.
- `ModelValidator.Check` and `ModelValidator.Inspect` as the public facade with zero-allocation success path for `Check`.
- `ValidationReport.ToResult()` as the single authoritative error-message formatter so `Check` and `Inspect` produce identical error strings.
- `CoreRuleCatalog.Package` / `MergeModule` / `Patch` / `Transform` as the canonical built-in registries, each built as filter expressions over the base package registry plus validator-specific rules.
- Rule registration pipeline that merges extension rules into the catalog at `Installer.Build()` setup time and freezes on first `Check` call.

### What the deepened module hides

- The 519-LOC `ModelValidator.cs` god class. Deleted.
- `PatchValidator.cs`, `MergeModuleValidator.cs`, `TransformValidator.cs` — parallel silos. Deleted, replaced by filter expressions.
- 61 hardcoded string-literal error codes. Replaced by typed `RuleId` constants in per-area classes.
- Inline `AddError(code, message)` calls. Replaced by `yield return new Violation(...)` inside rule delegates.
- The recursive `ValidateFeature` tree walk with accumulator parameter. Replaced by `ctx.FeatureWalk` pre-flattened traversal.
- The orphan `IExtensionValidator` interface. Deleted.
- Parallel extension validator silos (`DependencyValidator`, `FirewallValidator`, `SqlValidator`). Migrated into `GetValidationRules()` implementations returning `ValidationRule[]` records.
- The custom extension-specific error result types (`DependencyValidationError`, `FirewallValidationError`, etc.). Replaced by the shared `Violation` record.
- Rule metadata being stored only in the rule author's memory. Replaced by typed fields on the `ValidationRule` record.

## Dependency Strategy

This module is **pure in-process, no ports needed**. Its only input is the model (`PackageModel`, `MergeModuleModel`, `PatchModel`, or `TransformModel`) and optionally a `ValidationOptions` record. No I/O, no async, no external dependencies. The `ValidationEngine` is a pure function from model to report.

### Extension rule flow

1. `Installer.Build()` (or equivalent setup path) enumerates loaded `IFalkForgeExtension` implementations via the existing plugin mechanism.
2. For each extension, it calls `ext.GetValidationRules()` (default empty for extensions that don't opt in).
3. Collected rules are merged into the appropriate core catalog via `CoreRuleCatalog.Package.WithAdded(...)`.
4. The merged registry is stored in a singleton behind the `ModelValidator` facade.
5. First call to `ModelValidator.Check` or `Inspect` reads the merged registry. Subsequent calls are lookup-only — the registry is frozen after first use.

This matches the pattern used by Cycle 2's extension contributor fix and avoids reflection, attribute scanning, and runtime codegen.

### Zero-allocation discipline

The `Check(model) → Result<Unit>` happy path must not allocate. The design achieves this through:

- `Result<Unit>.Success(Unit.Value)` is a cached singleton value type.
- Rule delegates use `static` lambdas enforced by the C# compiler (`Evaluate: static ctx => ...`), preventing accidental closure capture.
- Rules that iterate return `Array.Empty<Violation>()` when they detect no issues via the `ValidationRule.Single` helper.
- `ModelPath` is built only inside the violation branch — never on the happy path.
- Iteration rules pay one iterator-enumerator allocation per rule per run (bounded at ~60, not per-item).
- `RuleContext` pre-built indexes are allocated once per run regardless of rule count, amortized across the ~60 rules that read them.

A dedicated test with `GC.GetAllocatedBytesForCurrentThread()` asserts zero allocation on a clean-model `Check` call.

### Patch/Merge/Transform rule reuse

Each non-package catalog is a filter expression over the base:

```csharp
public static RuleRegistry Patch { get; } =
    CoreRuleCatalog.Package
        .Without(new RuleId("SVC001"), new RuleId("SVC005"), new RuleId("CTB007") /* inapplicable to patches */)
        .WithAdded(PatchRules.Msp001_PatchGuidRequired,
                   PatchRules.Msp002_TargetProductRequired,
                   PatchRules.Msp003_BaselineRequired,
                   PatchRules.Msp004_SequenceRangeValid);
```

Rules that apply to multiple targets (e.g., "product code is a GUID") live once and are referenced by each catalog. The silos dissolve.

For models with shape differences from `PackageModel`, the validator adapts through a lightweight view rather than generics: `PatchModel.AsPackageView()` returns a `PackageModel`-shaped projection for the rules that share shape with package validation. Patch-specific rules access the full `PatchModel` through a `RuleContext` subclass extension (`PatchRuleContext : RuleContext`) that carries the original model alongside the indexes.

## Testing Strategy

**Replace, don't layer.** The 345-LOC orchestrator-only `ModelValidatorTests.cs` gets rewritten as one small test class per rule, each testing that rule in isolation against a minimal model fragment. Zero tests go through the full orchestrator except for a handful of engine-level tests verifying registry filtering, extension merge, and zero-allocation behavior.

### New boundary tests to write

At the rule level (one test class per rule, ~3 tests per class, ~60 × 3 = ~180 tests):

1. **Per-rule happy path** — minimal model with the field set correctly, assert rule returns empty.
2. **Per-rule violation** — minimal model with the field violating the rule, assert rule returns a single `Violation` with the expected `RuleId`, `Severity`, and `ModelPath`.
3. **Per-rule edge cases** — empty collections, null optional fields, whitespace-only strings, boundary values for numeric ranges.

Example — `Svc005_UserAccountRequiresUserNameTests`:

```csharp
[Fact]
public void User_account_without_username_yields_violation()
{
    var package = new PackageModel("Test", "1.0.0",
        Services: [new ServiceModel(Name: "svc", Account: ServiceAccount.User, UserName: null)]);
    var ctx = RuleContext.ForTest(package);

    var violations = ServiceRules.Svc005_UserAccountRequiresUserName.Evaluate(ctx).ToList();

    Assert.Single(violations);
    Assert.Equal("SVC005", violations[0].RuleId.Value);
    Assert.Equal(Severity.Error, violations[0].Severity);
    Assert.Contains("UserName", violations[0].Path.ToString());
}

[Fact]
public void System_account_without_username_is_valid()
{
    var package = new PackageModel("Test", "1.0.0",
        Services: [new ServiceModel(Name: "svc", Account: ServiceAccount.LocalSystem)]);
    var ctx = RuleContext.ForTest(package);

    Assert.Empty(ServiceRules.Svc005_UserAccountRequiresUserName.Evaluate(ctx));
}
```

At the engine level (~15 tests covering infrastructure):

1. **`Check` returns cached success on clean model** — assert `Result<Unit>.Success(Unit.Value)` reference equality.
2. **`Check` zero-allocation** — `GC.GetAllocatedBytesForCurrentThread()` before/after on a clean-model run.
3. **`Check` aggregates error messages** — multi-violation model, assert `Error.Message` contains each violation formatted.
4. **`Check` and `Inspect` produce identical error strings** — single formatter enforced.
5. **`Inspect` returns warnings and errors** — mixed-severity model, assert `report.Warnings.Count` and `report.Errors.Count`.
6. **Registry `Without` removes rule** — assert filtered registry does not contain the removed rule's ID.
7. **Registry `WithAdded` includes new rule** — assert filtered registry contains the added rule.
8. **Registry `OverrideSeverity` changes rule severity** — assert overridden rule's severity matches.
9. **`CoreRuleCatalog.Patch` excludes inapplicable rules** — assert `Patch.ById` does not contain `SVC001`, `CTB007`.
10. **`ValidationOptions.IgnoredRules` skips matching rules** — assert suppressed rule does not produce violations.
11. **`ValidationOptions.WarningsAsErrors` promotes warnings** — assert all warnings appear as errors in the result.
12. **Extension rule integration** — load a test `IFalkForgeExtension` implementing `GetValidationRules`, assert its rules appear in `ModelValidator.ListRules` output and fire during `Check`.
13. **`RuleContext.ComponentsById` pre-pass correctness** — construct a package with known component IDs, assert `ctx.ComponentsById` is a bijection.
14. **`RuleContext.FeatureWalk` pre-pass correctness** — construct a nested feature tree, assert `ctx.FeatureWalk` flattens in depth-first order with correct `ModelPath` for each entry.
15. **Cross-table rule correctness** — rule references `ctx.ComponentsById` to check FeatureComponentRef, construct package with orphan ref, assert rule fires.

### Old tests to delete

- All 345 LOC of `FalkForge.Core.Tests/ModelValidatorTests.cs`. Replaced by per-rule test classes.
- `PatchValidatorTests.cs`, `MergeModuleValidatorTests.cs`, `TransformValidatorTests.cs` — replaced by per-rule tests for the rules in those catalogs plus engine-level tests for catalog filtering.
- Any extension-specific validator test (`DependencyValidatorTests`, `FirewallValidatorTests`, etc.) — replaced by per-rule tests for the rules the extension contributes.

### Test environment needs

- `RuleContext.ForTest(package)` factory helper for rule tests, builds a minimal context without requiring the engine.
- Per-area test helper methods for constructing minimal valid and minimal invalid `PackageModel` fragments (e.g., `MinimalServicePackage(Action<ServiceModel> mutate)`).
- No new NuGet packages, no platform requirements, no Windows-only tests. Everything runs on Linux CI.

## Implementation Recommendations

Durable architectural guidance, decoupled from current file paths:

### What the module should own

- `ValidationRule` records as the canonical storage of validation logic. One record per rule, stored as a `public static readonly` field in a per-area class.
- The `RuleContext` pre-pass that builds shared indexes and flattens trees once per run, amortizing cross-table lookup cost across all rules.
- The immutable `RuleRegistry` with pure filter operations (`Without`, `WithAdded`, `OverrideSeverity`) that return new registries.
- The `ValidationEngine.Run` orchestrator as a pure function from model to report.
- The `ModelValidator.Check` facade with zero-allocation happy path and single-formatter error aggregation.
- The extension rule merge at setup time, producing a frozen registry shared by all subsequent validation calls.
- `CoreRuleCatalog` as the canonical collection of built-in registries, each built as a filter expression over the base package catalog plus target-specific rules.

### What the module should hide

- The imperative 519-LOC `ModelValidator.cs` god class structure.
- Inline string-literal error codes.
- The `AddError`/`AddWarning` mutation API on `ValidationResult`.
- The recursive `ValidateFeature` tree walk — replaced by `ctx.FeatureWalk` pre-pass.
- The parallel `PatchValidator`/`MergeModuleValidator`/`TransformValidator` silos.
- The orphan `IExtensionValidator` interface and the parallel extension validator silos.
- The rule ordering logic — rules are independent and the registry iteration order is deterministic but irrelevant to correctness.

### What the module should expose

Two public surfaces:

1. **Facade** — `ModelValidator.Check` for zero-allocation happy path, `ModelValidator.Inspect` for rich reports, `ModelValidator.ListRules` for discovery. Used by `MsiCompiler.Compile`, `ValidateCommand`, `ScriptLoader.Load`, `Installer.Build`, and the test wrapper.
2. **Rule engine** — `ValidationRule` record, `RuleContext` with pre-built indexes, `RuleRegistry` with filter operations, `ValidationEngine.Run`, `ValidationReport`, `Violation`, `ModelPath`. Used by rule authors, per-rule tests, CLI `--list-rules`, per-project severity overrides, and future Studio integration.

### How callers should migrate

**`MsiCompiler.Compile`** keeps the same call shape with a two-line diff:

```csharp
// Before
var validation = ModelValidator.Validate(package);
if (!validation.IsValid)
    return Result<string>.Failure(new Error(ErrorKind.Validation, FormatErrors(validation)));

// After
var check = ModelValidator.Check(package);
if (check.IsFailure) return Result<string>.Failure(check.Error);
```

**`ValidateCommand` (CLI)** migrates from `Validate` to `Inspect` to preserve warning output:

```csharp
// Before
var result = ModelValidator.Validate(package);
foreach (var w in result.Warnings) ...
foreach (var e in result.Errors) ...

// After
var report = ModelValidator.Inspect(package);
foreach (var w in report.Warnings) ...
foreach (var e in report.Errors) ...
```

**`ScriptLoader.Load`** and **`Installer.Build`** migrate identically to `MsiCompiler.Compile`.

**Test wrapper** (`FalkForge.Testing.InstallerValidator`) migrates to `Inspect` for test utility purposes.

**Extensions with custom rules** (Firewall, IIS, SQL, Dependency) migrate their parallel validator logic into `GetValidationRules()` implementations on their `IFalkForgeExtension` class. Custom error types (`FirewallValidationError`, etc.) are deleted — the shared `Violation` record replaces them.

### Implementation sequencing

The refactor is large but each rule migrates independently, making it highly TDD-friendly. Sketch of order:

1. **Define core types** — `ValidationRule`, `RuleId`, `Severity`, `ModelSection`, `ModelPath`, `PathSegment`, `Violation`, `ValidationReport`, `ValidationOptions`, `ValidationTarget`. Failing-first test on `RuleId.Prefix` extraction. Pure value types only, no behavior yet.
2. **Stand up `RuleContext` pre-pass** — `ComponentsById`, `FeaturesById`, `CustomTablesByName`, `FeatureWalk`. Failing-first test asserting correctness of each index against a known package. `RuleContext.ForTest` factory for test usage.
3. **Stand up `RuleRegistry`** — construction, `Find`, `Without`, `WithAdded`, `OverrideSeverity`. Failing-first tests for each operation.
4. **Stand up `ValidationEngine.Run` skeleton** — takes an empty registry, returns an empty report. Failing-first test that a clean model produces a valid empty report.
5. **Port rules one at a time** — start with `PKG001_NameRequired`. Write failing rule-isolated test against `RuleContext.ForTest` with a violating model, implement the static field, green, commit. Then the matching happy-path test. Repeat for PKG002-011.
6. **Port collection-iteration rules** — `SVC001-011`, `REG001-007`, `SHC`, `FAS`, etc. Same TDD flow, one rule per commit.
7. **Port tree-walking rule** (FEA002) using `ctx.FeatureWalk` — failing-first test with nested duplicate IDs, assert violation fires at correct `ModelPath`.
8. **Port cross-table rule** (new `FEA010_FeatureComponentRef`) using `ctx.ComponentsById` — failing-first test with orphan ref, assert violation.
9. **Port complex rules** — `CTB007` (nested iteration with schema lookup), `REG007` (regex via `GeneratedRegex`). Each is one commit.
10. **Port patch/merge/transform rules** into their per-target static classes. Build `CoreRuleCatalog.Patch`, `CoreRuleCatalog.MergeModule`, `CoreRuleCatalog.Transform` as filter expressions.
11. **Wire `ModelValidator.Check` facade** — calls `ValidationEngine.Run` with `CoreRuleCatalog.Package`, collapses to `Result<Unit>` via `ValidationReport.ToResult()`. Failing-first test for cached-success reference equality on a clean model.
12. **Wire `ModelValidator.Inspect` facade** — same engine call, returns the full report.
13. **Wire extension rule merge** — add `GetValidationRules()` default to `IFalkForgeExtension`, extend `Installer.Build()` setup to merge extension rules into the catalog. Failing-first test using a stub extension.
14. **Migrate extensions** — Firewall, IIS, SQL, Dependency each implement `GetValidationRules()` returning their rules. Delete the parallel validator silos and custom error types.
15. **Migrate 5 call sites** — `MsiCompiler.Compile`, `ValidateCommand`, `ScriptLoader.Load`, `Installer.Build`, test wrapper. One caller per commit.
16. **Delete legacy** — `ModelValidator.cs` god class, `PatchValidator.cs`, `MergeModuleValidator.cs`, `TransformValidator.cs`, `IExtensionValidator.cs`, `ValidationResult.cs` (replaced by `ValidationReport`), old 345-LOC `ModelValidatorTests.cs`. One cleanup commit.
17. **Wire CLI `--list-rules`, `--explain`, `--ignore`, `--warn-as-error`** — pure registry operations, one commit per flag.
18. **Documentation** — update `docs/` with the rules-as-data architecture and contribute-a-new-rule guide.

Each phase gets its own implementation plan file under `docs/plans/`, paired with this design document.
