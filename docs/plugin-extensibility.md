# FalkForge Plugin & Extension Authoring Guide

FalkForge ships two distinct extensibility surfaces:

1. **Compile-time extensions** — `IFalkForgeExtension`, defined in `FalkForge.Extensibility`. Run inside the MSI compilation pipeline (`MsiAuthoring.Compile`). Contribute MSI table rows, validators, components, dry-run actions, and dialog steps. Seven first-party extensions ship: Util, Dependency, Firewall, DotNet, Iis, Sql, Driver, Http.
2. **Runtime plugins** — `IInstallerPlugin`, defined in `FalkForge.Plugins` (under `FalkForge.Core/Plugins/`). Run inside the WPF UI process. Provide service implementations (folder browsers, ODBC managers, SQL discovery) accessed via `IPluginServices.GetService<T>()`. Three first-party plugins ship: Sql, Odbc, FileSystem.

The two surfaces are **independent**. Extensions never run inside the Engine or UI processes. Plugins never run inside the compiler. This document covers both, in that order.

---

## Part 1 — Compile-Time Extensions

### Contract: `IFalkForgeExtension`

```csharp
public interface IFalkForgeExtension
{
    string Name { get; }
    string Version => "0.0.0";              // default
    string MinHostVersion => "0.0.0";       // default = compatible with any host
    void Register(IExtensionRegistry registry);

    // Returns validation rules contributed by this extension.
    // Default: empty. Override to add extension-specific diagnostics.
    ImmutableArray<ValidationRule> GetValidationRules() => [];
}
```

`Name` is the unique identity used for duplicate detection. `Version` and `MinHostVersion` use dotted-numeric SemVer (`major.minor.patch`); pre-release suffixes after `-` are stripped before comparison. The `Register` method is called once during compilation and is the only entry point — extensions cannot subscribe to events or hold long-lived state across compilations beyond their own instance fields.

The current host contract version is `ExtensionRegistration.CurrentHostVersion = "1.0.0"`.

### Contract: `IExtensionRegistry`

```csharp
public interface IExtensionRegistry
{
    void RegisterTableContributor(IMsiTableContributor contributor);
    void RegisterComponentContributor(IComponentContributor contributor);
    void RegisterDryRunContributor(IDryRunContributor contributor);
    void RegisterDialogStep(IDialogStepBuilder builder);
}
```

The registry is short-lived: a fresh instance is created per `MsiAuthoring.Compile` call (`CollectingExtensionRegistry` in `Recipe/MsiAuthoring.cs`). It is **not** explicitly frozen; instead, lifetime is bounded by the compile call. Callers do not need to call `Freeze()` because the registry is discarded once `Register(...)` has been invoked on every extension and the collected contributions have been drained into the recipe pipeline.

> Note: an `IPluginServiceRegistry.Freeze()` method exists, but on the **runtime plugin** registry (Part 2), not on `IExtensionRegistry`.

### Contribution Interfaces

- **`IMsiTableContributor`** — produces `IReadOnlyList<MsiTableRow>` for a specific MSI table (`TableName`). Rows are dictionaries of column-name → value. `MsiAuthoring`'s `CollectingExtensionRegistry` collects registered table contributors and routes them through `MsiRecipeBuilder` → `ExtensionTableEmitter` so their rows reach the compiled MSI. Two modes, chosen by table name:
  - **Custom table** (name is not a built-in MSI table, e.g. `SqlDatabase`, `WixFirewallException`): the emitter issues `CREATE TABLE` from the contributor's **`WriteColumns`** schema and inserts the rows. A contributor that yields rows for a custom table with a `null`/empty `WriteColumns` **fails the build loudly** (`EXT001`) rather than silently dropping the rows.
  - **Built-in table** (name matches a table the fixed pipeline already produces, e.g. `CustomAction`, `Registry`): the rows are mapped against that table's known columns and merged into it; `WriteColumns` is ignored.

  Contributor order, row order, and column-declaration order are all authoritative, so emitted tables are deterministic for reproducible builds. Table and column identifiers are re-validated against the MSI identifier grammar before any SQL is built.

  `IMsiTableContributor` carries an optional **`ReadSchema`** property (`ITableReadSchema?`, default `null`) added in Cycle 4. When non-null, `MsiDecompiler` reads the contributor's custom table during decompile and stores rows in `MsiReadRecipe.ExtensionRows`. Without `ReadSchema`, custom tables are silently skipped on decompile. All four first-party contributors (Firewall, IIS, SQL, Dependency) populate `ReadSchema`. See `docs/decompile-pipeline.md` — "Adding extension read schemas" — for the implementation walkthrough.

  It also carries an optional **`WriteColumns`** property (`IReadOnlyList<ContributedColumn>?`, default `null`) — the write-side schema described above. Required for custom tables; ignored for built-in tables.

- **`IComponentContributor`** — `GetAdditionalFiles(ExtensionContext)` returns `FileEntryModel`s to merge into the package's component set. The registry now **collects** these (no longer an empty body), but the recipe pipeline does not yet emit contributed components; when any are registered, `MsiAuthoring` logs a `Warning` (`EXT002`) so the drop is not silent. Full component-file merge is a follow-up.

- **`IDryRunContributor`** — `GetDryRunActions(DryRunIntent)` returns descriptive `DryRunAction`s for `Install` / `Uninstall` / `Repair`. Used by callers that want a human-readable summary of side effects. The default `CollectingExtensionRegistry` accepts but discards them; concrete shipped extensions implement `IDryRunContributor` directly on the extension class so callers can iterate `extensions.OfType<IDryRunContributor>()`.

- **`IDialogStepBuilder`** — marker interface with a `Name`. Builders registered here are added to `DialogStepRegistry`, suppressing DLG001 ("unknown step") errors when `DialogCustomization.InsertStep(name, after:)` references them. Extensions needing to emit a full `MsiDialogModel` implement `IMsiDialogStepBuilder` (in `FalkForge.Compiler.Msi`) which extends this marker. See `docs/dialog-template-architecture.md` — "Extension Dialog Step Contribution" — for a full worked example including `DialogComposer.Compose` usage and the `InsertStep` call-site pattern.

### `ExtensionContext`

```csharp
public sealed class ExtensionContext
{
    public required PackageModel Package { get; init; }
    public required string OutputDirectory { get; init; }
    public required string SourceDirectory { get; init; }
}
```

Read-only snapshot passed into validators and component contributors. `OutputDirectory` and `SourceDirectory` are absolute paths.

### Activation Order

1. Caller passes `IReadOnlyList<IFalkForgeExtension> extensions` to `MsiCompiler` (or directly to `MsiAuthoring.Compile`). The list defines the **exact order** of registration.
2. `MsiAuthoring.Compile` runs `ModelValidator.Inspect(package)` (built-in package validation) **before** any extension code executes.
3. For each extension, in list order, the compiler calls `ExtensionRegistration.Register(extension, registry, registeredNames)` which:
   - Throws `PluginCompatibilityException` if `Name` is null/empty.
   - Throws `PluginCompatibilityException` if `Name` is already in `registeredNames`.
   - Throws `PluginCompatibilityException` if `MinHostVersion` is greater than `CurrentHostVersion` (`1.0.0`).
   - Otherwise calls `extension.Register(registry)` and adds `Name` to `registeredNames`.
4. After all extensions have registered, the compiler drains the registry:
   - Extension validation rules (contributed via `GetValidationRules()`) are merged into `ModelValidator` and run alongside core rules. Violations aggregate across all rules; any `Error`-severity violation fails the compile.
   - Dialog step builders feed `DialogStepRegistry` for DLG001 validation.
5. Recipe producers run (`MsiRecipeBuilder` → `IMultiTableProducer` chain), and registered `IMsiTableContributor` rows are routed through `ExtensionTableEmitter` (custom tables created from `WriteColumns`, built-in tables merged). Extensions cannot mutate the registry after `Register` has returned, because the registry instance leaves scope.

The entire activation sequence is **synchronous and single-threaded**.

### Duplicate `Name` Behaviour

`ExtensionRegistration.Register` enforces **fail-fast on duplicate `Name`** by throwing `PluginCompatibilityException`. This is stricter than the "first-registration-wins" behaviour used by the runtime plugin registry (Part 2).

> Historical CLAUDE.md text described "first-registration-wins" for the extension surface. The current code in `ExtensionRegistration.cs` (lines 50–54) **rejects** duplicates instead. If you need fall-through behaviour, route registrations through your own `IExtensionRegistry` implementation rather than `ExtensionRegistration.Register`.

### Host-Version Compatibility

`MinHostVersion` lets an extension declare the lowest contract version it needs. Comparison is dotted-numeric, missing components are treated as zero (`"1"` == `"1.0.0"`), pre-release suffixes are stripped. Extensions that do not set `MinHostVersion` are accepted on any host.

### NativeAOT Compatibility

The MSI compiler currently runs in JIT contexts (CLI: `forge build`, custom hosts). NativeAOT publish targets are the **Engine** and **Engine.Elevation** processes, which **do not host extensions**. Therefore:

- Extensions that load only into the compiler (the normal case) face **no AOT constraints**.
- Reflection, dynamic assembly load, `BinaryFormatter`, source-generated JSON contexts not registered with the host — all permitted.
- `<TBD>` — there is no documented scenario where a third-party extension is loaded into the AOT-published Engine. If this changes, AOT trim-warnings would surface at compile time.

### Sandbox Model

There is no plugin sandbox. Extensions execute in-process with full trust:

- They run inside the build host process.
- They share the file system and environment of the build agent.
- A faulting extension throws an exception that aborts the compilation.
- A malicious extension can read any file the build host can read and write any path the host can write.

Treat extensions as part of the trusted build supply chain. Pin extension package sources (private NuGet feed, signed packages, lockfile review). Audit the `IFalkForgeExtension` implementations referenced by your build the same way you audit MSBuild tasks.

### No Runtime Discovery / Hot Loading

Extensions are **compile-time references only**. The CLI does not scan plugins from disk, the application directory, or any user-configurable path. To activate an extension you must:

1. Add a `<PackageReference>` (or `<ProjectReference>`) to your build script's project.
2. Instantiate the extension type and pass it to the `MsiCompiler` ctor (or to your build host's extension list).

Removing an extension requires a rebuild.

### Shipped First-Party Extensions

| Extension                | Type                                | Contributes                                         | Error codes |
|--------------------------|-------------------------------------|------------------------------------------------------|-------------|
| `UtilExtension`          | `FalkForge.Extensions.Util`         | XmlConfig, ScheduledTask, PerfCounter, OdbcDriver, OdbcDataSource table contributors; `IDryRunContributor` | XCF001-009  |
| `DependencyExtension`    | `FalkForge.Extensions.Dependency`   | Dependency provider/consumer table contributor (HKLM registry rows) | DEP001-007  |
| `FirewallExtension`      | `FalkForge.Extensions.Firewall`     | Firewall rule rows                                  | FWL001-004  |
| `DotNetExtension`        | `FalkForge.Extensions.DotNet`       | .NET runtime detection (registry + filesystem search rows) | NET001-003  |
| `IisExtension`           | `FalkForge.Extensions.Iis`          | AppPool, WebSite, WebBinding, Certificate rows      | IIS001-011  |
| `SqlExtension`           | `FalkForge.Extensions.Sql`          | Database, script, string-execution rows             | SQL001-013  |
| `DriverExtension`        | `FalkForge.Extensions.Driver`       | Driver install/uninstall rows                       | `<TBD>`     |
| `HttpExtension`          | `FalkForge.Extensions.Http`         | HTTP-server URL reservation, SSL binding rows       | `<TBD>`     |

### Authoring Walkthrough

A minimal extension that contributes validation rules via `GetValidationRules()`:

```csharp
using System.Collections.Immutable;
using FalkForge.Extensibility;
using FalkForge.Validation;

public sealed class MyOrgExtension : IFalkForgeExtension
{
    public string Name => "MyOrg.FalkForge.Extensions.AuditTrail";
    public string Version => "1.2.0";
    public string MinHostVersion => "1.0.0";

    public void Register(IExtensionRegistry registry)
    {
        // Contribute table rows, dry-run actions, dialog steps here.
        // Validation rules are contributed separately via GetValidationRules().
    }

    public ImmutableArray<ValidationRule> GetValidationRules() =>
    [
        ValidationRule.Single(
            new RuleId("AT001"),
            Severity.Error,
            ModelSection.Package,
            "Manufacturer required by AuditTrail",
            "AuditTrail extension requires Manufacturer to be set for compliance logging.",
            static ctx => string.IsNullOrEmpty(ctx.Package.Manufacturer)
                ? new Violation(new RuleId("AT001"), Severity.Error,
                    ModelPath.Root.Field("Manufacturer"),
                    "AuditTrail requires Manufacturer to be set.")
                : null),
    ];
}
```

Rules returned by `GetValidationRules()` are merged into the `ModelValidator` singleton registry during `MsiAuthoring.Compile`. They appear in `forge rules list` output, fire during `forge validate`, and are individually suppressible with `--ignore AT001`. See `docs/rules-as-data-architecture.md` for the full rule-authoring guide.

Activation in a build host. The discoverable, fluent way is `MsiCompiler.Use(...)`:

```csharp
var util = new UtilExtension();
// ... configure util ...

// .Use(...) attaches the extension so its tables emit. It mutates and returns the
// same compiler, so even a discarded result still attaches — you cannot accidentally
// ship an installer that silently omits the extension.
return Installer.Build(args, package => { /* ... */ },
    new MsiCompiler().Use(util));
```

Multiple extensions attach in one call (`new MsiCompiler().Use(a, b, c)`) or by chaining
(`.Use(a).Use(b)`). The constructor form is equivalent:

```csharp
var compiler = new MsiCompiler(
    new WindowsFileSystem(),
    new IFalkForgeExtension[]
    {
        new UtilExtension(),
        new MyOrgExtension(),
    });

Result<string> msiPath = compiler.Compile(package, outputDirectory);
```

### Reference: `UtilExtension.Register`

Concrete shipped flow (from `src/FalkForge.Extensions.Util/UtilExtension.cs`):

```csharp
public void Register(IExtensionRegistry registry)
{
    registry.RegisterTableContributor(_xmlConfigContributor);
    registry.RegisterTableContributor(_scheduledTaskContributor);
    registry.RegisterTableContributor(_perfCounterContributor);
    registry.RegisterTableContributor(_odbcDriverContributor);
    registry.RegisterTableContributor(_odbcDataSourceContributor);
}
```

The five contributors are pre-allocated on the extension instance. The extension also implements `IDryRunContributor` directly so callers iterating `extensions.OfType<IDryRunContributor>()` pick up its actions.

---

## Part 2 — Runtime Plugins

### Contract: `IInstallerPlugin`

```csharp
public interface IInstallerPlugin
{
    string Name { get; }
    void RegisterServices(IPluginServiceRegistry registry);
}
```

Plugins live in the **UI process** (and any custom host that builds an `IPluginServices`). They expose service implementations addressed by interface type (`ISqlServerDiscovery`, `IFolderBrowser`, `IOdbcManager`, …). They never see `PackageModel`, never run during compilation, and never participate in MSI table emission.

### Contracts: `IPluginServiceRegistry` / `IPluginServices`

```csharp
public interface IPluginServiceRegistry
{
    void Register<TService>(TService instance) where TService : class;
    void Register<TService>(Func<TService> factory) where TService : class;
}

public interface IPluginServices
{
    TService? GetService<TService>() where TService : class;
    TService GetRequiredService<TService>() where TService : class;
}
```

The shipped implementation `PluginServiceRegistry` implements both interfaces. It uses `Dictionary<Type, Func<object>>.TryAdd` for registration, which gives **first-registration-wins** semantics — subsequent `Register<T>` calls for the same `T` are silently ignored.

### `Freeze()`

`PluginServiceRegistry.Freeze()` flips an internal `_frozen` flag. After freezing, any further `Register<T>` call throws `InvalidOperationException("Plugin registry is frozen after initialization.")`. Resolution (`GetService<T>` / `GetRequiredService<T>`) is unaffected. Hosts call `Freeze()` once startup composition is complete.

### Activation Order

1. Host code constructs concrete plugin instances (compile-time references — there is no disk scanning).
2. Optional: wrap them in `PluginRegistry.Create(plugin1, plugin2, …)`. This is an immutable, ordered collection that exposes `RegisterAll(IPluginServiceRegistry)`.
3. `RegisterAll` iterates the array in order, calling `RegisterServices` on each plugin. First-registration-wins is preserved by `Dictionary.TryAdd`.
4. Host calls `serviceRegistry.Freeze()`.
5. UI code resolves services via `IPluginServices.GetService<T>()`.

`PluginRegistry.PluginNames` exposes the names in registration order for diagnostics.

### Duplicate Service-Type Behaviour

If two plugins register the same service type, the **earlier plugin wins** (because `TryAdd` does not overwrite). No exception, no warning. Authors who want to override a default plugin must register their plugin first in the `PluginRegistry.Create(...)` argument list.

> This is intentionally different from `IExtensionRegistry`: extensions are identified by `Name` and duplicate names throw; plugins are identified by service type and duplicate types are silently ignored.

### NativeAOT Compatibility

Plugins are referenced by the **UI process**, which is WPF + ReactiveUI on the JIT runtime. AOT constraints do not apply to the shipped plugins. Plugins that load into a custom AOT-published host would be subject to the standard AOT rules: no reflection, no dynamic loading, no `BinaryFormatter`. The Engine and Engine.Elevation processes do **not** host plugins.

### Sandbox Model

No sandbox. Plugins run in-process with full trust inside the UI process. They have access to the WPF dispatcher, the file system, and any installer state held by the shell view-model.

### No Runtime Discovery / Hot Loading

Plugins are referenced at compile time. There is no `plugins/` folder scan, no MEF, no `Assembly.LoadFrom`. To activate a plugin: add a project reference, instantiate it, hand it to `PluginRegistry.Create` or directly to `serviceRegistry`.

### Shipped First-Party Plugins

| Plugin               | Project                         | Services                                                          | Platform |
|----------------------|---------------------------------|-------------------------------------------------------------------|----------|
| `SqlPlugin`          | `FalkForge.Plugins.Sql`         | `ISqlServerDiscovery`, `IDatabaseLister`, `IConnectionTester`     | any      |
| `OdbcPlugin`         | `FalkForge.Plugins.Odbc`        | `IOdbcManager` (DSN check, admin launcher)                        | Windows  |
| `FileSystemPlugin`   | `FalkForge.Plugins.FileSystem`  | `IFolderBrowser` (WPF folder dialog)                              | Windows  |

### Authoring Walkthrough

A minimal plugin contributing a single service:

```csharp
using FalkForge.Plugins;

public sealed class MyOrgPlugin : IInstallerPlugin
{
    public string Name => "MyOrg.FalkForge.Plugins.LicenseLookup";

    public void RegisterServices(IPluginServiceRegistry registry)
    {
        registry.Register<ILicenseLookup>(new LicenseLookupService());
    }
}
```

Composition (typical UI host):

```csharp
var serviceRegistry = new PluginServiceRegistry();

PluginRegistry.Create(
    new MyOrgPlugin(),       // earlier = wins on duplicate service type
    new SqlPlugin(),
    new OdbcPlugin(),
    new FileSystemPlugin())
  .RegisterAll(serviceRegistry);

serviceRegistry.Freeze();

// Resolution from a view model:
var lookup = serviceRegistry.GetRequiredService<ILicenseLookup>();
```

---

## Summary Table

| Aspect                 | Compile-time `IFalkForgeExtension`                       | Runtime `IInstallerPlugin`                          |
|------------------------|----------------------------------------------------------|-----------------------------------------------------|
| Identity               | `Name` (string)                                          | Service type (`TService`)                           |
| Duplicate handling     | Throws `PluginCompatibilityException`                    | First-registration-wins, silent                     |
| Freeze semantics       | Implicit (registry scoped to one `Compile` call)         | Explicit `PluginServiceRegistry.Freeze()`           |
| Host process           | Build host (compiler)                                    | UI (WPF/ReactiveUI) process                         |
| AOT compatibility      | N/A — compiler is JIT                                    | N/A — UI is JIT                                     |
| Sandbox                | None — full trust in build host                          | None — full trust in UI process                     |
| Hot loading            | Not supported                                            | Not supported                                       |
| Compile-time reference | Required                                                 | Required                                            |
| Host-version gate      | `MinHostVersion` vs `CurrentHostVersion = "1.0.0"`       | None                                                |

---

## Open Items (cat-21 hardening)

- `IMsiTableContributor` rows are now emitted into the compiled MSI (custom tables from `WriteColumns`, built-in tables merged). Remaining follow-ups: `IComponentContributor` is collected but its `GetAdditionalFiles` output is not yet merged (surfaced as `EXT002` Warning); the `ODBCDriver`/`ODBCDataSource` Util contributors have no `WriteColumns` yet (they are standard MSI tables needing their exact schema, and fail loud via `EXT001` if populated); IIS emits its configuration tables but install-time execution via `Microsoft.Web.Administration`, certificate emission, and a multi-binding table are not yet implemented.
- No telemetry surfaces the active extension/plugin list at build / install time. Logging the `Name` + `Version` set on startup would simplify support triage.
- No signature verification of extension assemblies. Trust is delegated to the NuGet supply chain.
- No mechanism for an extension to declare a maximum host version (`MaxHostVersion`). Forward-incompatible breaks require coordinating a `Version` bump across all extensions.
