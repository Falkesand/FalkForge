# RFC: Deepen Decompiler TableReaders into a recipe-symmetric pipeline

**Status:** Design accepted, implementation plan pending
**Author:** architectural review, 2026-04-11
**Scope:** `src/FalkForge.Decompiler/`, `src/FalkForge.Extensibility/`, `src/FalkForge.Testing/`, `tests/FalkForge.Decompiler.Tests/`
**Depends on:** `2026-04-11-tableemitter-recipe-deepening-design.md` (Cycle 2) — reuses `MsiDatabaseRecipe` and related value types

## Problem

`src/FalkForge.Decompiler/TableReaders/` contains 9 static classes (`PropertyTableReader`, `DirectoryTableReader`, `ComponentTableReader`, `FileTableReader`, `FeatureTableReader`, `RegistryTableReader`, `ServiceTableReader`, `ShortcutTableReader`, `UpgradeTableReader`), totaling 696 LOC. Each implements the same 4-step pattern with copy-pasted structure: check `tableAccess.TableExists(name)`, call `tableAccess.QueryTable(name)` returning `IReadOnlyList<IReadOnlyList<string>>`, iterate rows and hand-parse columns by positional index (`row[0]`, `row[1]`) with `int.TryParse` fallback for numeric columns, construct a typed domain entry per row, wrap errors with a hardcoded `"DEC00X: ..."` code.

The testability consequence is that zero isolated reader tests exist today. `IMsiTableAccess` is already the abstraction seam over the msi.dll query path — its production implementation `MsiTableAccess` wraps the native calls, and a `FakeMsiTableAccess` backed by an in-memory dict would be roughly 40 LOC — but nobody has written one because each reader is small enough that the effort-to-test-coverage ratio discourages it. Bugs surface only in `MsiCompilerIntegrationTests` through the real msi.dll read path, making diagnosis slow and platform-locked to Windows CI.

The latent bug class is column index swapping. `FileTableReader` reads `File.Component_` at index 1 and `File.FileName` at index 2 — if someone copy-pastes `FileTableReader` as the template for a new `IniFileTableReader` and forgets to update the indices, the new reader produces silently-wrong domain entries because nothing catches positional-vs-declared mismatches. There is no schema declaration, no type safety at the query layer, no validation that the row cell count matches what the reader expects.

The extension consequence is a round-trip bug exactly symmetric to the Firewall contributor half-wiring from Cycle 2. Extensions (Firewall, IIS, SQL, Dependency) emit custom tables during compile via `IMsiTableContributor`. Cycle 2's RFC adds an optional `Schema` property to that interface so extensions can declare both the schema and the rows they contribute. But the decompile path has no corresponding mechanism: if you compile an MSI with the Firewall extension and then decompile it, the `WixFirewallException` table is silently skipped because no reader knows how to read it. Decompile of extension-authored packages is lossy today.

The platform consequence is that every decompile test must run on Windows with real msi.dll. `MsiDecompiler.Decompile(path)` orchestrates all 9 readers sequentially through a real `MsiTableAccess`. Tests that want to assert "after decompiling this MSI, the Property table contains these rows" have to stand up an integration test with a real input file. The `IMsiTableAccess` abstraction is the seam that could unblock cross-platform testing — but without a `FakeMsiTableAccess` implementation and without a schema-driven reader engine that could be unit-tested against it, the seam goes unused.

The Cycle 2 consequence is that the build and decompile paths have no shared vocabulary. Cycle 2 introduces `MsiDatabaseRecipe` as the hero immutable value type describing every table, row, cell, and stream in an MSI being built. The decompile path today produces a `PackageModel` directly, bypassing any intermediate representation. This means no round-trip tests (can't decompile an MSI, then recompile the result, then compare byte-for-byte against the original), no shared type vocabulary (`RecipeTable`, `RecipeRow`, `CellValue`, `StreamSource`, `TableId`, `RecipeColumn` exist only on the build side — the read side invents its own per-table DTOs), and no reuse of FK validation (Cycle 2's `ForeignKeySpec` declarations validate the write side; the read side has no equivalent).

This is a shallow-modules problem compounded by a missing testability seam (test adapter for `IMsiTableAccess` doesn't exist), a missing extension integration point (no decompile-side equivalent to the Cycle 2 `Schema` property), and a missing shared vocabulary (the read path ignores the recipe types). Deepening means splitting the decompile pipeline into two pure stages — `MsiRecipeReader` (thin Windows-only P/Invoke wrapper around `IMsiTableAccess`) and `MsiPackageReconstructor` (pure cross-platform function from recipe to `PackageModel`) — reusing every Cycle 2 value type, collapsing the 9 copy-pasted readers into 9 declarative `TableReadSchema` records, and adding a `ReadSchema` optional property to `IMsiTableContributor` symmetric to Cycle 2's `Schema` property.

## Proposed Interface

The design splits the decompile pipeline into two stages that mirror Cycle 2's build pipeline exactly. The build flow is `PackageModel → ComponentResolver → ResolvedPackage → MsiRecipeBuilder → MsiDatabaseRecipe → MsiRecipeExecutor → .msi file`. The decompile flow becomes `.msi file → IMsiTableAccess → MsiRecipeReader → MsiDatabaseRecipe → MsiPackageReconstructor → PackageModel`. The same `MsiDatabaseRecipe` value type is the central intermediate, making round-trip tests trivial: decompile an MSI to a recipe, recompile the recipe to a new MSI, compare the recipes (or the files).

### Public facade — call site preserved

```csharp
namespace FalkForge.Decompiler;

/// <summary>
/// Existing public API preserved. CLI 'forge decompile' and any downstream
/// caller continues to receive Result&lt;PackageModel&gt; with zero churn.
/// </summary>
public sealed class MsiDecompiler
{
    public Result<PackageModel> Decompile(string msiPath);

    /// <summary>
    /// New — round-trip test entry point. Returns the raw MsiDatabaseRecipe
    /// without running the reconstructor stage. Used by regression tests
    /// that want to compare recipes byte-for-byte against a Cycle 2 build.
    /// </summary>
    public Result<MsiDatabaseRecipe> DecompileToRecipe(string msiPath);
}
```

Real `MsiDecompiler.Decompile` body shrinks to a short forwarder:

```csharp
public Result<PackageModel> Decompile(string msiPath)
{
    using var access = MsiTableAccess.Open(msiPath).Value;
    return MsiRecipeReader.Read(access)
        .Bind(recipe => MsiPackageReconstructor.Rebuild(recipe));
}
```

### Stage 1 — `MsiRecipeReader` (Windows-only, thin P/Invoke wrapper)

```csharp
namespace FalkForge.Decompiler.Recipe;

/// <summary>
/// Reads an MSI database through an IMsiTableAccess abstraction and produces
/// an immutable MsiDatabaseRecipe. Symmetric inverse of Cycle 2's
/// MsiRecipeExecutor. Windows-only because msi.dll is the only production
/// backing for IMsiTableAccess, but the reader itself only touches the
/// abstraction — tests substitute a FakeMsiTableAccess and run on any OS.
///
/// Delegates per-table row mapping to TableReadSchema records registered
/// in a FrozenDictionary lookup by table name. Unknown tables are skipped
/// silently. Schema shape mismatches return structured errors naming the
/// table, expected column count, and actual column count.
/// </summary>
[SupportedOSPlatform("windows")]
public static class MsiRecipeReader
{
    public static Result<MsiDatabaseRecipe> Read(IMsiTableAccess access);
    public static Result<MsiDatabaseRecipe> Read(IMsiTableAccess access, TableReadRegistry registry);
}
```

### Stage 2 — `MsiPackageReconstructor` (pure cross-platform)

```csharp
namespace FalkForge.Decompiler.Recipe;

/// <summary>
/// Pure function converting an MsiDatabaseRecipe back into a PackageModel
/// suitable for the legacy MsiDecompiler.Decompile call site. Symmetric
/// inverse of ComponentResolver (which goes PackageModel to ResolvedPackage
/// on the build side — the reconstructor skips the ResolvedPackage stage
/// because the recipe already carries resolved data).
///
/// Cross-platform. Zero msi.dll touches. Unit-testable on Linux CI by
/// hand-building a minimal MsiDatabaseRecipe and asserting on the resulting
/// PackageModel.
/// </summary>
public static class MsiPackageReconstructor
{
    public static Result<PackageModel> Rebuild(MsiDatabaseRecipe recipe);
}
```

### Rules-as-data — `TableReadSchema` record

```csharp
namespace FalkForge.Decompiler.Recipe;

/// <summary>
/// Immutable description of how to read one MSI table into recipe rows.
/// One static readonly field per table in a per-area class. Declares
/// column schema, error diagnostic code, and a pure row mapper delegate.
/// Symmetric to Cycle 2's ITableProducer on the build side.
/// </summary>
public sealed record TableReadSchema<TRow>(
    TableId Table,
    ImmutableArray<ReadColumn> Columns,
    RowMapper<TRow> Map,
    string DiagnosticCode = "DEC003") : ITableReadSchema;

public interface ITableReadSchema
{
    TableId Table { get; }
    ImmutableArray<ReadColumn> Columns { get; }
    string DiagnosticCode { get; }
    Result<ImmutableArray<RecipeRow>> ReadErased(IMsiTableAccess access);
}

/// <summary>
/// Column descriptor reusing Cycle 2's RecipeColumn plus an explicit Index
/// for byte-identical round-trip. The Index is usually the position in the
/// Columns array, but stating it explicitly lets schemas declare columns
/// out of positional order and lets the read/write sides share column
/// constants.
/// </summary>
public readonly record struct ReadColumn(RecipeColumn Column, int Index)
{
    public string Name => Column.Name;
    public ColumnType Type => Column.Type;
    public bool Nullable => Column.Nullable;
}

public delegate Result<TRow> RowMapper<TRow>(ReadRow row);

/// <summary>
/// Zero-allocation view over one raw row, bound to the schema. Ref struct
/// so it cannot escape the mapper. Column access is type-safe via ReadColumn
/// tokens — no row[0], no row["name"], no stringly-typed lookups.
/// </summary>
public readonly ref struct ReadRow
{
    internal ReadRow(ReadOnlySpan<string?> cells, TableReadContext context);

    public string String(ReadColumn col);
    public string? StringOrNull(ReadColumn col);
    public int Int32(ReadColumn col);
    public int? Int32OrNull(ReadColumn col);
    public long Int64(ReadColumn col);
    public TEnum Enum<TEnum>(ReadColumn col) where TEnum : struct, Enum;

    /// <summary>
    /// Resolves a stream column to a lazy StreamSource. Never loads bytes.
    /// The returned StreamSource defers to IMsiStreamAccess.OpenStream at
    /// use time.
    /// </summary>
    public StreamSource Stream(ReadColumn col);
}
```

### Registry

```csharp
namespace FalkForge.Decompiler.Recipe;

/// <summary>
/// Immutable registry of read schemas keyed by table name. Frozen after
/// construction. Extensions contribute via With(...).
/// </summary>
public sealed class TableReadRegistry
{
    public static TableReadRegistry BuiltIn();

    public TableReadRegistry With(IEnumerable<ITableReadSchema> extra);
    public ITableReadSchema? TryGet(string tableName);
    public IReadOnlyCollection<ITableReadSchema> All { get; }
}

public static class BuiltInReadSchemas
{
    public static IEnumerable<ITableReadSchema> Enumerate();
}
```

### Example schema — `ComponentSchema`

```csharp
namespace FalkForge.Decompiler.Recipe.Schemas;

public static class ComponentSchema
{
    public static readonly ReadColumn Component   = new(new("Component",   ColumnType.String, false), 0);
    public static readonly ReadColumn ComponentId = new(new("ComponentId", ColumnType.String, true),  1);
    public static readonly ReadColumn Directory_  = new(new("Directory_",  ColumnType.String, false), 2);
    public static readonly ReadColumn Attributes  = new(new("Attributes",  ColumnType.Int32,  false), 3);
    public static readonly ReadColumn Condition   = new(new("Condition",   ColumnType.String, true),  4);
    public static readonly ReadColumn KeyPath     = new(new("KeyPath",     ColumnType.String, true),  5);

    public static readonly TableReadSchema<ComponentRecipeRow> Schema = new(
        Table:   TableId.Component,
        Columns: [Component, ComponentId, Directory_, Attributes, Condition, KeyPath],
        Map:     row => new ComponentRecipeRow(
                     row.String(Component),
                     row.StringOrNull(ComponentId),
                     row.String(Directory_),
                     row.Int32(Attributes),
                     row.StringOrNull(Condition),
                     row.StringOrNull(KeyPath)));
}
```

Every other built-in schema follows the same shape. Nine schemas total, approximately 20 LOC each, versus the current 60-90 LOC per reader. Total read-side LOC drops from 696 to approximately 180.

### Extension integration — symmetric to Cycle 2 `Schema`

```csharp
namespace FalkForge.Extensibility;

public interface IMsiTableContributor
{
    string TableName { get; }
    IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context);

    /// <summary>
    /// Added in Cycle 2 — write-side schema declaration. Populating this
    /// fixes the Firewall WixFirewallException schema-missing bug.
    /// </summary>
    TableSchema? Schema => null;

    /// <summary>
    /// Added in Cycle 4 — read-side schema for the decompile path.
    /// Populating this lets the extension's custom table round-trip
    /// through decompile. Without it, the table is silently skipped.
    /// </summary>
    ITableReadSchema? ReadSchema => null;
}
```

Firewall extension fix:

```csharp
public sealed class FirewallTableContributor : IMsiTableContributor
{
    public string TableName => "WixFirewallException";
    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context) => /* ... */;
    public TableSchema? Schema => FirewallTableSchemas.WixFirewallExceptionWriteSchema;
    public ITableReadSchema? ReadSchema => FirewallTableSchemas.WixFirewallExceptionReadSchema;
}
```

### What the deepened module owns

- `MsiRecipeReader.Read` as the thin Windows-only P/Invoke wrapper orchestrating per-table reads through `IMsiTableAccess`.
- `MsiPackageReconstructor.Rebuild` as the pure cross-platform function converting a recipe to `PackageModel`.
- 9 built-in `TableReadSchema` static records, one per table, each approximately 20 LOC.
- `ReadColumn` struct pairing a Cycle 2 `RecipeColumn` with an explicit index.
- `ReadRow` ref struct providing type-safe zero-allocation column access.
- `TableReadRegistry` immutable frozen-dictionary-backed lookup by table name.
- `TableReadEngine.ReadOne(schema, access)` helper for isolated per-schema unit tests.
- Extension integration via new `IMsiTableContributor.ReadSchema` optional property.
- `MsiDecompiler.DecompileToRecipe` new entry point for round-trip regression tests.

### What the deepened module hides

- All 9 copy-pasted `*TableReader` static classes. Deleted.
- Per-reader hardcoded `"DEC00X"` error code strings. Centralized in schema `DiagnosticCode` field.
- Positional row indexing (`row[0]`, `row[1]`, `int.TryParse`). Replaced by type-safe `ReadRow` accessors.
- Per-reader duplicated `TableExists` + `QueryTable` + `foreach` orchestration. Replaced by single engine loop.
- The domain DTO construction inside each reader. Moved into the separate `MsiPackageReconstructor` stage operating on the shared recipe.
- The hardcoded assumption that decompile produces only built-in tables. Replaced by the extensible registry.

## Dependency Strategy

This module has two distinct dependency profiles, matching the two-stage split.

### Stage 1 — `MsiRecipeReader` (Windows-only)

Consumes `IMsiTableAccess` as the sole external dependency. The abstraction already exists. Production uses `MsiTableAccess` backed by msi.dll P/Invoke through `NativeMethods.Msi`. Tests use a new `FakeMsiTableAccess` shipped in `FalkForge.Testing`:

```csharp
public sealed class FakeMsiTableAccess : IMsiTableAccess
{
    private readonly Dictionary<string, FakeTable> _tables = new();

    public FakeMsiTableAccess Add(string tableName, string[] columns, params string?[][] rows);

    public Result<bool> TableExists(string name);
    public Result<IReadOnlyList<IReadOnlyList<string?>>> QueryTable(string name);
    public void Dispose();
}
```

Per-schema unit tests look like:

```csharp
[Fact]
public void ComponentSchema_maps_full_row()
{
    var access = new FakeMsiTableAccess()
        .Add("Component",
            columns: ["Component", "ComponentId", "Directory_", "Attributes", "Condition", "KeyPath"],
            rows:
            [
                ["WixComp1", "{GUID-1}", "INSTALLFOLDER", "8", null, "file1.exe"],
                ["WixComp2", null, "INSTALLFOLDER", "0", "VersionNT>=600", null]
            ]);

    var result = TableReadEngine.ReadOne(ComponentSchema.Schema, access);

    Assert.True(result.IsSuccess);
    Assert.Equal(2, result.Value.Length);
    Assert.Equal("WixComp1", result.Value[0].ComponentName);
    Assert.Equal(8, result.Value[0].Attributes);
    Assert.Null(result.Value[0].Condition);
    Assert.Equal("VersionNT>=600", result.Value[1].Condition);
}
```

Zero msi.dll, runs on Linux CI.

### Stage 2 — `MsiPackageReconstructor` (pure cross-platform)

Consumes only `MsiDatabaseRecipe` (an immutable value type) and produces `PackageModel`. No `IMsiTableAccess`, no `IFileSystem`, no external dependencies at all. Tests construct a minimal recipe by hand:

```csharp
[Fact]
public void Rebuild_populates_PackageModel_Name_from_Property_table()
{
    var recipe = new MsiDatabaseRecipe
    {
        Tables = [
            new RecipeTable
            {
                Name = TableId.Property,
                Columns = /* ... */,
                Rows = [
                    new RecipeRow { Cells = [new CellValue.StringValue("ProductName"), new CellValue.StringValue("Acme")] },
                    new RecipeRow { Cells = [new CellValue.StringValue("Manufacturer"), new CellValue.StringValue("Acme Corp")] },
                    new RecipeRow { Cells = [new CellValue.StringValue("ProductVersion"), new CellValue.StringValue("1.2.3")] },
                ],
                /* ... */
            }
        ],
        SummaryInfo = /* ... */,
        Streams = ImmutableDictionary<string, StreamSource>.Empty,
        FileSequencing = ImmutableArray<FileSequenceEntry>.Empty,
        ContentHash = default,
    };

    var result = MsiPackageReconstructor.Rebuild(recipe);

    Assert.True(result.IsSuccess);
    Assert.Equal("Acme", result.Value.Name);
    Assert.Equal("Acme Corp", result.Value.Manufacturer);
}
```

Zero `IMsiTableAccess`, zero msi.dll, zero Windows requirement. Reconstructor is pure data transformation.

### Round-trip test story

Two flavors of round-trip test become possible, each catching a different class of drift.

**Recipe-equality round-trip (preferred, stable)**:

```csharp
[Theory]
[MemberData(nameof(DemoFixtures))]
public void Recipe_roundtrip_is_stable(string demoPath)
{
    var package = LoadDemoPackage(demoPath);
    var resolved = new ComponentResolver(new WindowsFileSystem()).Resolve(package).Value;
    var originalRecipe = MsiRecipeBuilder.Build(resolved, contributors: [], new MsiRecipeBuildOptions()).Value;

    var msiPath = Path.GetTempFileName() + ".msi";
    using var db = MsiDatabase.Create(msiPath).Value;
    new MsiRecipeExecutor(db).Apply(originalRecipe).ShouldBeSuccess();
    db.Commit().ShouldBeSuccess();

    using var access = MsiTableAccess.Open(msiPath).Value;
    var recompiledRecipe = MsiRecipeReader.Read(access).Value;

    Assert.Equal(originalRecipe.ContentHash, recompiledRecipe.ContentHash);
}
```

**Byte-identical .msi round-trip (strict, may need reproducible-timestamp guards)**:

```csharp
[Theory]
[MemberData(nameof(DemoFixtures))]
public void Msi_bytewise_roundtrip(string demoPath)
{
    var first = CompileDemo(demoPath);
    var recipe = new MsiDecompiler().DecompileToRecipe(first).Value;
    var second = RecompileFromRecipe(recipe);

    Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
}
```

Recipe-equality is the load-bearing regression test. Byte-identical is a stricter secondary test that may need guards for reproducible-timestamp drift.

### Extension flow

During `Installer.Build()` or `MsiDecompiler` construction, extensions implementing `IMsiTableContributor` are enumerated. Each contributor's `ReadSchema` property is queried; non-null values are merged into the `TableReadRegistry` via `registry.With(...)`. The merged registry is stored behind `MsiRecipeReader.Read(access)` and used for the lifetime of the decompile operation.

Firewall, IIS, SQL, and Dependency extensions each populate their existing `IMsiTableContributor` implementations with both `Schema` (Cycle 2 write fix) and `ReadSchema` (Cycle 4 read fix) properties. Their custom tables round-trip through decompile for the first time.

## Testing Strategy

**Replace, don't layer.** Zero isolated reader tests exist today; all new tests are additive. The existing integration tests through `MsiDecompiler.Decompile` remain as a safety net during cutover and are retired or reduced once per-schema tests cover the rule surface.

### New boundary tests to write

At the per-schema level (one test class per schema, approximately 3 tests per class, approximately 9 × 3 = 27 tests):

1. **Happy-path row mapping** — minimal `FakeMsiTableAccess` with one valid row, assert the mapped entry matches field-for-field.
2. **Multi-row mapping** — `FakeMsiTableAccess` with several rows, assert order preservation and field correctness.
3. **Nullable column handling** — rows with null cells for nullable columns, assert `StringOrNull` returns null.
4. **Shape mismatch** — row with wrong cell count, assert `TableReadEngine.ReadOne` fails with a structured error naming the schema's `DiagnosticCode`, the table name, the expected column count, and the actual count.
5. **Numeric parse failures** — cell that cannot parse as int, assert structured error naming the table, row index, and column.
6. **Enum column** — integer cell that maps to an enum value, assert correct enum decoding.

At the `MsiPackageReconstructor` level (cross-platform, pure, approximately 15 tests):

7. **Empty recipe produces empty PackageModel** — baseline correctness.
8. **Property-only recipe populates package name/version/manufacturer** — PKG metadata round-trip.
9. **Component-only recipe populates package components with correct directory refs**.
10. **Feature-only recipe populates feature tree with correct parent/child relationships**.
11. **FeatureComponents junction correctly wires features to components** — cross-table integrity.
12. **File table with sequence numbers** — file ordering preserved.
13. **Binary stream references preserved** — `StreamSource` values flow through.
14. **Registry entries with root enum** — registry decoding.
15. **Reconstructor returns structured error on malformed recipe** — orphan FK references, duplicate PKs.

At the `MsiRecipeReader` level (Windows-only integration, approximately 3 tests):

16. **End-to-end read of a real compiled MSI** — compile a minimal package, read it back with `MsiRecipeReader.Read`, assert recipe content matches.
17. **Extension custom table round-trip** — compile a package with the Firewall extension, decompile, assert `WixFirewallException` rows appear in the recipe.
18. **Recipe content hash stability** — two reads of the same MSI produce equal content hashes.

At the round-trip level (approximately 12 tests, one per demo fixture):

19-30. **Recipe-equality round-trip per demo** — build, execute, read, compare content hashes.

### Old tests to delete

None. Zero isolated reader tests exist. All new tests are additive. Integration tests in `MsiCompilerIntegrationTests` that exercise the decompile path remain as safety nets; they can be reduced once the new tests cover the rule surface with high confidence.

### Test environment needs

- `FalkForge.Testing` gains `FakeMsiTableAccess` (approximately 40 LOC) backed by an in-memory dict.
- No new NuGet packages. No new platform requirements. Per-schema tests and reconstructor tests run on Linux CI. Only the end-to-end `MsiRecipeReader` tests and the round-trip tests require Windows.

## Implementation Recommendations

Durable architectural guidance, decoupled from current file paths:

### What the module should own

- The `PackageModel → MsiDatabaseRecipe → PackageModel` round-trip via paired `MsiRecipeReader` + `MsiPackageReconstructor` stages.
- 9 built-in `TableReadSchema` records as the canonical declaration of how each MSI table maps to recipe rows. One static field per table, grep-able by name.
- The `ReadRow` ref struct as the zero-allocation type-safe column access primitive. Compile-time column safety via `ReadColumn` tokens.
- The `TableReadRegistry` as the immutable frozen-dictionary-backed lookup, composable via `With(...)` for extensions.
- The `TableReadEngine.Read` orchestrator as a pure function walking the registry against `IMsiTableAccess`.
- Extension integration via the new `IMsiTableContributor.ReadSchema` optional property.
- The decompile-to-recipe entry point (`MsiDecompiler.DecompileToRecipe`) for round-trip regression tests.
- The reconstructor stage as a pure cross-platform function from recipe to `PackageModel`, unit-testable on Linux without any `IMsiTableAccess`.

### What the module should hide

- The 9 copy-pasted `*TableReader` classes.
- Hardcoded `"DEC00X"` error code strings scattered across readers.
- Positional row indexing (`row[0]`, `row[1]`, `int.TryParse`).
- Per-reader duplicated orchestration (`TableExists` + `QueryTable` + iterate + accumulate).
- The distinction between built-in and extension-contributed tables — the registry merges both transparently.
- The hardcoded assumption that decompile always touches msi.dll — the reconstructor side is now cross-platform and msi.dll is isolated behind `IMsiTableAccess` and `MsiTableAccess`.

### What the module should expose

Two public surfaces:

1. **Facade** — `MsiDecompiler.Decompile(msiPath)` preserved for backwards compatibility. `MsiDecompiler.DecompileToRecipe(msiPath)` added for round-trip tests.
2. **Recipe pipeline** — `MsiRecipeReader.Read`, `MsiPackageReconstructor.Rebuild`, `TableReadSchema<T>` record, `ReadColumn`, `ReadRow`, `RowMapper<T>`, `TableReadRegistry`, `TableReadEngine.ReadOne` for tests. Used by tests, future round-trip tooling, analyzers, and extensions.

### How callers should migrate

**`MsiDecompiler.Decompile` callers** — no changes. The call site contract is preserved: `Result<PackageModel> Decompile(string path)`.

**CLI `forge decompile`** — no changes. Calls `MsiDecompiler.Decompile` as today.

**Extension authors with custom tables** (Firewall, IIS, SQL, Dependency) — two-phase migration: first populate `Schema` (Cycle 2 write fix), then populate `ReadSchema` (Cycle 4 read fix). Custom parallel validator silos are retired in favor of shared recipe types.

**Future round-trip test authors** — call `MsiDecompiler.DecompileToRecipe(path)` to get a `MsiDatabaseRecipe` directly, skip the reconstructor stage.

**Future analyzer or dry-run tool authors** — same: `MsiRecipeReader.Read(access)` returns a cross-platform-testable recipe that can be serialized (via Cycle 2's `RecipeJsonContext`), compared, or walked offline.

### Implementation sequencing

TDD-driven, each phase gets its own implementation plan file. Sketch of order:

1. **Add `FakeMsiTableAccess` to `FalkForge.Testing`** — failing-first test against a known row list, implement the in-memory dict.
2. **Define `ReadColumn`, `ReadRow`, `RowMapper<T>`, `TableReadSchema<T>`, `ITableReadSchema`** — pure value types only, no behavior. Tests assert record equality and `ReadRow` column access against a known span.
3. **Define `TableReadEngine.ReadOne<T>`** — failing-first test reads one row via a minimal schema and `FakeMsiTableAccess`, implements the single-schema path.
4. **Define `TableReadEngine.Read`** — failing-first test walks a registry with two schemas, asserts both produce rows.
5. **Port schemas one at a time, TDD** — start with `PropertySchema`. Failing-first per-schema unit test against `FakeMsiTableAccess`, implement the static field, green. Then `DirectorySchema`, `ComponentSchema`, `FileSchema`, `FeatureSchema`, `FeatureComponentsSchema`, `RegistrySchema`, `ServiceSchema`, `ShortcutSchema`, `UpgradeSchema`. One schema per commit.
6. **Stand up `MsiRecipeReader.Read` facade** — failing-first integration test reads a real compiled MSI and asserts the recipe contains expected tables. Requires a running Windows CI host.
7. **Stand up `MsiPackageReconstructor.Rebuild` skeleton** — failing-first test constructs an empty recipe, asserts reconstructor returns a minimal empty `PackageModel`.
8. **Port reconstructor stages, TDD** — one domain area per commit. Property metadata → Directory tree → Component graph → File map → Feature tree → FeatureComponents wiring → Registry entries → Services → Shortcuts → Upgrades. Each stage is a pure function over recipe rows producing domain DTOs. Tests use hand-built recipes, zero `IMsiTableAccess`.
9. **Wire `MsiDecompiler.Decompile` to the new pipeline** — shrinks to a short forwarder calling `MsiRecipeReader.Read` then `MsiPackageReconstructor.Rebuild`. Test that the existing integration tests still pass.
10. **Wire `MsiDecompiler.DecompileToRecipe`** — new entry point. Test that round-trip recipe-equality works for the hello-world demo.
11. **Add `ReadSchema` optional property to `IMsiTableContributor`** — default null, existing contributors unchanged. Failing-first test with a stub contributor.
12. **Populate `ReadSchema` in Firewall, IIS, SQL, Dependency contributors** — one extension per commit. Failing-first test for each: compile a package using the extension, decompile, assert the extension's custom table appears in the recipe.
13. **Delete the 9 old `*TableReader` classes** — one cleanup commit after all schemas are ported and tests pass.
14. **Add round-trip tests for each of the 12 demo fixtures** — recipe-equality first, byte-identical second with appropriate guards.
15. **Documentation** — update `docs/` with the read pipeline architecture and the extension contribution guide.

Each phase of the sequencing plan gets its own implementation plan file under `docs/plans/`, paired with this design document.
