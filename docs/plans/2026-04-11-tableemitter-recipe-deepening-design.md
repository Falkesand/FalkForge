# RFC: Deepen the MSI TableEmitter into a Recipe pipeline

**Status:** COMPLETED 2026-05-05 — see commits 1c40837 (cutover) and 0d853bd (legacy deletion)
**Author:** architectural review, 2026-04-11
**Scope:** `src/FalkForge.Compiler.Msi/`, `src/FalkForge.Extensibility/`, `tests/FalkForge.Compiler.Msi.Tests/`, `tests/FalkForge.Testing/`

## Problem

The MSI authoring pipeline in `src/FalkForge.Compiler.Msi/` is organized around a single 1765-LOC god class, `TableEmitter`. It contains 37 `Emit*` methods called in hardcoded sequential order from an `EmitAllTables(ResolvedPackage)` entry point. Each method writes directly into a real `MsiDatabase` via P/Invoke through `NativeMethods.Msi` LibraryImport. Its helpers — `MsiDatabase`, `MsiRecord`, `SummaryInfoWriter` — are thin passthrough wrappers over `msi.dll` with no caching, no intermediate form, and no substitutability.

The cross-table foreign-key invariants that MSI requires (Directory → Component → File, Feature + Component → FeatureComponents, File sequence → Media.LastSequence, Component → Registry/Service/Shortcut/Environment/LockPermissions, Feature → Condition/Upgrade/Assemblies) are enforced only by the order of calls in `EmitAllTables`. Nothing in code checks that a `Component.Directory_` value refers to a row that was emitted into the Directory table, or that a `File.Component_` value refers to a Component row. If anyone reorders the emission sequence or introduces a new cross-reference, orphan FKs slip through silently and surface later as `msi.dll` errors at install time.

The testability consequence is severe. `IMsiApi` provides a substitution seam for runtime MSI install (`MsiInstallProduct`), and `FakeMsiApi` lets runtime-install tests run fast and isolated. The authoring path has no equivalent abstraction. Every test that wants to assert "after compiling this package, the Property table contains these rows in this order" has to run on Windows, spawn `msi.dll`, write a real `.msi` file to disk, and open it back via query. That friction is why exactly one unit test exists for the entire `TableEmitter` surface — `TableEmitterCustomTableTests` — and it only covers the `ValidateCustomTableIdentifiers` static SQL-injection-defense method. There are zero unit tests asserting against table content. When an emission bug appears, the only diagnostic path is "read the `.msi` file back and compare."

A second consequence is reproducible-build drift. FalkForge supports reproducible builds (timestamps patched via `SummaryInfoPatcher` post-commit, deterministic GUID generation in `ComponentResolver`, stable file sequencing). The current pipeline has no way to assert "two compiles of the same input produced byte-identical table data" short of byte-comparing the two `.msi` files. Any drift in `TableEmitter` ordering or row construction that reproducibility testing would catch today has to wait for an integration test to notice.

A third consequence is a live extension bug. `IMsiTableContributor` in `src/FalkForge.Extensibility/` allows extensions to contribute custom MSI tables. The Firewall extension's `FirewallTableContributor` declares `TableName = "WixFirewallException"` and provides rows, but no code anywhere in the compiler creates the schema for that table. The contributor path is half-wired: rows are produced, but there is no `CREATE TABLE` for them to land in. The bug survives because the extension path is untested and the coupling between `TableEmitter.EmitCustomTables` and `IMsiTableContributor` is ambient rather than contractual.

Finally, navigability is bad. Answering "where does the `File.Sequence` column get populated, and how does `CabinetBuilder` know what order to pack files in" requires reading `TableEmitter.EmitFiles`, `TableEmitter.EmitMedia`, `CabinetBuilder.BuildCabinet`, and then inferring that both read `ResolvedPackage.Files` in the same iteration order. Nothing in code names or enforces the contract. Add a new Windows Installer table (Win11 added several), and the reader must find the right place in the 1765-LOC god class, invent a new `Emit*` method, and correctly slot it into the hardcoded FK sequence — with zero compiler help.

This is a shallow-module problem compounded by a missing testability seam. Deepening it will produce a pure, immutable intermediate value type (`MsiDatabaseRecipe`) that fully describes what `msi.dll` would write, plus a thin executor that applies the recipe to a real database. The recipe is the seam. Every other benefit — unit tests without `msi.dll`, automatic FK validation, reproducibility hashing, lazy stream handling, offline JSON dry-run export, MSM/MSP/MST pipeline reuse, the Firewall extension fix — falls out of that single structural change.

## Proposed Interface

The design splits the public surface into a static facade for the 95% case (the production `MsiCompiler` path) and a recipe-builder contract for tests, MSM/MSP/MST compilers, dry-run tooling, and future analyzers.

### Public facade — the 95% case

```csharp
namespace FalkForge.Compiler.Msi.Authoring;

/// <summary>
/// Compiles a PackageModel into an .msi file on disk. One entry point wraps
/// validation, component resolution, recipe building, cabinet construction,
/// recipe execution, summary info, commit, reproducible timestamp patching,
/// code signing, ICE validation, SBOM sidecar, and WinGet manifest generation.
/// </summary>
[SupportedOSPlatform("windows")]
public static class MsiAuthoring
{
    public static Result<string> Compile(PackageModel package, string outputPath);
}
```

Real `MsiCompiler.Compile` collapses to a one-line forwarder:

```csharp
[SupportedOSPlatform("windows")]
public sealed class MsiCompiler : ICompiler
{
    public Result<string> Compile(PackageModel package, string outputPath)
        => MsiAuthoring.Compile(package, outputPath);
}
```

### Recipe contract — the 5% case

Tests, MSM/MSP/MST compilers, headless analyzers, CI dry-run tooling, and future hosts use the recipe pipeline directly, bypassing the static facade.

```csharp
namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Immutable, platform-free description of everything that will be written to an MSI database.
/// Produced by MsiRecipeBuilder from a ResolvedPackage. Consumed by MsiRecipeExecutor
/// which applies it to a real msi.dll database.
///
/// Invariants enforced at construction:
///   - All foreign-key cell references resolve to an existing row in the target table.
///   - Primary keys are unique within each table.
///   - Custom table and column identifiers pass SQL-injection safety validation.
///   - File.Sequence values are contiguous from 1 and assigned deterministically.
///   - Tables appear in FK-safe order (parents before children).
/// </summary>
public sealed record MsiDatabaseRecipe
{
    public required ImmutableArray<RecipeTable> Tables { get; init; }
    public required SummaryInfoRecipe SummaryInfo { get; init; }
    public required ImmutableDictionary<string, StreamSource> Streams { get; init; }
    public required ImmutableArray<FileSequenceEntry> FileSequencing { get; init; }
    public CabinetEmbedding? CabinetEmbedding { get; init; }
    public required ReadOnlyMemory<byte> ContentHash { get; init; }
}

public sealed record RecipeTable
{
    public required TableId Name { get; init; }
    public required ImmutableArray<RecipeColumn> Columns { get; init; }
    public required ImmutableArray<RecipeRow> Rows { get; init; }
    public required ImmutableArray<ColumnIndex> PrimaryKey { get; init; }
    public required string CreateTableSql { get; init; }
    public required string InsertViewSql { get; init; }
}

public readonly record struct TableId
{
    public string Value { get; }
    public static Result<TableId> Create(string name);
}

public sealed record RecipeColumn
{
    public required string Name { get; init; }
    public required ColumnType Type { get; init; }
    public required int Width { get; init; }
    public required bool Nullable { get; init; }
    public required bool LocalizableKey { get; init; }
}

public enum ColumnType { Integer, String, Localized, Binary }

public readonly record struct ColumnIndex(int Value);

public sealed record RecipeRow
{
    public required ImmutableArray<CellValue> Cells { get; init; }
}

public abstract record CellValue
{
    public sealed record Null : CellValue;
    public sealed record IntValue(int Value) : CellValue;
    public sealed record StringValue(string Value) : CellValue;
    public sealed record ForeignKey(TableId TargetTable, string TargetKey) : CellValue;
    public sealed record StreamRef(string StreamName) : CellValue;
}

public abstract record StreamSource
{
    public abstract ReadOnlyMemory<byte> Sha256 { get; }
    public abstract long Length { get; }
    public abstract Stream Open();

    public sealed record FilePath(string Path, ReadOnlyMemory<byte> Sha256, long Length) : StreamSource;
    public sealed record InMemory(ReadOnlyMemory<byte> Bytes, ReadOnlyMemory<byte> Sha256) : StreamSource;
    public sealed record Factory(Func<Stream> OpenFactory, ReadOnlyMemory<byte> Sha256, long Length) : StreamSource;
}

public sealed record FileSequenceEntry(string FileId, int Sequence);
public sealed record CabinetEmbedding(string StreamName, StreamSource Source);
public sealed record SummaryInfoRecipe { /* title, subject, author, template, revision, etc. */ }
```

### Recipe builder — pure function, no I/O

```csharp
namespace FalkForge.Compiler.Msi.Recipe;

public static class MsiRecipeBuilder
{
    public static Result<MsiDatabaseRecipe> Build(
        ResolvedPackage resolved,
        IReadOnlyList<IMsiTableContributor> contributors,
        MsiRecipeBuildOptions options);
}

public sealed record MsiRecipeBuildOptions
{
    public FileSequencingStrategy Sequencing { get; init; } = FileSequencingStrategy.FileIdOrdinal;
    public bool EagerStreamHashing { get; init; } = true;
    public int MaxInMemoryStreamBytes { get; init; } = 256 * 1024;
}

public enum FileSequencingStrategy { FileIdOrdinal, ComponentThenFileId }
```

### Recipe executor — thin Windows-only P/Invoke loop

```csharp
namespace FalkForge.Compiler.Msi.Recipe;

[SupportedOSPlatform("windows")]
public sealed class MsiRecipeExecutor
{
    public MsiRecipeExecutor(MsiDatabase database);
    public Result<Unit> Apply(MsiDatabaseRecipe recipe);
}
```

The executor makes zero decisions. It walks the recipe's tables in order, issues `CREATE TABLE`, inserts each row via `MsiRecord`, registers each stream, writes summary info, and returns. Any failure from `msi.dll` here is an engine bug — the recipe believed it was valid.

### Per-table producer — pure function over ResolvedPackage

```csharp
namespace FalkForge.Compiler.Msi.Recipe;

internal interface ITableProducer
{
    TableSchema Schema { get; }
    Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context);
}

public sealed record TableSchema
{
    public required TableId Name { get; init; }
    public required ImmutableArray<RecipeColumn> Columns { get; init; }
    public required ImmutableArray<ColumnIndex> PrimaryKey { get; init; }
    public required ImmutableArray<ForeignKeySpec> ForeignKeys { get; init; }
}

public sealed record ForeignKeySpec(ColumnIndex SourceColumn, TableId TargetTable);

public sealed class RecipeBuildContext
{
    public ResolvedPackage Resolved { get; }
    public MsiRecipeBuildOptions Options { get; }
    public IReadOnlyDictionary<TableId, ImmutableArray<RecipeRow>> BuiltTables { get; }
    public IFileSequencer FileSequencer { get; }
    public IStreamRegistry Streams { get; }
}
```

Each of the 37 current `Emit*` methods becomes one small producer file (~50–150 LOC each) with a static `Schema` field and a pure `Produce` method. Topological ordering is derived from `ForeignKeySpec` declarations, not hardcoded in an orchestrator.

### What the deepened module owns

- `MsiDatabaseRecipe` as the hero immutable value type.
- Approximately 30 `ITableProducer` implementations, one per table, each with a static `TableSchema` and a pure `Produce(RecipeBuildContext)` method.
- `TableSchema` with declared `ForeignKeySpec[]` enabling automatic FK validation via `FrozenSet<string>` primary-key lookups.
- `CellValue` discriminated union (`Null`, `IntValue`, `StringValue`, `ForeignKey`, `StreamRef`).
- `StreamSource` discriminated union (`FilePath`, `InMemory`, `Factory`) with pre-computed SHA-256 for reproducibility and lazy `Open()` for memory safety.
- `RecipeContentHasher` producing a stable hash over the canonicalised recipe.
- `MsiRecipeBuilder.Build` as a pure function chaining producers in topological order, running FK and PK validators after all producers complete.
- `MsiRecipeExecutor.Apply` as a thin P/Invoke loop.
- `MsiAuthoring.Compile` as a static facade orchestrating validation, `ComponentResolver`, recipe building, cabinet construction, recipe `with { CabinetEmbedding = ... }` injection, execution, commit, reproducible timestamp patching, code signing, ICE validation, SBOM sidecar, and WinGet manifest generation.
- `RecipeDiff` helper for debug output and golden-snapshot tests.
- `RecipeJsonContext` source-generated JSON serializer for offline dry-run export.

### What the deepened module hides

- The entire 1765-LOC `TableEmitter` class. Deleted.
- `MsiTableDefinitions.cs` (241 LOC of SQL string constants). Deleted; schemas now live as static fields on producer classes.
- `MsiDatabase`, `MsiRecord`, `NativeMethods.Msi` — marked `internal`, only the executor touches them.
- All 37 `Emit*` methods. Replaced by small producer files.
- Hardcoded FK emission order. Derived automatically from `ForeignKeySpec` declarations.
- SQL identifier injection defense. Moved into `TableId.Create` and `RecipeColumn` constructors — defense at value creation rather than post-hoc sweep.
- The contract between `CabinetBuilder` and `TableEmitter` over `File.Sequence`. Now explicit via `MsiDatabaseRecipe.FileSequencing`.

## Dependency Strategy

The recipe builder is an **in-process pure function**. Its only input is `ResolvedPackage` (owned by `ComponentResolver`, which stays unchanged) plus a list of `IMsiTableContributor` extensions and a small options record. No ports, no adapters, no injection framework. The testability seam is the recipe itself: tests call `MsiRecipeBuilder.Build(resolved, [], options)` and assert on the returned `MsiDatabaseRecipe.Tables` and `MsiDatabaseRecipe.Streams` directly. No `msi.dll` is involved in any test that does not require an actual `.msi` file on disk.

Stream handling uses a lazy discriminated union. `StreamSource.FilePath` is preferred for cabinets and large binaries — the bytes never enter managed memory during recipe construction. `StreamSource.InMemory` is capped at `MaxInMemoryStreamBytes` (default 256 KB) to prevent memory blowup from pathological inputs. SHA-256 is computed eagerly during recipe construction (streaming read for `FilePath`) so that `ContentHash` can be computed without re-reading streams at compare time.

`MsiRecipeExecutor` is the only component that touches `msi.dll`. It is marked `[SupportedOSPlatform("windows")]` and wraps the existing `MsiDatabase` class unchanged. On Linux CI, test projects that reference only the recipe namespace (not the executor) compile and run without platform guards.

The extension contract `IMsiTableContributor` evolves surgically. One new optional property is added:

```csharp
namespace FalkForge.Extensibility;

public interface IMsiTableContributor
{
    string TableName { get; }
    IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context);

    /// <summary>
    /// Schema the contributor owns. Null means "row-only contributor" — the
    /// table must be created by some other contributor or by a built-in producer.
    /// Non-null means this contributor declares both schema and rows. The
    /// Firewall extension's WixFirewallException table requires this, as no
    /// built-in producer creates it today.
    /// </summary>
    TableSchema? Schema => null;
}
```

This is a source-compatible change — existing extensions continue to work with `Schema` returning null (legacy row-only behavior). The Firewall bug is fixed by populating `Schema` in `FirewallTableContributor`. Over time, extensions can migrate to full `ITableProducer` implementations if they want typed schema declarations.

## Testing Strategy

**Replace, don't layer.** The single existing `TableEmitterCustomTableTests` unit test moves into `TableId.Create` / `RecipeColumn` constructor tests — it still covers SQL injection defense, but now at the value-type boundary where it belongs. Every other test that currently requires running a full MSI compile gets replaced by per-producer unit tests.

### New boundary tests to write

At the recipe-builder level, using pure function calls and in-memory file systems:

1. **Property table content** — compile a package with `ProductCode`, `ProductName`, `ProductVersion`, assert `recipe.Tables["Property"]` contains the expected rows in the expected order with the expected cell values. No `msi.dll`.
2. **Component table FK correctness** — build a package with two components in different directories, assert that each `Component` row's `Directory_` cell is a `CellValue.ForeignKey` pointing to the correct Directory row.
3. **File sequence contiguity** — build a package with three files, assert `recipe.Tables["File"].Rows` has sequence values 1, 2, 3 and that `recipe.FileSequencing` lists them in the same order.
4. **FeatureComponents junction** — build a package with two features sharing one component, assert the FeatureComponents table has two rows both pointing to the shared component.
5. **Orphan Directory reference fails** — build a broken `ResolvedPackage` with a Component referencing a non-existent Directory, assert `MsiRecipeBuilder.Build` returns `Result.Failure(ErrorKind.Validation)` with a structured error message naming the offending row and the missing target.
6. **Orphan Component reference fails** — same shape but File → Component.
7. **Duplicate primary key fails** — two Property rows with the same Name, assert Build fails with a PK-violation error message.
8. **SQL injection in custom table name rejected** — custom table with `TableName = "Property; DROP TABLE Component;"`, assert `TableId.Create` rejects at recipe construction.
9. **SQL injection in custom column name rejected** — same via `RecipeColumn` constructor.
10. **Reproducibility hash stability** — two calls to `MsiRecipeBuilder.Build` with the same input produce recipes whose `ContentHash` spans are byte-equal.
11. **Reproducibility regression catches drift** — mutate one row between calls, assert `ContentHash` differs and `RecipeDiff.Compare` returns a structured difference naming the changed table and column.
12. **Golden snapshot for hello-world demo** — compile the `hello-world` demo, serialize its recipe to JSON via `RecipeJsonContext`, compare byte-for-byte to a committed golden file. Any change in emission — producer order, column layout, row content — fails this test loudly.
13. **Custom table with schema (Firewall fix)** — register a `FirewallTableContributor` with a populated `Schema`, compile a package using it, assert the `WixFirewallException` table appears in the recipe with the declared columns and contributed rows.
14. **Custom table without schema (legacy contributor)** — register a row-only contributor, assert Build fails gracefully with a message explaining that no producer declared the schema.
15. **Binary custom action with large stream** — recipe uses `StreamSource.FilePath` for the DLL, memory usage during Build stays bounded regardless of DLL size.

At the executor level, using real `msi.dll` on Windows CI only:

16. **Round-trip test** — compile the hello-world demo via `MsiAuthoring.Compile`, read the resulting `.msi` file back, assert the Property table content matches what `MsiRecipeBuilder.Build` produced. This is the single integration test that proves the recipe-to-`msi.dll` path is faithful.
17. **Byte-diff regression** — compile each of the 12 demo fixtures via both the old `TableEmitter` path (behind a feature flag during cutover) and the new recipe path, assert the resulting `.msi` files are byte-identical. Deleted after cutover.

### Old tests to delete

- `TableEmitterCustomTableTests` — SQL injection coverage moves into `TableId.Create` and `RecipeColumn` constructor tests.
- Any integration test that exists only because per-table content couldn't be unit-tested — replace with per-producer unit tests against the recipe.

### Test environment needs

- `FalkForge.Testing` gains a `RecipeTestHelpers` static class with `BuildRecipe(package)` convenience wrapper running `ComponentResolver` + `MsiRecipeBuilder.Build` in one call.
- `FalkForge.Testing` gains a `GoldenRecipeFixture` helper for JSON snapshot comparison.
- No new NuGet packages. No new platform requirements for most tests — the per-table unit tests run on Linux CI. Only the executor round-trip test (16) and the byte-diff regression test (17, temporary) require Windows.

## Implementation Recommendations

Durable architectural guidance, decoupled from current file paths:

### What the module should own

- The `PackageModel → MsiDatabaseRecipe` transformation end-to-end, including validation, FK resolution, PK uniqueness, SQL identifier sanitization, stream hashing, file sequencing, and content hashing.
- Per-table producers as first-class, independently testable units. Each producer owns its schema, its row projection, and its declared foreign-key relationships.
- The topological ordering derived from declared foreign keys — no human maintains a list of `Emit*` call order.
- The canonical content hash used for reproducibility testing.
- The static façade that production callers invoke for the 95% case.
- The thin executor that writes a recipe to a real `msi.dll` database.
- The `RecipeDiff` helper for debugging non-reproducible emissions.
- The JSON source-generated serializer for cross-platform analysis.

### What the module should hide

- Every one of the 37 current `Emit*` methods.
- The `MsiTableDefinitions` SQL string constants.
- The `MsiDatabase` and `MsiRecord` P/Invoke wrappers — internal.
- The hardcoded FK emission order — derived from declarations.
- SQL identifier validation as a post-hoc sweep — moved into value-type constructors.
- The implicit `CabinetBuilder` sequencing contract — now explicit via `MsiDatabaseRecipe.FileSequencing`.
- Error-code checking from every `msi.dll` call — encapsulated in the executor.

### What the module should expose

Two public surfaces:

1. **Static facade** — `MsiAuthoring.Compile(package, outputPath)` returning `Result<string>`. One method. Used by `MsiCompiler.Compile` and any other 95% caller.
2. **Recipe pipeline** — `MsiRecipeBuilder.Build` (pure, cross-platform), `MsiRecipeExecutor.Apply` (Windows-only), and the `MsiDatabaseRecipe` value type. Used by tests, `MsmCompiler`, `PatchCompiler`, `TransformCompiler`, future analyzers, and dry-run tooling.

### How callers should migrate

**`MsiCompiler.Compile`** becomes a one-line forwarder. No behavior change from the outside.

**`MsmCompiler`, `PatchCompiler`, `TransformCompiler`** migrate to call `MsiRecipeBuilder.Build` directly with subset-specific producer lists (merge-module-only producers, patch-transform producers, etc.), then run their own executor variants. They stop duplicating emission logic and share the ~30 built-in producers.

**Test callers** that today stand up `TableEmitter` against a real `MsiDatabase` are rewritten to call `MsiRecipeBuilder.Build` and assert on the recipe directly. Windows-only integration tests become cross-platform unit tests.

**Extensions with custom tables** (Firewall, IIS, SQL, Dependency) migrate in two stages: first, populate the new optional `Schema` property on existing `IMsiTableContributor` implementations (fixes the Firewall bug immediately); second, optionally rewrite as native `ITableProducer` implementations for full typed schema declarations.

### Implementation sequencing

The refactor is substantial and must land through a TDD-driven plan with the commit sequence gates enforced per `CLAUDE.md`. Sketch of order:

1. **Define recipe value types** — `MsiDatabaseRecipe`, `RecipeTable`, `RecipeRow`, `CellValue`, `StreamSource`, `TableId`, `RecipeColumn`, `SummaryInfoRecipe`, `FileSequenceEntry`, `CabinetEmbedding`. Value types only, zero behavior. Failing-first tests on `TableId.Create` SQL injection defense.
2. **Define `TableSchema` and `ForeignKeySpec`** — declarative schema descriptors used by producers.
3. **Stand up `MsiRecipeBuilder.Build` skeleton** — empty pipeline, no producers, emits an empty recipe. Write failing test that producing an empty recipe for an empty package succeeds.
4. **Port producers one at a time, TDD** — start with `PropertyTableProducer`, write a failing test asserting Property rows appear in the recipe, implement the producer, green. Commit. Repeat for Directory, Component, File, Feature, FeatureComponents, Media, then the rest. Each producer is one or two commits (test + implementation).
5. **FK and PK validators** — after each producer lands, add validator tests that assert orphan references and duplicate PKs fail recipe construction with structured errors.
6. **`RecipeContentHasher`** — write reproducibility hash tests. Two-run-same-input-equal-hash, mutate-input-differs-hash.
7. **`MsiRecipeExecutor`** — thin P/Invoke wrapper over existing `MsiDatabase`. One round-trip test on Windows asserting recipe → `.msi` → read-back equivalence.
8. **`MsiAuthoring.Compile` facade** — static orchestration wrapping validation, `ComponentResolver`, `MsiRecipeBuilder`, `CabinetBuilder`, recipe `with` clone, `MsiRecipeExecutor`, commit, post-processing. Behind feature flag.
9. **Byte-diff CI** — for each of the 12 demo fixtures, compile with old `TableEmitter` and new recipe path, assert byte-identical `.msi` output. This gate must be green before cutover.
10. **`IMsiTableContributor` schema extension** — add optional `Schema` property. Fix Firewall extension in the same commit. Failing-first test that `WixFirewallException` table appears in the compiled MSI.
11. **Migrate `MsmCompiler`, `PatchCompiler`, `TransformCompiler`** — one at a time, replace their duplicated emission logic with recipe-builder calls using merge-module / patch / transform producer subsets.
12. **Flip default and delete legacy** — remove the feature flag, delete `TableEmitter.cs`, `MsiTableDefinitions.cs`, `TableEmitterCustomTableTests.cs` (tests already moved to `TableId.Create` unit tests). One cleanup commit.
13. **JSON dry-run export** — add `RecipeJsonContext` source-generated serializer. Add `forge build --dry-run --recipe-json out.json` CLI flag. Add golden-snapshot test for hello-world.
14. **Documentation** — update `docs/` with the recipe architecture and contribute-a-new-table guide.

---

## Implementation Postscript (2026-05-05)

Actual execution order deviated from the design plan above, which ordered byte-diff CI gating before producers. In practice:

1. **Producers first** — all 39 table producers were implemented incrementally under TDD (one failing test → one producer commit per table). The recipe pipeline types (`MsiDatabaseRecipe`, `MsiRecipeBuilder`, `MsiRecipeExecutor`, `MsiAuthoring`) were built alongside, not after.
2. **No explicit byte-diff harness** — parity was validated by running the full test suite (compile + read-back integration tests) against each producer before flipping the default. The byte-diff CI gate (step 9) was effectively replaced by this test coverage.
3. **Cutover** — commit 1c40837 removed the feature flag and made `MsiAuthoring.Compile` the sole compilation path. `MsiCompiler.Compile` became a one-line forwarder.
4. **Legacy deletion** — commits 396c4f6 (`TableEmitter.cs` deleted) and 0d853bd (`DialogEmitter.cs` deleted) removed the legacy emitters. `MsiTableDefinitions.cs` was retained as `TableId.cs` constants only.
5. **Steps 11–14** (MSM/MSP/MST migration, JSON dry-run, full docs) remain open follow-on work.

Each phase of the sequencing plan gets its own implementation plan file under `docs/plans/`, paired with this design document.
